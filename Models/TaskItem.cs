using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace AIA.Models
{
    public enum TaskStatus
    {
        NotStarted,
        InProgress,
        OnHold,
        Completed,
        Cancelled
    }

    public enum TaskPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum RecurrenceType
    {
        None,
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public class TaskItem : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title = string.Empty;
        private string _description = string.Empty;
        private string _notes = string.Empty;
        private TaskStatus _status = TaskStatus.NotStarted;
        private TaskPriority _priority = TaskPriority.Medium;
        private DateTime _createdDate;
        private DateTime? _dueDate;
        private DateTime? _completedDate;
        private bool _isExpanded;
        private bool _isEditing;
        private Guid? _parentTaskId;
        private bool _isArchived;
        private bool _isSelected;
        private RecurrenceType _recurrenceType = RecurrenceType.None;
        private int _recurrenceInterval = 1;
        private DateTime? _recurrenceEndDate;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(nameof(Notes)); }
        }

        public TaskStatus Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(IsCompleted));
                if (value == TaskStatus.Completed && _completedDate == null)
                {
                    CompletedDate = DateTime.Now;
                }
            }
        }

        public TaskPriority Priority
        {
            get => _priority;
            set 
            { 
                _priority = value; 
                OnPropertyChanged(nameof(Priority));
                OnPropertyChanged(nameof(PriorityColor));
            }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        public DateTime? DueDate
        {
            get => _dueDate;
            set 
            { 
                _dueDate = value; 
                OnPropertyChanged(nameof(DueDate));
                OnPropertyChanged(nameof(DueDateText));
                OnPropertyChanged(nameof(IsOverdue));
            }
        }

        public DateTime? CompletedDate
        {
            get => _completedDate;
            set { _completedDate = value; OnPropertyChanged(nameof(CompletedDate)); }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
        }

        public Guid? ParentTaskId
        {
            get => _parentTaskId;
            set { _parentTaskId = value; OnPropertyChanged(nameof(ParentTaskId)); OnPropertyChanged(nameof(IsSubtask)); }
        }

        public bool IsArchived
        {
            get => _isArchived;
            set { _isArchived = value; OnPropertyChanged(nameof(IsArchived)); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public RecurrenceType RecurrenceType
        {
            get => _recurrenceType;
            set { _recurrenceType = value; OnPropertyChanged(nameof(RecurrenceType)); OnPropertyChanged(nameof(IsRecurring)); }
        }

        public int RecurrenceInterval
        {
            get => _recurrenceInterval;
            set { _recurrenceInterval = value; OnPropertyChanged(nameof(RecurrenceInterval)); }
        }

        public DateTime? RecurrenceEndDate
        {
            get => _recurrenceEndDate;
            set { _recurrenceEndDate = value; OnPropertyChanged(nameof(RecurrenceEndDate)); }
        }

        public ObservableCollection<TaskItem> Subtasks { get; set; } = new ObservableCollection<TaskItem>();
        public ObservableCollection<string> Tags { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<Guid> DependencyTaskIds { get; set; } = new ObservableCollection<Guid>();

        // Computed properties
        public bool IsCompleted => Status == TaskStatus.Completed;

        public bool IsSubtask => ParentTaskId.HasValue;

        public bool IsRecurring => RecurrenceType != RecurrenceType.None;

        public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.Now && Status != TaskStatus.Completed;

        public string DueDateText
        {
            get
            {
                if (!DueDate.HasValue) return "No due date";
                var diff = DueDate.Value.Date - DateTime.Today;
                if (diff.Days == 0) return "Due today";
                if (diff.Days == 1) return "Due tomorrow";
                if (diff.Days == -1) return "Due yesterday";
                if (diff.Days < 0) return $"Overdue by {Math.Abs(diff.Days)} days";
                if (diff.Days <= 7) return $"Due in {diff.Days} days";
                return DueDate.Value.ToString("MMM dd, yyyy");
            }
        }

        public string StatusColor => Status switch
        {
            TaskStatus.NotStarted => "#808080",
            TaskStatus.InProgress => "#0078D4",
            TaskStatus.OnHold => "#FFA500",
            TaskStatus.Completed => "#1EB75F",
            TaskStatus.Cancelled => "#FF4444",
            _ => "#808080"
        };

        public string PriorityColor => Priority switch
        {
            TaskPriority.Low => "#808080",
            TaskPriority.Medium => "#0078D4",
            TaskPriority.High => "#FFA500",
            TaskPriority.Critical => "#FF4444",
            _ => "#808080"
        };

        public int CompletedSubtasksCount => GetCompletedSubtasksCount();

        public int TotalSubtasksCount => Subtasks.Count;

        public string SubtaskProgressText => TotalSubtasksCount > 0 
            ? $"{CompletedSubtasksCount}/{TotalSubtasksCount} subtasks" 
            : string.Empty;

        public string TagsText => Tags.Count > 0 ? string.Join(", ", Tags) : string.Empty;

        public TaskItem()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            Subtasks.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(TotalSubtasksCount));
                OnPropertyChanged(nameof(CompletedSubtasksCount));
                OnPropertyChanged(nameof(SubtaskProgressText));
            };
            Tags.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TagsText));
        }

        private int GetCompletedSubtasksCount()
        {
            int count = 0;
            foreach (var subtask in Subtasks)
            {
                if (subtask.Status == TaskStatus.Completed)
                    count++;
            }
            return count;
        }

        public void AddSubtask(TaskItem subtask)
        {
            subtask.ParentTaskId = this.Id;
            Subtasks.Add(subtask);
        }

        public void RefreshSubtaskProgress()
        {
            OnPropertyChanged(nameof(CompletedSubtasksCount));
            OnPropertyChanged(nameof(SubtaskProgressText));
        }

        /// <summary>
        /// Creates a deep copy of this task
        /// </summary>
        public TaskItem Clone(bool resetStatus = true, bool includeSubtasks = true)
        {
            var clone = new TaskItem
            {
                Title = Title,
                Description = Description,
                Notes = Notes,
                Status = resetStatus ? TaskStatus.NotStarted : Status,
                Priority = Priority,
                DueDate = DueDate,
                RecurrenceType = RecurrenceType,
                RecurrenceInterval = RecurrenceInterval,
                RecurrenceEndDate = RecurrenceEndDate
            };

            // Copy tags
            foreach (var tag in Tags)
            {
                clone.Tags.Add(tag);
            }

            // Copy dependencies
            foreach (var depId in DependencyTaskIds)
            {
                clone.DependencyTaskIds.Add(depId);
            }

            // Copy subtasks if requested
            if (includeSubtasks)
            {
                foreach (var subtask in Subtasks)
                {
                    var subtaskClone = subtask.Clone(resetStatus, false);
                    clone.AddSubtask(subtaskClone);
                }
            }

            return clone;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
