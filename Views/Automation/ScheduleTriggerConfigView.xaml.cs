using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AIA.Models.Automation;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AIA.Views.Automation
{
    /// <summary>
    /// Configuration view for Schedule trigger
    /// </summary>
    public partial class ScheduleTriggerConfigView : WpfUserControl
    {
        public ScheduleTriggerConfigView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ScheduleTrigger oldTrigger)
            {
                oldTrigger.PropertyChanged -= OnTriggerPropertyChanged;
            }

            if (e.NewValue is ScheduleTrigger newTrigger)
            {
                newTrigger.PropertyChanged += OnTriggerPropertyChanged;
                UpdateRecurrenceUI(newTrigger);
                UpdatePreview(newTrigger);
            }
        }

        private void OnTriggerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ScheduleTrigger trigger) return;

            if (e.PropertyName is nameof(ScheduleTrigger.Recurrence) or 
                nameof(ScheduleTrigger.RecurrenceInterval) or 
                nameof(ScheduleTrigger.ScheduledTime))
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateRecurrenceUI(trigger);
                    UpdatePreview(trigger);
                });
            }
        }

        private void UpdateRecurrenceUI(ScheduleTrigger trigger)
        {
            var showInterval = trigger.Recurrence != RecurrenceType.None;
            IntervalGrid.Visibility = showInterval ? Visibility.Visible : Visibility.Collapsed;

            IntervalUnitText.Text = trigger.Recurrence switch
            {
                RecurrenceType.Daily => "day(s)",
                RecurrenceType.Weekly => "week(s)",
                RecurrenceType.Monthly => "month(s)",
                _ => "day(s)"
            };
        }

        private void UpdatePreview(ScheduleTrigger trigger)
        {
            if (trigger.ScheduledTime.HasValue)
            {
                var nextRun = CalculateNextRun(trigger);
                if (nextRun.HasValue)
                {
                    var timeUntil = nextRun.Value - DateTime.Now;
                    string timeText;
                    
                    if (timeUntil.TotalMinutes < 1)
                        timeText = "less than a minute";
                    else if (timeUntil.TotalHours < 1)
                        timeText = $"{(int)timeUntil.TotalMinutes} minutes";
                    else if (timeUntil.TotalDays < 1)
                        timeText = $"{(int)timeUntil.TotalHours} hours, {timeUntil.Minutes} minutes";
                    else
                        timeText = $"{(int)timeUntil.TotalDays} days";

                    PreviewText.Text = $"Next run: {nextRun.Value:g} (in {timeText})";
                }
                else
                {
                    PreviewText.Text = "Schedule has passed";
                }
            }
            else
            {
                PreviewText.Text = "Next run: Not scheduled";
            }
        }

        private static DateTime? CalculateNextRun(ScheduleTrigger trigger)
        {
            if (!trigger.ScheduledTime.HasValue) return null;

            var scheduled = trigger.ScheduledTime.Value;
            var now = DateTime.Now;

            if (trigger.Recurrence == RecurrenceType.None)
            {
                return scheduled > now ? scheduled : null;
            }

            // Find next occurrence based on recurrence
            var nextRun = scheduled;
            while (nextRun <= now)
            {
                nextRun = trigger.Recurrence switch
                {
                    RecurrenceType.Daily => nextRun.AddDays(trigger.RecurrenceInterval),
                    RecurrenceType.Weekly => nextRun.AddDays(7 * trigger.RecurrenceInterval),
                    RecurrenceType.Monthly => nextRun.AddMonths(trigger.RecurrenceInterval),
                    _ => nextRun.AddDays(1)
                };
            }

            return nextRun;
        }
    }
}
