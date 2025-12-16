using System;
using System.ComponentModel;

namespace AIA.Models
{
    public class TeamsMeeting : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _subject = string.Empty;
        private string _organizer = string.Empty;
        private string _organizerEmail = string.Empty;
        private DateTime _startTime;
        private DateTime _endTime;
        private string _location = string.Empty;
        private string _joinUrl = string.Empty;
        private bool _isOnlineMeeting;
        private bool _isAllDay;
        private MeetingStatus _status = MeetingStatus.Scheduled;
        private string _bodyPreview = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Subject
        {
            get => _subject;
            set { _subject = value; OnPropertyChanged(nameof(Subject)); }
        }

        public string Organizer
        {
            get => _organizer;
            set { _organizer = value; OnPropertyChanged(nameof(Organizer)); OnPropertyChanged(nameof(OrganizerInitials)); }
        }

        public string OrganizerEmail
        {
            get => _organizerEmail;
            set { _organizerEmail = value; OnPropertyChanged(nameof(OrganizerEmail)); }
        }

        public DateTime StartTime
        {
            get => _startTime;
            set
            {
                _startTime = value;
                OnPropertyChanged(nameof(StartTime));
                OnPropertyChanged(nameof(TimeRangeText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsHappeningNow));
                OnPropertyChanged(nameof(IsUpcoming));
                OnPropertyChanged(nameof(IsPast));
            }
        }

        public DateTime EndTime
        {
            get => _endTime;
            set
            {
                _endTime = value;
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(TimeRangeText));
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(IsHappeningNow));
                OnPropertyChanged(nameof(IsPast));
            }
        }

        public string Location
        {
            get => _location;
            set { _location = value; OnPropertyChanged(nameof(Location)); }
        }

        public string JoinUrl
        {
            get => _joinUrl;
            set { _joinUrl = value; OnPropertyChanged(nameof(JoinUrl)); OnPropertyChanged(nameof(HasJoinUrl)); }
        }

        public bool IsOnlineMeeting
        {
            get => _isOnlineMeeting;
            set { _isOnlineMeeting = value; OnPropertyChanged(nameof(IsOnlineMeeting)); }
        }

        public bool IsAllDay
        {
            get => _isAllDay;
            set { _isAllDay = value; OnPropertyChanged(nameof(IsAllDay)); OnPropertyChanged(nameof(TimeRangeText)); }
        }

        public MeetingStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string BodyPreview
        {
            get => _bodyPreview;
            set { _bodyPreview = value; OnPropertyChanged(nameof(BodyPreview)); }
        }

        // Computed properties
        public bool HasJoinUrl => !string.IsNullOrEmpty(JoinUrl);

        public bool IsHappeningNow => DateTime.Now >= StartTime && DateTime.Now <= EndTime;

        public bool IsUpcoming => DateTime.Now < StartTime;

        public bool IsPast => DateTime.Now > EndTime;

        public string TimeRangeText
        {
            get
            {
                if (IsAllDay)
                    return "All day";

                return $"{StartTime:HH:mm} - {EndTime:HH:mm}";
            }
        }

        public string DurationText
        {
            get
            {
                var duration = EndTime - StartTime;
                if (duration.TotalHours >= 1)
                {
                    var hours = (int)duration.TotalHours;
                    var minutes = duration.Minutes;
                    if (minutes > 0)
                        return $"{hours}h {minutes}m";
                    return $"{hours}h";
                }
                return $"{(int)duration.TotalMinutes}m";
            }
        }

        public string StatusText
        {
            get
            {
                if (IsHappeningNow)
                    return "Happening now";

                var diff = StartTime - DateTime.Now;

                if (diff.TotalMinutes <= 0)
                    return "Started";

                if (diff.TotalMinutes < 5)
                    return "Starting soon";

                if (diff.TotalMinutes < 60)
                    return $"In {(int)diff.TotalMinutes} min";

                if (diff.TotalHours < 24)
                    return $"In {(int)diff.TotalHours}h {diff.Minutes}m";

                return StartTime.ToString("HH:mm");
            }
        }

        public string OrganizerInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Organizer))
                    return "?";

                var parts = Organizer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    return $"{parts[0][0]}{parts[1][0]}".ToUpper();
                if (parts.Length == 1 && parts[0].Length >= 1)
                    return parts[0][0].ToString().ToUpper();
                return "?";
            }
        }

        public void RefreshTimeDisplays()
        {
            OnPropertyChanged(nameof(TimeRangeText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsHappeningNow));
            OnPropertyChanged(nameof(IsUpcoming));
            OnPropertyChanged(nameof(IsPast));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum MeetingStatus
    {
        Scheduled,
        InProgress,
        Cancelled,
        Completed
    }
}
