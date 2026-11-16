using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service for persisting tasks and reminders to disk
    /// </summary>
    public static class TaskReminderService
    {
        private static string DataFolder => AppPaths.UserDataFolder;
        private static string TasksFile => Path.Combine(DataFolder, "tasks.json");
        private static string RemindersFile => Path.Combine(DataFolder, "reminders.json");

        /// <summary>
        /// True when task or reminder data has ever been persisted. Used to distinguish a real
        /// first run from a failed load or a user who deleted everything.
        /// </summary>
        public static bool HasStoredData => File.Exists(TasksFile) || File.Exists(RemindersFile);

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        #region Tasks

        /// <summary>
        /// Loads tasks from disk
        /// </summary>
        public static async Task<List<TaskItem>> LoadTasksAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(TasksFile))
            {
                return new List<TaskItem>();
            }

            try
            {
                var taskDtos = await JsonFileStore.ReadAsync<List<TaskItemDto>>(TasksFile, GetJsonOptions());

                if (taskDtos == null)
                    return new List<TaskItem>();

                return taskDtos.Select(ConvertFromDto).ToList();
            }
            catch (Exception ex)
            {
                // Never report "no tasks" for a file that exists but cannot be parsed: callers
                // treat an empty list as a first run and would overwrite the user's data.
                throw new DataLoadException(TasksFile, JsonFileStore.Quarantine(TasksFile), ex);
            }
        }

        /// <summary>
        /// Saves tasks to disk
        /// </summary>
        public static async Task SaveTasksAsync(IEnumerable<TaskItem> tasks)
        {
            EnsureDirectoryExists();

            var taskDtos = tasks.Select(ConvertToDto).ToList();
            await JsonFileStore.WriteAsync(TasksFile, taskDtos, GetJsonOptions());
        }

        /// <summary>
        /// Persists tasks and reminders in one serialized pass.
        /// </summary>
        public static async Task SaveAllAsync(IEnumerable<TaskItem> tasks, IEnumerable<ReminderItem> reminders)
        {
            await SaveTasksAsync(tasks);
            await SaveRemindersAsync(reminders);
        }

        private static TaskItemDto ConvertToDto(TaskItem task)
        {
            return new TaskItemDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Notes = task.Notes,
                Status = task.Status,
                Priority = task.Priority,
                CreatedDate = task.CreatedDate,
                DueDate = task.DueDate,
                CompletedDate = task.CompletedDate,
                ParentTaskId = task.ParentTaskId,
                IsArchived = task.IsArchived,
                RecurrenceType = task.RecurrenceType,
                RecurrenceInterval = task.RecurrenceInterval,
                RecurrenceEndDate = task.RecurrenceEndDate,
                Tags = task.Tags.ToList(),
                DependencyTaskIds = task.DependencyTaskIds.ToList(),
                Subtasks = task.Subtasks.Select(ConvertToDto).ToList()
            };
        }

        private static TaskItem ConvertFromDto(TaskItemDto dto)
        {
            var task = new TaskItem
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                Notes = dto.Notes,
                Status = dto.Status,
                Priority = dto.Priority,
                CreatedDate = dto.CreatedDate,
                DueDate = dto.DueDate,
                CompletedDate = dto.CompletedDate,
                ParentTaskId = dto.ParentTaskId,
                IsArchived = dto.IsArchived,
                RecurrenceType = dto.RecurrenceType,
                RecurrenceInterval = dto.RecurrenceInterval,
                RecurrenceEndDate = dto.RecurrenceEndDate
            };

            foreach (var tag in dto.Tags)
            {
                task.Tags.Add(tag);
            }

            foreach (var dependencyId in dto.DependencyTaskIds)
            {
                task.DependencyTaskIds.Add(dependencyId);
            }

            foreach (var subtaskDto in dto.Subtasks)
            {
                var subtask = ConvertFromDto(subtaskDto);
                subtask.ParentTaskId = task.Id;
                task.Subtasks.Add(subtask);
            }

            return task;
        }

        #endregion

        #region Reminders

        /// <summary>
        /// Loads reminders from disk
        /// </summary>
        public static async Task<List<ReminderItem>> LoadRemindersAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(RemindersFile))
            {
                return new List<ReminderItem>();
            }

            try
            {
                var reminderDtos = await JsonFileStore.ReadAsync<List<ReminderItemDto>>(RemindersFile, GetJsonOptions());

                if (reminderDtos == null)
                    return new List<ReminderItem>();

                return reminderDtos.Select(ConvertFromDto).ToList();
            }
            catch (Exception ex)
            {
                throw new DataLoadException(RemindersFile, JsonFileStore.Quarantine(RemindersFile), ex);
            }
        }

        /// <summary>
        /// Saves reminders to disk
        /// </summary>
        public static async Task SaveRemindersAsync(IEnumerable<ReminderItem> reminders)
        {
            EnsureDirectoryExists();

            var reminderDtos = reminders.Select(ConvertToDto).ToList();
            await JsonFileStore.WriteAsync(RemindersFile, reminderDtos, GetJsonOptions());
        }

        private static ReminderItemDto ConvertToDto(ReminderItem reminder)
        {
            return new ReminderItemDto
            {
                Id = reminder.Id,
                Title = reminder.Title,
                DueDate = reminder.DueDate,
                Severity = reminder.Severity,
                CreatedDate = reminder.CreatedDate,
                IsCompleted = reminder.IsCompleted
            };
        }

        private static ReminderItem ConvertFromDto(ReminderItemDto dto)
        {
            return new ReminderItem
            {
                Id = dto.Id,
                Title = dto.Title,
                DueDate = dto.DueDate,
                Severity = dto.Severity,
                CreatedDate = dto.CreatedDate,
                IsCompleted = dto.IsCompleted
            };
        }

        #endregion

        #region DTOs for serialization

        private class TaskItemDto
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public Models.TaskStatus Status { get; set; }
            public TaskPriority Priority { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime? DueDate { get; set; }
            public DateTime? CompletedDate { get; set; }
            public Guid? ParentTaskId { get; set; }
            public bool IsArchived { get; set; }
            public RecurrenceType RecurrenceType { get; set; }
            public int RecurrenceInterval { get; set; } = 1;
            public DateTime? RecurrenceEndDate { get; set; }
            public List<string> Tags { get; set; } = new();
            public List<Guid> DependencyTaskIds { get; set; } = new();
            public List<TaskItemDto> Subtasks { get; set; } = new();
        }

        private class ReminderItemDto
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public DateTime DueDate { get; set; }
            public ReminderSeverity Severity { get; set; }
            public DateTime CreatedDate { get; set; }
            public bool IsCompleted { get; set; }
        }

        #endregion
    }
}
