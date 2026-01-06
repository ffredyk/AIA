using System;
using System.ComponentModel;

namespace AIA.Models
{
    /// <summary>
    /// Settings for reminder notifications
    /// </summary>
    public class ReminderNotificationSettings : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private int _warningMinutes = 60; // Show warning when 1 hour left
        private int _urgentMinutes = 15;  // Show urgent warning when 15 minutes left
        private int _expiredCheckSeconds = 30; // Check for expired every 30 seconds
        private bool _showOverdueNotifications = true;
        private bool _showWarningNotifications = true;
        private bool _showUrgentNotifications = true;
        private bool _playSound = false;
        private int _notificationDurationSeconds = 10;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether reminder notifications are enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Minutes before due date to show warning notification (default: 60 = 1 hour)
        /// </summary>
        public int WarningMinutes
        {
            get => _warningMinutes;
            set { _warningMinutes = value; OnPropertyChanged(nameof(WarningMinutes)); }
        }

        /// <summary>
        /// Minutes before due date to show urgent notification (default: 15 minutes)
        /// </summary>
        public int UrgentMinutes
        {
            get => _urgentMinutes;
            set { _urgentMinutes = value; OnPropertyChanged(nameof(UrgentMinutes)); }
        }

        /// <summary>
        /// How often to check for expired reminders in seconds
        /// </summary>
        public int ExpiredCheckSeconds
        {
            get => _expiredCheckSeconds;
            set { _expiredCheckSeconds = value; OnPropertyChanged(nameof(ExpiredCheckSeconds)); }
        }

        /// <summary>
        /// Whether to show notifications for overdue reminders
        /// </summary>
        public bool ShowOverdueNotifications
        {
            get => _showOverdueNotifications;
            set { _showOverdueNotifications = value; OnPropertyChanged(nameof(ShowOverdueNotifications)); }
        }

        /// <summary>
        /// Whether to show warning notifications (e.g., 1 hour left)
        /// </summary>
        public bool ShowWarningNotifications
        {
            get => _showWarningNotifications;
            set { _showWarningNotifications = value; OnPropertyChanged(nameof(ShowWarningNotifications)); }
        }

        /// <summary>
        /// Whether to show urgent notifications (e.g., 15 minutes left)
        /// </summary>
        public bool ShowUrgentNotifications
        {
            get => _showUrgentNotifications;
            set { _showUrgentNotifications = value; OnPropertyChanged(nameof(ShowUrgentNotifications)); }
        }

        /// <summary>
        /// Whether to play a sound with notifications
        /// </summary>
        public bool PlaySound
        {
            get => _playSound;
            set { _playSound = value; OnPropertyChanged(nameof(PlaySound)); }
        }

        /// <summary>
        /// How long notifications stay visible in seconds (0 = until dismissed)
        /// </summary>
        public int NotificationDurationSeconds
        {
            get => _notificationDurationSeconds;
            set { _notificationDurationSeconds = value; OnPropertyChanged(nameof(NotificationDurationSeconds)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents the urgency level of a notification
    /// </summary>
    public enum NotificationUrgency
    {
        /// <summary>
        /// Warning - reminder is approaching (e.g., 1 hour left)
        /// </summary>
        Warning,

        /// <summary>
        /// Urgent - reminder is very close (e.g., 15 minutes left)
        /// </summary>
        Urgent,

        /// <summary>
        /// Overdue - reminder has passed its due date
        /// </summary>
        Overdue
    }

    /// <summary>
    /// Data for a reminder notification
    /// </summary>
    public class ReminderNotification
    {
        public ReminderItem Reminder { get; set; } = null!;
        public NotificationUrgency Urgency { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime NotifiedAt { get; set; } = DateTime.Now;

        public string UrgencyText => Urgency switch
        {
            NotificationUrgency.Warning => Services.LocalizationService.Instance.GetString("Notification_Approaching"),
            NotificationUrgency.Urgent => Services.LocalizationService.Instance.GetString("Notification_DueSoon"),
            NotificationUrgency.Overdue => Services.LocalizationService.Instance.GetString("Notification_Overdue"),
            _ => Services.LocalizationService.Instance.GetString("Notification_Reminder")
        };

        public string UrgencyColor => Urgency switch
        {
            NotificationUrgency.Warning => "#FFA500",
            NotificationUrgency.Urgent => "#FF6B00",
            NotificationUrgency.Overdue => "#FF4444",
            _ => "#0078D4"
        };

        public string UrgencyBackground => Urgency switch
        {
            NotificationUrgency.Warning => "#33FFA500",
            NotificationUrgency.Urgent => "#44FF6B00",
            NotificationUrgency.Overdue => "#55FF4444",
            _ => "#330078D4"
        };
    }
}
