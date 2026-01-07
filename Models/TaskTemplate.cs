using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AIA.Models
{
    /// <summary>
    /// Represents a reusable task template
    /// </summary>
    public class TaskTemplate : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _title = string.Empty;
        private string _taskDescription = string.Empty;
        private TaskPriority _defaultPriority = TaskPriority.Medium;
        private int? _relativeDueDateDays;
        private List<string> _tags = new List<string>();
        private ObservableCollection<SubtaskTemplate> _subtasks = new ObservableCollection<SubtaskTemplate>();
        private DateTime _createdDate;
        private DateTime _lastUsedDate;
        private int _useCount;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique identifier for the template
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// Display name of the template
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// Description of what this template is for
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        /// <summary>
        /// Default title for tasks created from this template
        /// </summary>
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        /// <summary>
        /// Default task description
        /// </summary>
        public string TaskDescription
        {
            get => _taskDescription;
            set { _taskDescription = value; OnPropertyChanged(nameof(TaskDescription)); }
        }

        /// <summary>
        /// Default priority for tasks
        /// </summary>
        public TaskPriority DefaultPriority
        {
            get => _defaultPriority;
            set { _defaultPriority = value; OnPropertyChanged(nameof(DefaultPriority)); }
        }

        /// <summary>
        /// Relative due date in days from task creation (null = no due date)
        /// </summary>
        public int? RelativeDueDateDays
        {
            get => _relativeDueDateDays;
            set { _relativeDueDateDays = value; OnPropertyChanged(nameof(RelativeDueDateDays)); }
        }

        /// <summary>
        /// Default tags for tasks
        /// </summary>
        public List<string> Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        /// <summary>
        /// Subtasks to be created with the task
        /// </summary>
        public ObservableCollection<SubtaskTemplate> Subtasks
        {
            get => _subtasks;
            set { _subtasks = value; OnPropertyChanged(nameof(Subtasks)); }
        }

        /// <summary>
        /// When this template was created
        /// </summary>
        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        /// <summary>
        /// When this template was last used
        /// </summary>
        public DateTime LastUsedDate
        {
            get => _lastUsedDate;
            set { _lastUsedDate = value; OnPropertyChanged(nameof(LastUsedDate)); }
        }

        /// <summary>
        /// How many times this template has been used
        /// </summary>
        public int UseCount
        {
            get => _useCount;
            set { _useCount = value; OnPropertyChanged(nameof(UseCount)); }
        }

        public TaskTemplate()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            LastUsedDate = DateTime.Now;
        }

        /// <summary>
        /// Creates a task from this template
        /// </summary>
        public TaskItem CreateTask()
        {
            var task = new TaskItem
            {
                Title = Title,
                Description = TaskDescription,
                Priority = DefaultPriority,
                Status = TaskStatus.NotStarted
            };

            // Set relative due date
            if (RelativeDueDateDays.HasValue)
            {
                task.DueDate = DateTime.Now.AddDays(RelativeDueDateDays.Value);
            }

            // Add tags
            foreach (var tag in Tags)
            {
                task.Tags.Add(tag);
            }

            // Add subtasks
            foreach (var subtaskTemplate in Subtasks)
            {
                var subtask = new TaskItem
                {
                    Title = subtaskTemplate.Title,
                    Description = subtaskTemplate.Description,
                    Priority = subtaskTemplate.Priority,
                    Status = TaskStatus.NotStarted
                };

                // Set relative due date for subtask
                if (subtaskTemplate.RelativeDueDateDays.HasValue)
                {
                    subtask.DueDate = DateTime.Now.AddDays(subtaskTemplate.RelativeDueDateDays.Value);
                }

                task.AddSubtask(subtask);
            }

            // Update usage stats
            LastUsedDate = DateTime.Now;
            UseCount++;

            return task;
        }

        /// <summary>
        /// Creates a template from an existing task
        /// </summary>
        public static TaskTemplate FromTask(TaskItem task, string templateName, string templateDescription = "")
        {
            var template = new TaskTemplate
            {
                Name = templateName,
                Description = templateDescription,
                Title = task.Title,
                TaskDescription = task.Description,
                DefaultPriority = task.Priority
            };

            // Calculate relative due date
            if (task.DueDate.HasValue)
            {
                var days = (task.DueDate.Value.Date - DateTime.Today).Days;
                template.RelativeDueDateDays = days;
            }

            // Copy tags
            template.Tags = new List<string>(task.Tags);

            // Copy subtasks
            foreach (var subtask in task.Subtasks)
            {
                var subtaskTemplate = new SubtaskTemplate
                {
                    Title = subtask.Title,
                    Description = subtask.Description,
                    Priority = subtask.Priority
                };

                // Calculate relative due date for subtask
                if (subtask.DueDate.HasValue)
                {
                    var days = (subtask.DueDate.Value.Date - DateTime.Today).Days;
                    subtaskTemplate.RelativeDueDateDays = days;
                }

                template.Subtasks.Add(subtaskTemplate);
            }

            return template;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a subtask within a template
    /// </summary>
    public class SubtaskTemplate : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _description = string.Empty;
        private TaskPriority _priority = TaskPriority.Medium;
        private int? _relativeDueDateDays;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public TaskPriority Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged(nameof(Priority));
                }
            }
        }

        public int? RelativeDueDateDays
        {
            get => _relativeDueDateDays;
            set
            {
                if (_relativeDueDateDays != value)
                {
                    _relativeDueDateDays = value;
                    OnPropertyChanged(nameof(RelativeDueDateDays));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
