using System;
using System.Windows;
using System.Windows.Controls;
using AIA.Models;

namespace AIA.Dialogs
{
    public partial class RecurrenceConfigDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly TaskItem _task;

        public RecurrenceConfigDialog(TaskItem task)
        {
            InitializeComponent();
            _task = task;
            
            LoadTaskRecurrence();
            UpdatePreview();
            
            // Wire up event handlers
            CmbRecurrenceType.SelectionChanged += (s, e) => UpdateIntervalUnit();
            CmbRecurrenceType.SelectionChanged += (s, e) => UpdatePreview();
            NumInterval.ValueChanged += (s, e) => UpdatePreview();
            ChkSetEndDate.Checked += (s, e) => UpdatePreview();
            ChkSetEndDate.Unchecked += (s, e) => UpdatePreview();
            DateEndDate.SelectedDateChanged += (s, e) => UpdatePreview();
        }

        private void LoadTaskRecurrence()
        {
            ChkEnableRecurrence.IsChecked = _task.IsRecurring;
            
            CmbRecurrenceType.SelectedIndex = _task.RecurrenceType switch
            {
                RecurrenceType.Daily => 0,
                RecurrenceType.Weekly => 1,
                RecurrenceType.Monthly => 2,
                RecurrenceType.Yearly => 3,
                _ => 0
            };
            
            NumInterval.Value = _task.RecurrenceInterval;
            
            if (_task.RecurrenceEndDate.HasValue)
            {
                ChkSetEndDate.IsChecked = true;
                DateEndDate.SelectedDate = _task.RecurrenceEndDate;
            }
            
            UpdateIntervalUnit();
        }

        private void ChkEnableRecurrence_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdateIntervalUnit()
        {
            if (CmbRecurrenceType.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string;
                TxtIntervalUnit.Text = tag switch
                {
                    "Daily" => Services.LocalizationService.Instance["RecurrenceUnit_Days"] ?? "day(s)",
                    "Weekly" => Services.LocalizationService.Instance["RecurrenceUnit_Weeks"] ?? "week(s)",
                    "Monthly" => Services.LocalizationService.Instance["RecurrenceUnit_Months"] ?? "month(s)",
                    "Yearly" => Services.LocalizationService.Instance["RecurrenceUnit_Years"] ?? "year(s)",
                    _ => "day(s)"
                };
            }
        }

        private void UpdatePreview()
        {
            if (ChkEnableRecurrence.IsChecked != true)
            {
                TxtPreview.Text = "Recurrence is disabled.";
                return;
            }

            var interval = (int)(NumInterval.Value ?? 1);
            var typeText = "";
            
            if (CmbRecurrenceType.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag as string;
                typeText = tag switch
                {
                    "Daily" => interval == 1 ? "every day" : $"every {interval} days",
                    "Weekly" => interval == 1 ? "every week" : $"every {interval} weeks",
                    "Monthly" => interval == 1 ? "every month" : $"every {interval} months",
                    "Yearly" => interval == 1 ? "every year" : $"every {interval} years",
                    _ => ""
                };
            }

            var preview = $"This task will repeat {typeText}";
            
            if (ChkSetEndDate.IsChecked == true && DateEndDate.SelectedDate.HasValue)
            {
                preview += $" until {DateEndDate.SelectedDate.Value:MMM dd, yyyy}";
            }
            else
            {
                preview += " indefinitely";
            }
            
            preview += ".";
            
            TxtPreview.Text = preview;
        }

        private void BtnOK(object sender, RoutedEventArgs e)
        {
            // Apply recurrence settings to task
            var isRecurring = ChkEnableRecurrence.IsChecked ?? false;
            
            if (isRecurring)
            {
                if (CmbRecurrenceType.SelectedItem is ComboBoxItem item)
                {
                    var tag = item.Tag as string;
                    _task.RecurrenceType = tag switch
                    {
                        "Daily" => RecurrenceType.Daily,
                        "Weekly" => RecurrenceType.Weekly,
                        "Monthly" => RecurrenceType.Monthly,
                        "Yearly" => RecurrenceType.Yearly,
                        _ => RecurrenceType.Daily
                    };
                }
                
                _task.RecurrenceInterval = (int)(NumInterval.Value ?? 1);
                
                if (ChkSetEndDate.IsChecked == true && DateEndDate.SelectedDate.HasValue)
                {
                    _task.RecurrenceEndDate = DateEndDate.SelectedDate;
                }
                else
                {
                    _task.RecurrenceEndDate = null;
                }
            }
            else
            {
                _task.RecurrenceType = RecurrenceType.None;
            }
            
            // Save tasks
            _ = OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
            
            DialogResult = true;
            Close();
        }

        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
