using System;
using System.ComponentModel;

namespace AIA.Models
{
    public enum EmailFlagStatus
    {
        NotFlagged,
        Flagged,
        Complete
    }

    public class OutlookEmail : INotifyPropertyChanged
    {
        private string _entryId = string.Empty;
        private string _subject = string.Empty;
        private string _senderName = string.Empty;
        private string _senderEmail = string.Empty;
        private DateTime _receivedDate;
        private string _bodyPreview = string.Empty;
        private string _body = string.Empty;
        private EmailFlagStatus _flagStatus = EmailFlagStatus.Flagged;
        private bool _isRead;
        private string _importance = "Normal";
        private DateTime? _flagDueDate;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string EntryId
        {
            get => _entryId;
            set { _entryId = value; OnPropertyChanged(nameof(EntryId)); }
        }

        public string Subject
        {
            get => _subject;
            set { _subject = value; OnPropertyChanged(nameof(Subject)); }
        }

        public string SenderName
        {
            get => _senderName;
            set { _senderName = value; OnPropertyChanged(nameof(SenderName)); }
        }

        public string SenderEmail
        {
            get => _senderEmail;
            set { _senderEmail = value; OnPropertyChanged(nameof(SenderEmail)); }
        }

        public DateTime ReceivedDate
        {
            get => _receivedDate;
            set 
            { 
                _receivedDate = value; 
                OnPropertyChanged(nameof(ReceivedDate));
                OnPropertyChanged(nameof(ReceivedDateText));
            }
        }

        public string BodyPreview
        {
            get => _bodyPreview;
            set { _bodyPreview = value; OnPropertyChanged(nameof(BodyPreview)); }
        }

        public string Body
        {
            get => _body;
            set { _body = value; OnPropertyChanged(nameof(Body)); }
        }

        public EmailFlagStatus FlagStatus
        {
            get => _flagStatus;
            set 
            { 
                _flagStatus = value; 
                OnPropertyChanged(nameof(FlagStatus));
                OnPropertyChanged(nameof(IsCompleted));
            }
        }

        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(nameof(IsRead)); }
        }

        public string Importance
        {
            get => _importance;
            set { _importance = value; OnPropertyChanged(nameof(Importance)); }
        }

        public DateTime? FlagDueDate
        {
            get => _flagDueDate;
            set 
            { 
                _flagDueDate = value; 
                OnPropertyChanged(nameof(FlagDueDate));
                OnPropertyChanged(nameof(FlagDueDateText));
                OnPropertyChanged(nameof(IsOverdue));
            }
        }

        // Computed properties
        public bool IsCompleted => FlagStatus == EmailFlagStatus.Complete;

        public bool IsOverdue => FlagDueDate.HasValue && FlagDueDate.Value < DateTime.Now && !IsCompleted;

        public string ReceivedDateText
        {
            get
            {
                var diff = DateTime.Now - ReceivedDate;
                
                if (diff.TotalMinutes < 1)
                    return "Just now";
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes} min ago";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours} hours ago";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} days ago";
                if (ReceivedDate.Year == DateTime.Now.Year)
                    return ReceivedDate.ToString("MMM dd");
                return ReceivedDate.ToString("MMM dd, yyyy");
            }
        }

        public string FlagDueDateText
        {
            get
            {
                if (!FlagDueDate.HasValue)
                    return "No due date";

                if (IsCompleted)
                    return "Completed";

                var diff = FlagDueDate.Value - DateTime.Now;
                
                if (diff.TotalSeconds < 0)
                {
                    var overdue = DateTime.Now - FlagDueDate.Value;
                    if (overdue.TotalDays < 1)
                        return "Overdue today";
                    if (overdue.TotalDays < 7)
                        return $"{(int)overdue.TotalDays} days overdue";
                    return $"Overdue since {FlagDueDate.Value:MMM dd}";
                }
                
                if (diff.TotalDays < 1)
                    return "Due today";
                if (diff.TotalDays < 2)
                    return "Due tomorrow";
                if (diff.TotalDays < 7)
                    return $"Due in {(int)diff.TotalDays} days";
                return $"Due {FlagDueDate.Value:MMM dd}";
            }
        }

        public string SenderInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SenderName))
                    return "?";

                var parts = SenderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    return $"{parts[0][0]}{parts[1][0]}".ToUpper();
                if (parts.Length == 1 && parts[0].Length >= 1)
                    return parts[0][0].ToString().ToUpper();
                return "?";
            }
        }

        public void RefreshTimeDisplays()
        {
            OnPropertyChanged(nameof(ReceivedDateText));
            OnPropertyChanged(nameof(FlagDueDateText));
            OnPropertyChanged(nameof(IsOverdue));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
