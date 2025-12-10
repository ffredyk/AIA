using System;
using System.ComponentModel;

namespace AIA.Models
{
    public enum ReminderSeverity
    {
        Low,
        Medium,
        High,
        Urgent
    }

    public class ReminderItem : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title = string.Empty;
        private DateTime _dueDate;
        private ReminderSeverity _severity = ReminderSeverity.Medium;
        private DateTime _createdDate;
        private bool _isCompleted;
        private bool _isEditing;

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

        public DateTime DueDate
        {
            get => _dueDate;
            set 
            { 
                _dueDate = value; 
                OnPropertyChanged(nameof(DueDate));
                OnPropertyChanged(nameof(TimeLeftText));
                OnPropertyChanged(nameof(IsOverdue));
            }
        }

        public ReminderSeverity Severity
        {
            get => _severity;
            set 
            { 
                _severity = value; 
                OnPropertyChanged(nameof(Severity));
                OnPropertyChanged(nameof(SeverityColor));
            }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set 
            { 
                _isCompleted = value; 
                OnPropertyChanged(nameof(IsCompleted)); 
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); }
        }

        // Computed properties
        public bool IsOverdue => DueDate < DateTime.Now && !IsCompleted;

        public string TimeLeftText
        {
            get
            {
                if (IsCompleted) return "Completed";
                
                var diff = DueDate - DateTime.Now;
                
                if (diff.TotalSeconds < 0)
                {
                    // Overdue
                    var overdue = DateTime.Now - DueDate;
                    if (overdue.TotalMinutes < 60)
                        return $"{(int)overdue.TotalMinutes} min overdue";
                    if (overdue.TotalHours < 24)
                        return $"{(int)overdue.TotalHours} hours overdue";
                    if (overdue.TotalDays < 7)
                        return $"{(int)overdue.TotalDays} days overdue";
                    return $"{(int)(overdue.TotalDays / 7)} weeks overdue";
                }
                
                // Future
                if (diff.TotalMinutes < 1)
                    return "Less than a minute";
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes} min left";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours} hours left";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} days left";
                if (diff.TotalDays < 30)
                    return $"{(int)(diff.TotalDays / 7)} weeks left";
                return DueDate.ToString("MMM dd, yyyy");
            }
        }

        public string SeverityColor => Severity switch
        {
            ReminderSeverity.Low => "#808080",
            ReminderSeverity.Medium => "#0078D4",
            ReminderSeverity.High => "#FFA500",
            ReminderSeverity.Urgent => "#FF4444",
            _ => "#808080"
        };

        public ReminderItem()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            DueDate = DateTime.Now.AddHours(1);
        }

        public void RefreshTimeLeft()
        {
            OnPropertyChanged(nameof(TimeLeftText));
            OnPropertyChanged(nameof(IsOverdue));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
