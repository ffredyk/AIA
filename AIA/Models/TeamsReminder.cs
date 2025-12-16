using System;
using System.ComponentModel;

namespace AIA.Models
{
    public class TeamsReminder : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _description = string.Empty;
        private DateTime _dueDate;
        private bool _isCompleted;
        private string _source = string.Empty;
        private string _linkedMessageId = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id
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

        public DateTime DueDate
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

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); }
        }

        public string Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(nameof(Source)); }
        }

        public string LinkedMessageId
        {
            get => _linkedMessageId;
            set { _linkedMessageId = value; OnPropertyChanged(nameof(LinkedMessageId)); }
        }

        // Computed properties
        public bool IsOverdue => !IsCompleted && DueDate < DateTime.Now;

        public string DueDateText
        {
            get
            {
                if (IsCompleted)
                    return "Completed";

                var diff = DueDate - DateTime.Now;

                if (diff.TotalSeconds < 0)
                {
                    var overdue = DateTime.Now - DueDate;
                    if (overdue.TotalDays < 1)
                        return "Overdue today";
                    if (overdue.TotalDays < 7)
                        return $"{(int)overdue.TotalDays} days overdue";
                    return $"Overdue since {DueDate:MMM dd}";
                }

                if (diff.TotalMinutes < 60)
                    return $"In {(int)diff.TotalMinutes} min";
                if (diff.TotalHours < 24)
                    return $"In {(int)diff.TotalHours}h";
                if (diff.TotalDays < 1)
                    return "Today";
                if (diff.TotalDays < 2)
                    return "Tomorrow";
                if (diff.TotalDays < 7)
                    return $"In {(int)diff.TotalDays} days";
                return $"Due {DueDate:MMM dd}";
            }
        }

        public void RefreshTimeDisplays()
        {
            OnPropertyChanged(nameof(DueDateText));
            OnPropertyChanged(nameof(IsOverdue));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
