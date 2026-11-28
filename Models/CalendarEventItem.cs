using System;
using System.ComponentModel;

namespace AIA.Models
{
    public class CalendarEventItem : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title = string.Empty;
        private string _description = string.Empty;
        private string _location = string.Empty;
        private DateTime _start;
        private DateTime _end;
        private bool _isAllDay;
        private RecurrenceType _recurrenceType = RecurrenceType.None;
        private int _recurrenceInterval = 1;
        private DateTime? _recurrenceEndDate;
        private int _reminderMinutesBefore = 15;
        private DateTime _createdDate;

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

        public string Location
        {
            get => _location;
            set { _location = value; OnPropertyChanged(nameof(Location)); }
        }

        public DateTime Start
        {
            get => _start;
            set
            {
                _start = value;
                OnPropertyChanged(nameof(Start));
                OnPropertyChanged(nameof(TimeRangeText));
                OnPropertyChanged(nameof(IsUpcoming));
            }
        }

        public DateTime End
        {
            get => _end;
            set
            {
                _end = value;
                OnPropertyChanged(nameof(End));
                OnPropertyChanged(nameof(TimeRangeText));
            }
        }

        public bool IsAllDay
        {
            get => _isAllDay;
            set
            {
                _isAllDay = value;
                OnPropertyChanged(nameof(IsAllDay));
                OnPropertyChanged(nameof(TimeRangeText));
            }
        }

        public RecurrenceType RecurrenceType
        {
            get => _recurrenceType;
            set
            {
                _recurrenceType = value;
                OnPropertyChanged(nameof(RecurrenceType));
                OnPropertyChanged(nameof(IsRecurring));
            }
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

        public int ReminderMinutesBefore
        {
            get => _reminderMinutesBefore;
            set { _reminderMinutesBefore = value; OnPropertyChanged(nameof(ReminderMinutesBefore)); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        public bool IsRecurring => RecurrenceType != RecurrenceType.None;

        public bool IsUpcoming => End >= DateTime.Now;

        public string TimeRangeText
        {
            get
            {
                if (IsAllDay)
                    return Start.Date == End.Date
                        ? Start.ToString("MMM dd, yyyy") + " (all day)"
                        : $"{Start:MMM dd, yyyy} - {End:MMM dd, yyyy} (all day)";

                return Start.Date == End.Date
                    ? $"{Start:HH:mm} - {End:HH:mm}"
                    : $"{Start:MMM dd, HH:mm} - {End:MMM dd, HH:mm}";
            }
        }

        public CalendarEventItem()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            var now = DateTime.Now;
            Start = now.AddHours(1);
            End = Start.AddHours(1);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
