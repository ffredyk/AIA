using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AIA.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIA.Dialogs
{
    public partial class CalendarEventDialog
    {
        private readonly CalendarEventItem? _existing;

        public CalendarEventItem? ResultEvent { get; private set; }

        public CalendarEventDialog() : this(null)
        {
        }

        public CalendarEventDialog(CalendarEventItem? existing)
        {
            _existing = existing;
            InitializeComponent();

            var start = existing?.Start ?? DateTime.Now.AddHours(1);
            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
            var end = existing?.End ?? start.AddHours(1);

            TxtTitle.Text = existing?.Title ?? string.Empty;
            TxtDescription.Text = existing?.Description ?? string.Empty;
            TxtLocation.Text = existing?.Location ?? string.Empty;
            ChkAllDay.IsChecked = existing?.IsAllDay ?? false;
            DpStart.SelectedDate = start.Date;
            DpEnd.SelectedDate = end.Date;
            TxtStartTime.Text = start.ToString("HH:mm");
            TxtEndTime.Text = end.ToString("HH:mm");
            NumInterval.Value = Math.Max(1, existing?.RecurrenceInterval ?? 1);
            NumReminder.Value = Math.Max(0, existing?.ReminderMinutesBefore ?? 15);

            var recurrence = existing?.RecurrenceType ?? RecurrenceType.None;
            foreach (ComboBoxItem item in CmbRecurrence.Items)
            {
                if (string.Equals(item.Tag?.ToString(), recurrence.ToString(), StringComparison.Ordinal))
                {
                    CmbRecurrence.SelectedItem = item;
                    break;
                }
            }

            ApplyAllDayState();
        }

        private void ChkAllDay_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAllDayState();
        }

        private void ApplyAllDayState()
        {
            var isAllDay = ChkAllDay.IsChecked == true;
            TxtStartTime.IsEnabled = !isAllDay;
            TxtEndTime.IsEnabled = !isAllDay;
            if (isAllDay && DpEnd.SelectedDate == null)
                DpEnd.SelectedDate = DpStart.SelectedDate;
        }

        private void BtnOK(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTitle.Text))
            {
                WpfMessageBox.Show("Title is required.", "Calendar event", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = DpStart.SelectedDate ?? DateTime.Today;
            var endDate = DpEnd.SelectedDate ?? startDate;

            if (!TryParseTime(TxtStartTime.Text, out var startTime) || !TryParseTime(TxtEndTime.Text, out var endTime))
            {
                WpfMessageBox.Show("Use a time in HH:mm format.", "Calendar event", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime start;
            DateTime end;

            if (ChkAllDay.IsChecked == true)
            {
                start = startDate.Date;
                end = endDate.Date.AddDays(1).AddSeconds(-1);
            }
            else
            {
                start = startDate.Date.Add(startTime);
                end = endDate.Date.Add(endTime);
            }

            if (end <= start)
            {
                WpfMessageBox.Show("End must be after start.", "Calendar event", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recurrence = RecurrenceType.None;
            if (CmbRecurrence.SelectedItem is ComboBoxItem { Tag: string tag } &&
                Enum.TryParse<RecurrenceType>(tag, out var parsed))
            {
                recurrence = parsed;
            }

            ResultEvent = new CalendarEventItem
            {
                Id = _existing?.Id ?? Guid.NewGuid(),
                Title = TxtTitle.Text.Trim(),
                Description = TxtDescription.Text ?? string.Empty,
                Location = TxtLocation.Text?.Trim() ?? string.Empty,
                Start = start,
                End = end,
                IsAllDay = ChkAllDay.IsChecked == true,
                RecurrenceType = recurrence,
                RecurrenceInterval = Math.Max(1, (int)NumInterval.Value),
                RecurrenceEndDate = _existing?.RecurrenceEndDate,
                ReminderMinutesBefore = Math.Max(0, (int)NumReminder.Value),
                CreatedDate = _existing?.CreatedDate ?? DateTime.Now
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static bool TryParseTime(string? value, out TimeSpan time)
        {
            if (TimeSpan.TryParse(value?.Trim(), out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
                return true;

            if (DateTime.TryParse(value, out var parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }

            time = default;
            return false;
        }
    }
}
