using System;

namespace AIA.Services
{
    /// <summary>
    /// Notification types for visual styling
    /// </summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Event args for notification requests
    /// </summary>
    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Info;
        public int DurationSeconds { get; set; } = 3;
        public bool PlaySound { get; set; } = false;

        public NotificationEventArgs(string message, NotificationType type = NotificationType.Info)
        {
            Message = message;
            Type = type;
        }

        public NotificationEventArgs(string title, string message, NotificationType type = NotificationType.Info)
        {
            Title = title;
            Message = message;
            Type = type;
        }
    }

    /// <summary>
    /// Central service for showing notifications throughout the application.
    /// Can be called from any thread - handles UI dispatch internally.
    /// </summary>
    public class NotificationService
    {
        private static NotificationService? _instance;
        private static readonly object _lock = new();

        /// <summary>
        /// Singleton instance of the notification service
        /// </summary>
        public static NotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new NotificationService();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Event fired when a toast notification should be shown
        /// </summary>
        public event EventHandler<NotificationEventArgs>? ToastRequested;

        /// <summary>
        /// Event fired when a rich notification (like reminder style) should be shown
        /// </summary>
        public event EventHandler<NotificationEventArgs>? RichNotificationRequested;

        private NotificationService() { }

        /// <summary>
        /// Shows a simple toast notification
        /// </summary>
        public void ShowToast(string message, NotificationType type = NotificationType.Info)
        {
            var args = new NotificationEventArgs(message, type);
            RaiseToastRequested(args);
        }

        /// <summary>
        /// Shows a toast notification with title
        /// </summary>
        public void ShowToast(string title, string message, NotificationType type = NotificationType.Info)
        {
            var args = new NotificationEventArgs(title, message, type);
            RaiseToastRequested(args);
        }

        /// <summary>
        /// Shows an info toast
        /// </summary>
        public void ShowInfo(string message) => ShowToast(message, NotificationType.Info);

        /// <summary>
        /// Shows a success toast
        /// </summary>
        public void ShowSuccess(string message) => ShowToast(message, NotificationType.Success);

        /// <summary>
        /// Shows a warning toast
        /// </summary>
        public void ShowWarning(string message) => ShowToast(message, NotificationType.Warning);

        /// <summary>
        /// Shows an error toast
        /// </summary>
        public void ShowError(string message) => ShowToast(message, NotificationType.Error);

        /// <summary>
        /// Shows a rich notification with more options
        /// </summary>
        public void ShowRichNotification(string title, string message, NotificationType type = NotificationType.Info, 
            int durationSeconds = 10, bool playSound = false)
        {
            var args = new NotificationEventArgs(title, message, type)
            {
                DurationSeconds = durationSeconds,
                PlaySound = playSound
            };
            RaiseRichNotificationRequested(args);
        }

        private void RaiseToastRequested(NotificationEventArgs args)
        {
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ToastRequested?.Invoke(this, args);
                });
            }
            else
            {
                ToastRequested?.Invoke(this, args);
            }
        }

        private void RaiseRichNotificationRequested(NotificationEventArgs args)
        {
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RichNotificationRequested?.Invoke(this, args);
                });
            }
            else
            {
                RichNotificationRequested?.Invoke(this, args);
            }
        }
    }
}
