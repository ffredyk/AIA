using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of task service that bridges to OverlayViewModel
    /// </summary>
    public class HostTaskService : ITaskService
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public event EventHandler<TasksChangedEventArgs>? TasksChanged;

        public HostTaskService(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        private OverlayViewModel ViewModel => _viewModelProvider();

        public IReadOnlyList<ITaskItem> GetAllTasks()
        {
            return ViewModel.Tasks.Select(t => new TaskItemAdapter(t, this)).ToList();
        }

        public ITaskItem? GetTaskById(Guid id)
        {
            var task = ViewModel.FindTaskById(id);
            return task != null ? new TaskItemAdapter(task, this) : null;
        }

        public ITaskItem CreateTask(string title, string? description = null)
        {
            var task = new TaskItem
            {
                Title = title,
                Description = description ?? string.Empty,
                Status = Models.TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };

            ViewModel.Tasks.Add(task);
            TasksChanged?.Invoke(this, new TasksChangedEventArgs(TaskChangeType.Added, new TaskItemAdapter(task, this)));

            return new TaskItemAdapter(task, this);
        }

        public bool DeleteTask(Guid id)
        {
            var task = ViewModel.FindTaskById(id);
            if (task == null) return false;

            ViewModel.DeleteTask(task);
            TasksChanged?.Invoke(this, new TasksChangedEventArgs(TaskChangeType.Deleted));
            return true;
        }

        public async Task SaveAsync()
        {
            await ViewModel.SaveTasksAndRemindersAsync();
        }

        internal void RaiseTasksChanged(TaskChangeType changeType, ITaskItem? task = null)
        {
            TasksChanged?.Invoke(this, new TasksChangedEventArgs(changeType, task));
        }
    }

    /// <summary>
    /// Adapter that wraps TaskItem for plugin consumption
    /// </summary>
    internal class TaskItemAdapter : ITaskItem
    {
        private readonly TaskItem _task;
        private readonly HostTaskService _service;

        public TaskItemAdapter(TaskItem task, HostTaskService service)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public Guid Id => _task.Id;

        public string Title
        {
            get => _task.Title;
            set
            {
                _task.Title = value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public string Description
        {
            get => _task.Description;
            set
            {
                _task.Description = value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public string Notes
        {
            get => _task.Notes;
            set
            {
                _task.Notes = value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public TaskItemStatus Status
        {
            get => (TaskItemStatus)(int)_task.Status;
            set
            {
                _task.Status = (Models.TaskStatus)(int)value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public TaskItemPriority Priority
        {
            get => (TaskItemPriority)(int)_task.Priority;
            set
            {
                _task.Priority = (TaskPriority)(int)value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public DateTime CreatedDate => _task.CreatedDate;

        public DateTime? DueDate
        {
            get => _task.DueDate;
            set
            {
                _task.DueDate = value;
                _service.RaiseTasksChanged(TaskChangeType.Updated, this);
            }
        }

        public DateTime? CompletedDate => _task.CompletedDate;

        public bool IsCompleted => _task.IsCompleted;

        public bool IsOverdue => _task.IsOverdue;

        public IReadOnlyList<ITaskItem> Subtasks =>
            _task.Subtasks.Select(s => new TaskItemAdapter(s, _service)).ToList();

        public ITaskItem AddSubtask(string title)
        {
            var subtask = new TaskItem
            {
                Title = title,
                Status = Models.TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };

            _task.AddSubtask(subtask);
            var adapter = new TaskItemAdapter(subtask, _service);
            _service.RaiseTasksChanged(TaskChangeType.Added, adapter);

            return adapter;
        }
    }
}
