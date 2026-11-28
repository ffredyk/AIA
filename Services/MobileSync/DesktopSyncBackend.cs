using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AIA.Models;
using AIA.Sync.Contracts;
using AIA.Sync.Core;

namespace AIA.Services.MobileSync
{
    /// <summary>
    /// Bridges the sync engine to the live WPF collections. Every collection read or write is
    /// marshalled onto the UI dispatcher.
    /// </summary>
    public sealed class DesktopSyncBackend : ISyncBackend
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public DesktopSyncBackend(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        public async Task<SyncSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var viewModel = _viewModelProvider();
            return await InvokeOnUiAsync(() => BuildSnapshot(viewModel)).ConfigureAwait(false);
        }

        public async Task ApplyChangesAsync(IReadOnlyList<SyncEntityChange> changes, CancellationToken cancellationToken)
        {
            var viewModel = _viewModelProvider();

            await InvokeOnUiAsync(async () =>
            {
                ApplyTaskChanges(viewModel, changes);
                ApplyReminderChanges(viewModel, changes);
                ApplyCalendarEventChanges(viewModel, changes);

                viewModel.ApplyTaskFilter();
                viewModel.RefreshCalendarEvents();
                await viewModel.SaveTasksAndRemindersAsync().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }

        private static SyncSnapshot BuildSnapshot(OverlayViewModel viewModel)
        {
            var snapshot = new SyncSnapshot();

            foreach (var task in FlattenTasks(viewModel.Tasks))
            {
                snapshot.Entities.Add(new SyncSnapshotEntity
                {
                    EntityType = SyncEntityType.Task,
                    Id = task.Id,
                    Data = JsonSerializer.SerializeToElement(ToDto(task), SyncJson.Options)
                });
            }

            foreach (var reminder in viewModel.Reminders)
            {
                snapshot.Entities.Add(new SyncSnapshotEntity
                {
                    EntityType = SyncEntityType.Reminder,
                    Id = reminder.Id,
                    Data = JsonSerializer.SerializeToElement(ToDto(reminder), SyncJson.Options)
                });
            }

            foreach (var calendarEvent in viewModel.CalendarEvents)
            {
                snapshot.Entities.Add(new SyncSnapshotEntity
                {
                    EntityType = SyncEntityType.CalendarEvent,
                    Id = calendarEvent.Id,
                    Data = JsonSerializer.SerializeToElement(ToDto(calendarEvent), SyncJson.Options)
                });
            }

            return snapshot;
        }

        private static IEnumerable<TaskItem> FlattenTasks(IEnumerable<TaskItem> roots)
        {
            foreach (var task in roots)
            {
                yield return task;

                foreach (var child in FlattenTasks(task.Subtasks))
                {
                    yield return child;
                }
            }
        }

        private static TaskDto ToDto(TaskItem task)
        {
            return new TaskDto
            {
                Id = task.Id,
                ParentTaskId = task.ParentTaskId,
                Title = task.Title,
                Description = task.Description,
                Notes = task.Notes,
                Status = (SyncTaskStatus)(int)task.Status,
                Priority = (SyncTaskPriority)(int)task.Priority,
                CreatedDate = task.CreatedDate,
                DueDate = task.DueDate,
                CompletedDate = task.CompletedDate,
                IsArchived = task.IsArchived,
                RecurrenceType = (SyncRecurrenceType)(int)task.RecurrenceType,
                RecurrenceInterval = task.RecurrenceInterval,
                RecurrenceEndDate = task.RecurrenceEndDate,
                Tags = task.Tags.ToList(),
                DependencyTaskIds = task.DependencyTaskIds.ToList()
            };
        }

        private static ReminderDto ToDto(ReminderItem reminder)
        {
            return new ReminderDto
            {
                Id = reminder.Id,
                Title = reminder.Title,
                DueDate = reminder.DueDate,
                Severity = (SyncReminderSeverity)(int)reminder.Severity,
                CreatedDate = reminder.CreatedDate,
                IsCompleted = reminder.IsCompleted
            };
        }

        private static CalendarEventDto ToDto(CalendarEventItem calendarEvent)
        {
            return new CalendarEventDto
            {
                Id = calendarEvent.Id,
                Title = calendarEvent.Title,
                Description = calendarEvent.Description,
                Location = calendarEvent.Location,
                Start = calendarEvent.Start,
                End = calendarEvent.End,
                IsAllDay = calendarEvent.IsAllDay,
                RecurrenceType = (SyncRecurrenceType)(int)calendarEvent.RecurrenceType,
                RecurrenceInterval = calendarEvent.RecurrenceInterval,
                RecurrenceEndDate = calendarEvent.RecurrenceEndDate,
                ReminderMinutesBefore = calendarEvent.ReminderMinutesBefore,
                CreatedDate = calendarEvent.CreatedDate
            };
        }

        private static void ApplyTaskChanges(OverlayViewModel viewModel, IReadOnlyList<SyncEntityChange> changes)
        {
            var taskChanges = changes.Where(c => c.EntityType == SyncEntityType.Task).ToList();
            if (taskChanges.Count == 0)
                return;

            foreach (var change in taskChanges.Where(c => c.IsDeleted))
            {
                var task = FindTask(viewModel.Tasks, change.Id);
                if (task != null)
                {
                    RemoveTask(viewModel, task);
                }
            }

            var upserts = new List<(SyncEntityChange Change, TaskDto Dto)>();
            foreach (var change in taskChanges.Where(c => !c.IsDeleted))
            {
                var dto = Deserialize<TaskDto>(change);
                if (dto.ParentTaskId == dto.Id)
                    dto.ParentTaskId = null;

                upserts.Add((change, dto));
            }

            var dtoById = upserts
                .GroupBy(x => x.Dto.Id)
                .ToDictionary(g => g.Key, g => g.Last().Dto);

            foreach (var pair in upserts.OrderBy(x => GetTaskDepth(x.Dto, dtoById)).ThenBy(x => x.Dto.CreatedDate))
            {
                ApplyTask(viewModel, pair.Dto);
            }
        }

        private static int GetTaskDepth(TaskDto dto, IReadOnlyDictionary<Guid, TaskDto> known)
        {
            var depth = 0;
            var parentId = dto.ParentTaskId;
            var guard = new HashSet<Guid> { dto.Id };

            while (parentId.HasValue && known.TryGetValue(parentId.Value, out var parent) && guard.Add(parent.Id))
            {
                depth++;
                parentId = parent.ParentTaskId;
            }

            return depth;
        }

        private static void ApplyTask(OverlayViewModel viewModel, TaskDto dto)
        {
            var existing = FindTask(viewModel.Tasks, dto.Id);

            if (existing == null)
            {
                var created = new TaskItem { Id = dto.Id };
                CopyTaskFields(dto, created);
                AttachTask(viewModel, created, dto.ParentTaskId);
                return;
            }

            var reparent = existing.ParentTaskId != dto.ParentTaskId;
            CopyTaskFields(dto, existing);

            if (reparent)
            {
                DetachTask(viewModel, existing);
                AttachTask(viewModel, existing, dto.ParentTaskId);
            }
            else if (existing.ParentTaskId.HasValue)
            {
                existing.RefreshSubtaskProgress();
            }
        }

        private static void CopyTaskFields(TaskDto dto, TaskItem task)
        {
            task.Title = dto.Title;
            task.Description = dto.Description;
            task.Notes = dto.Notes;
            task.Status = (AIA.Models.TaskStatus)(int)dto.Status;
            task.Priority = (TaskPriority)(int)dto.Priority;
            task.CreatedDate = dto.CreatedDate;
            task.DueDate = dto.DueDate;
            task.CompletedDate = dto.CompletedDate;
            task.IsArchived = dto.IsArchived;
            task.RecurrenceType = (RecurrenceType)(int)dto.RecurrenceType;
            task.RecurrenceInterval = Math.Max(1, dto.RecurrenceInterval);
            task.RecurrenceEndDate = dto.RecurrenceEndDate;

            task.Tags.Clear();
            foreach (var tag in dto.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                task.Tags.Add(tag);
            }

            task.DependencyTaskIds.Clear();
            foreach (var dependencyId in dto.DependencyTaskIds.Where(id => id != task.Id))
            {
                task.DependencyTaskIds.Add(dependencyId);
            }
        }

        private static void AttachTask(OverlayViewModel viewModel, TaskItem task, Guid? parentId)
        {
            if (parentId.HasValue)
            {
                var parent = FindTask(viewModel.Tasks, parentId.Value);
                if (parent != null && parent.Id != task.Id)
                {
                    parent.AddSubtask(task);
                    parent.RefreshSubtaskProgress();
                    return;
                }
            }

            task.ParentTaskId = null;
            if (!viewModel.Tasks.Contains(task))
                viewModel.Tasks.Add(task);
        }

        private static void DetachTask(OverlayViewModel viewModel, TaskItem task)
        {
            if (task.ParentTaskId.HasValue)
            {
                var parent = FindTask(viewModel.Tasks, task.ParentTaskId.Value);
                if (parent != null)
                {
                    parent.Subtasks.Remove(task);
                    parent.RefreshSubtaskProgress();
                    return;
                }
            }

            viewModel.Tasks.Remove(task);
        }

        private static void RemoveTask(OverlayViewModel viewModel, TaskItem task)
        {
            if (viewModel.SelectedTask == task)
                viewModel.SelectedTask = null;

            foreach (var child in task.Subtasks.ToList())
            {
                RemoveTask(viewModel, child);
            }

            DetachTask(viewModel, task);
        }

        private static TaskItem? FindTask(IEnumerable<TaskItem> roots, Guid id)
        {
            foreach (var task in roots)
            {
                if (task.Id == id)
                    return task;

                var child = FindTask(task.Subtasks, id);
                if (child != null)
                    return child;
            }

            return null;
        }

        private static void ApplyReminderChanges(OverlayViewModel viewModel, IReadOnlyList<SyncEntityChange> changes)
        {
            foreach (var change in changes.Where(c => c.EntityType == SyncEntityType.Reminder))
            {
                if (change.IsDeleted)
                {
                    var existing = viewModel.Reminders.FirstOrDefault(r => r.Id == change.Id);
                    if (existing != null)
                        viewModel.Reminders.Remove(existing);
                    continue;
                }

                var dto = Deserialize<ReminderDto>(change);
                var reminder = viewModel.Reminders.FirstOrDefault(r => r.Id == dto.Id);
                if (reminder == null)
                {
                    reminder = new ReminderItem { Id = dto.Id };
                    viewModel.Reminders.Add(reminder);
                }

                reminder.Title = dto.Title;
                reminder.DueDate = dto.DueDate;
                reminder.Severity = (ReminderSeverity)(int)dto.Severity;
                reminder.CreatedDate = dto.CreatedDate;
                reminder.IsCompleted = dto.IsCompleted;
            }
        }

        private static void ApplyCalendarEventChanges(OverlayViewModel viewModel, IReadOnlyList<SyncEntityChange> changes)
        {
            foreach (var change in changes.Where(c => c.EntityType == SyncEntityType.CalendarEvent))
            {
                if (change.IsDeleted)
                {
                    var existing = viewModel.CalendarEvents.FirstOrDefault(e => e.Id == change.Id);
                    if (existing != null)
                        viewModel.CalendarEvents.Remove(existing);
                    continue;
                }

                var dto = Deserialize<CalendarEventDto>(change);
                var calendarEvent = viewModel.CalendarEvents.FirstOrDefault(e => e.Id == dto.Id);
                if (calendarEvent == null)
                {
                    calendarEvent = new CalendarEventItem { Id = dto.Id };
                    viewModel.CalendarEvents.Add(calendarEvent);
                }

                calendarEvent.Title = dto.Title;
                calendarEvent.Description = dto.Description;
                calendarEvent.Location = dto.Location;
                calendarEvent.Start = dto.Start;
                calendarEvent.End = dto.End;
                calendarEvent.IsAllDay = dto.IsAllDay;
                calendarEvent.RecurrenceType = (RecurrenceType)(int)dto.RecurrenceType;
                calendarEvent.RecurrenceInterval = Math.Max(1, dto.RecurrenceInterval);
                calendarEvent.RecurrenceEndDate = dto.RecurrenceEndDate;
                calendarEvent.ReminderMinutesBefore = dto.ReminderMinutesBefore;
                calendarEvent.CreatedDate = dto.CreatedDate;
            }
        }

        private static T Deserialize<T>(SyncEntityChange change) where T : class
        {
            if (change.Data == null)
                throw new SyncRequestException($"Entity {change.Id} has no data.");

            var dto = change.Data.Value.Deserialize<T>(SyncJson.Options);
            if (dto == null)
                throw new SyncRequestException($"Entity {change.Id} could not be deserialized.");

            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty?.GetValue(dto) is Guid dtoId && dtoId != change.Id)
                throw new SyncRequestException($"Entity id mismatch for {change.Id}.");

            return dto;
        }

        private static async Task InvokeOnUiAsync(Func<Task> action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                await action().ConfigureAwait(true);
                return;
            }

            await dispatcher.InvokeAsync(action).Task.Unwrap().ConfigureAwait(true);
        }

        private static async Task<T> InvokeOnUiAsync<T>(Func<T> action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                return action();

            return await dispatcher.InvokeAsync(action);
        }
    }
}
