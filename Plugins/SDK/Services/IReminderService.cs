using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for managing reminders
    /// </summary>
    public interface IReminderService
    {
        /// <summary>
        /// Gets all reminders (read permission required)
        /// </summary>
        IReadOnlyList<IReminderItem> GetAllReminders();

        /// <summary>
        /// Gets a reminder by ID
        /// </summary>
        IReminderItem? GetReminderById(Guid id);

        /// <summary>
        /// Creates a new reminder (write permission required)
        /// </summary>
        IReminderItem CreateReminder(string title, DateTime dueDate, ReminderItemSeverity severity = ReminderItemSeverity.Medium);

        /// <summary>
        /// Deletes a reminder (write permission required)
        /// </summary>
        bool DeleteReminder(Guid id);

        /// <summary>
        /// Snoozes a reminder by the specified minutes
        /// </summary>
        void SnoozeReminder(Guid id, int minutes = 15);

        /// <summary>
        /// Marks a reminder as complete or incomplete
        /// </summary>
        void ToggleComplete(Guid id);

        /// <summary>
        /// Saves all reminders (write permission required)
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// Event fired when reminders change
        /// </summary>
        event EventHandler<RemindersChangedEventArgs>? RemindersChanged;
    }

    /// <summary>
    /// Reminder item interface
    /// </summary>
    public interface IReminderItem
    {
        Guid Id { get; }
        string Title { get; set; }
        DateTime DueDate { get; set; }
        ReminderItemSeverity Severity { get; set; }
        DateTime CreatedDate { get; }
        bool IsCompleted { get; set; }
        bool IsOverdue { get; }
        string TimeLeftText { get; }
    }

    public enum ReminderItemSeverity
    {
        Low,
        Medium,
        High,
        Urgent
    }

    public class RemindersChangedEventArgs : EventArgs
    {
        public ReminderChangeType ChangeType { get; }
        public IReminderItem? Reminder { get; }

        public RemindersChangedEventArgs(ReminderChangeType changeType, IReminderItem? reminder = null)
        {
            ChangeType = changeType;
            Reminder = reminder;
        }
    }

    public enum ReminderChangeType
    {
        Added,
        Updated,
        Deleted,
        Reloaded
    }
}
