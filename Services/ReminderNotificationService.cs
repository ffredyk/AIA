using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service that monitors reminders and triggers notifications when they're about to expire
    /// </summary>
    public class ReminderNotificationService
    {
        private readonly DispatcherTimer _checkTimer;
        private readonly Dictionary<Guid, HashSet<NotificationUrgency>> _notifiedReminders;
        private ReminderNotificationSettings _settings;

        /// <summary>
        /// Event fired when a reminder notification should be shown
        /// </summary>
        public event EventHandler<ReminderNotification>? NotificationTriggered;

        /// <summary>
        /// The notification settings
        /// </summary>
        public ReminderNotificationSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                UpdateTimerInterval();
            }
        }

        public ReminderNotificationService()
        {
            _settings = new ReminderNotificationSettings();
            _notifiedReminders = new Dictionary<Guid, HashSet<NotificationUrgency>>();

            _checkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(_settings.ExpiredCheckSeconds)
            };
            _checkTimer.Tick += OnCheckTimerTick;
        }

        /// <summary>
        /// Starts monitoring reminders for notifications
        /// </summary>
        public void Start()
        {
            if (_settings.IsEnabled)
            {
                _checkTimer.Start();
            }
        }

        /// <summary>
        /// Stops monitoring reminders
        /// </summary>
        public void Stop()
        {
            _checkTimer.Stop();
        }

        /// <summary>
        /// Clears the notification history for a specific reminder (allows re-notification)
        /// </summary>
        public void ClearNotificationHistory(Guid reminderId)
        {
            _notifiedReminders.Remove(reminderId);
        }

        /// <summary>
        /// Clears all notification history
        /// </summary>
        public void ClearAllNotificationHistory()
        {
            _notifiedReminders.Clear();
        }

        private void UpdateTimerInterval()
        {
            _checkTimer.Interval = TimeSpan.FromSeconds(_settings.ExpiredCheckSeconds);

            if (_settings.IsEnabled && !_checkTimer.IsEnabled)
            {
                _checkTimer.Start();
            }
            else if (!_settings.IsEnabled && _checkTimer.IsEnabled)
            {
                _checkTimer.Stop();
            }
        }

        private void OnCheckTimerTick(object? sender, EventArgs e)
        {
            CheckReminders();
        }

        /// <summary>
        /// Manually checks all reminders and triggers notifications as needed
        /// </summary>
        public void CheckReminders()
        {
            if (!_settings.IsEnabled) return;

            var viewModel = OverlayViewModel.Singleton;
            var now = DateTime.Now;

            foreach (var reminder in viewModel.Reminders.Where(r => !r.IsCompleted))
            {
                CheckReminder(reminder, now);
            }

            // Clean up notification history for completed or deleted reminders
            CleanupNotificationHistory(viewModel.Reminders);
        }

        private void CheckReminder(ReminderItem reminder, DateTime now)
        {
            var timeUntilDue = reminder.DueDate - now;
            var minutesUntilDue = timeUntilDue.TotalMinutes;

            // Determine what notification level should be shown
            NotificationUrgency? urgencyToShow = null;
            string message = string.Empty;

            if (minutesUntilDue < 0 && _settings.ShowOverdueNotifications)
            {
                // Overdue
                urgencyToShow = NotificationUrgency.Overdue;
                var overdueTime = now - reminder.DueDate;
                message = GetOverdueMessage(overdueTime);
            }
            else if (minutesUntilDue <= _settings.UrgentMinutes && minutesUntilDue > 0 && _settings.ShowUrgentNotifications)
            {
                // Urgent - due soon
                urgencyToShow = NotificationUrgency.Urgent;
                message = GetUrgentMessage(timeUntilDue);
            }
            else if (minutesUntilDue <= _settings.WarningMinutes && minutesUntilDue > _settings.UrgentMinutes && _settings.ShowWarningNotifications)
            {
                // Warning - approaching
                urgencyToShow = NotificationUrgency.Warning;
                message = GetWarningMessage(timeUntilDue);
            }

            if (urgencyToShow.HasValue && !HasBeenNotified(reminder.Id, urgencyToShow.Value))
            {
                // Mark as notified
                MarkAsNotified(reminder.Id, urgencyToShow.Value);

                // Trigger the notification
                var notification = new ReminderNotification
                {
                    Reminder = reminder,
                    Urgency = urgencyToShow.Value,
                    Message = message,
                    NotifiedAt = now
                };

                NotificationTriggered?.Invoke(this, notification);
            }
        }

        private bool HasBeenNotified(Guid reminderId, NotificationUrgency urgency)
        {
            return _notifiedReminders.TryGetValue(reminderId, out var notified) && notified.Contains(urgency);
        }

        private void MarkAsNotified(Guid reminderId, NotificationUrgency urgency)
        {
            if (!_notifiedReminders.ContainsKey(reminderId))
            {
                _notifiedReminders[reminderId] = new HashSet<NotificationUrgency>();
            }
            _notifiedReminders[reminderId].Add(urgency);
        }

        private void CleanupNotificationHistory(IEnumerable<ReminderItem> currentReminders)
        {
            var currentIds = currentReminders.Select(r => r.Id).ToHashSet();
            var idsToRemove = _notifiedReminders.Keys.Where(id => !currentIds.Contains(id)).ToList();

            foreach (var id in idsToRemove)
            {
                _notifiedReminders.Remove(id);
            }
        }

        private static string GetWarningMessage(TimeSpan timeUntilDue)
        {
            if (timeUntilDue.TotalMinutes < 60)
            {
                return $"{(int)timeUntilDue.TotalMinutes} minutes left";
            }
            else
            {
                var hours = (int)timeUntilDue.TotalHours;
                var minutes = timeUntilDue.Minutes;
                if (minutes > 0)
                {
                    return $"{hours} hour{(hours > 1 ? "s" : "")} {minutes} min left";
                }
                return $"{hours} hour{(hours > 1 ? "s" : "")} left";
            }
        }

        private static string GetUrgentMessage(TimeSpan timeUntilDue)
        {
            if (timeUntilDue.TotalMinutes < 1)
            {
                return "Less than a minute left!";
            }
            return $"Only {(int)timeUntilDue.TotalMinutes} minutes left!";
        }

        private static string GetOverdueMessage(TimeSpan overdueTime)
        {
            if (overdueTime.TotalMinutes < 1)
            {
                return "Just expired!";
            }
            else if (overdueTime.TotalMinutes < 60)
            {
                return $"Overdue by {(int)overdueTime.TotalMinutes} minutes";
            }
            else if (overdueTime.TotalHours < 24)
            {
                return $"Overdue by {(int)overdueTime.TotalHours} hours";
            }
            else
            {
                return $"Overdue by {(int)overdueTime.TotalDays} days";
            }
        }
    }
}
