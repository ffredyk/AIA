using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of reminder service that bridges to OverlayViewModel
    /// </summary>
    public class HostReminderService : IReminderService
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public event EventHandler<RemindersChangedEventArgs>? RemindersChanged;

        public HostReminderService(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        private OverlayViewModel ViewModel => _viewModelProvider();

        public IReadOnlyList<IReminderItem> GetAllReminders()
        {
            return ViewModel.Reminders.Select(r => new ReminderItemAdapter(r, this)).ToList();
        }

        public IReminderItem? GetReminderById(Guid id)
        {
            var reminder = ViewModel.Reminders.FirstOrDefault(r => r.Id == id);
            return reminder != null ? new ReminderItemAdapter(reminder, this) : null;
        }

        public IReminderItem CreateReminder(string title, DateTime dueDate, ReminderItemSeverity severity = ReminderItemSeverity.Medium)
        {
            var reminder = new ReminderItem
            {
                Title = title,
                DueDate = dueDate,
                Severity = (ReminderSeverity)(int)severity
            };

            ViewModel.Reminders.Add(reminder);
            var adapter = new ReminderItemAdapter(reminder, this);
            RemindersChanged?.Invoke(this, new RemindersChangedEventArgs(ReminderChangeType.Added, adapter));

            return adapter;
        }

        public bool DeleteReminder(Guid id)
        {
            var reminder = ViewModel.Reminders.FirstOrDefault(r => r.Id == id);
            if (reminder == null) return false;

            ViewModel.DeleteReminder(reminder);
            RemindersChanged?.Invoke(this, new RemindersChangedEventArgs(ReminderChangeType.Deleted));
            return true;
        }

        public void SnoozeReminder(Guid id, int minutes = 15)
        {
            var reminder = ViewModel.Reminders.FirstOrDefault(r => r.Id == id);
            if (reminder != null)
            {
                ViewModel.SnoozeReminder(reminder, minutes);
                RemindersChanged?.Invoke(this, new RemindersChangedEventArgs(ReminderChangeType.Updated, new ReminderItemAdapter(reminder, this)));
            }
        }

        public void ToggleComplete(Guid id)
        {
            var reminder = ViewModel.Reminders.FirstOrDefault(r => r.Id == id);
            if (reminder != null)
            {
                ViewModel.ToggleReminderComplete(reminder);
                RemindersChanged?.Invoke(this, new RemindersChangedEventArgs(ReminderChangeType.Updated, new ReminderItemAdapter(reminder, this)));
            }
        }

        public async Task SaveAsync()
        {
            await ViewModel.SaveTasksAndRemindersAsync();
        }

        internal void RaiseRemindersChanged(ReminderChangeType changeType, IReminderItem? reminder = null)
        {
            RemindersChanged?.Invoke(this, new RemindersChangedEventArgs(changeType, reminder));
        }
    }

    /// <summary>
    /// Adapter that wraps ReminderItem for plugin consumption
    /// </summary>
    internal class ReminderItemAdapter : IReminderItem
    {
        private readonly ReminderItem _reminder;
        private readonly HostReminderService _service;

        public ReminderItemAdapter(ReminderItem reminder, HostReminderService service)
        {
            _reminder = reminder ?? throw new ArgumentNullException(nameof(reminder));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public Guid Id => _reminder.Id;

        public string Title
        {
            get => _reminder.Title;
            set
            {
                _reminder.Title = value;
                _service.RaiseRemindersChanged(ReminderChangeType.Updated, this);
            }
        }

        public DateTime DueDate
        {
            get => _reminder.DueDate;
            set
            {
                _reminder.DueDate = value;
                _service.RaiseRemindersChanged(ReminderChangeType.Updated, this);
            }
        }

        public ReminderItemSeverity Severity
        {
            get => (ReminderItemSeverity)(int)_reminder.Severity;
            set
            {
                _reminder.Severity = (ReminderSeverity)(int)value;
                _service.RaiseRemindersChanged(ReminderChangeType.Updated, this);
            }
        }

        public DateTime CreatedDate => _reminder.CreatedDate;

        public bool IsCompleted
        {
            get => _reminder.IsCompleted;
            set
            {
                _reminder.IsCompleted = value;
                _service.RaiseRemindersChanged(ReminderChangeType.Updated, this);
            }
        }

        public bool IsOverdue => _reminder.IsOverdue;

        public string TimeLeftText => _reminder.TimeLeftText;
    }
}
