using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models.Automation;
using AIA.Services.Automation;
using AIA.Views.Automation;
using Microsoft.Win32;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIA
{
    /// <summary>
    /// Interaction logic for AutomationWindow.xaml
    /// </summary>
    public partial class AutomationWindow : Window, INotifyPropertyChanged
    {
        private readonly AutomationService _automationService;
        private AutomationTask? _selectedAutomation;

        public event PropertyChangedEventHandler? PropertyChanged;

        public static Array AgentTypeValues => Enum.GetValues(typeof(AgentType));

        public ObservableCollection<AutomationTask> Automations => _automationService.Automations;
        public ObservableCollection<AutomationExecution> RunningExecutions => _automationService.RunningExecutions;
        public ObservableCollection<AutomationExecution> ExecutionHistory => _automationService.ExecutionHistory;
        public AutomationSettings Settings => _automationService.Settings;

        public AutomationTask? SelectedAutomation
        {
            get => _selectedAutomation;
            set
            {
                _selectedAutomation = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAutomation)));
                UpdateTriggerTypeSelection();
            }
        }

        public AutomationWindow(AutomationService automationService)
        {
            InitializeComponent();
            _automationService = automationService;
            DataContext = this;

            // Update running count display
            _automationService.RunningExecutions.CollectionChanged += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RunningCountText.Text = _automationService.RunningExecutions.Count.ToString();
                });
            };
        }

        #region Global Settings

        private void GlobalEnableToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (Settings.IsEnabled)
            {
                _automationService.Start();
                StatusText.Text = "Automation service started";
            }
            else
            {
                _automationService.Stop();
                StatusText.Text = "Automation service stopped";
            }
        }

        #endregion

        #region Automation CRUD

        private void BtnAddAutomation(object sender, RoutedEventArgs e)
        {
            var automation = _automationService.CreateAutomation("New Automation", "Description");
            SelectedAutomation = automation;
            StatusText.Text = "Created new automation";
        }

        private void AutomationItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AutomationTask automation)
            {
                SelectedAutomation = automation;
            }
        }

        private void AutomationToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is AutomationTask automation)
            {
                if (checkBox.IsChecked == true)
                {
                    _automationService.EnableAutomation(automation);
                    StatusText.Text = $"Enabled '{automation.Name}'";
                }
                else
                {
                    _automationService.DisableAutomation(automation);
                    StatusText.Text = $"Disabled '{automation.Name}'";
                }
            }
        }

        private async void BtnRunAutomation(object sender, RoutedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            StatusText.Text = $"Running '{SelectedAutomation.Name}'...";
            var execution = await _automationService.TriggerManuallyAsync(SelectedAutomation);

            if (execution != null)
            {
                StatusText.Text = execution.IsSuccess
                    ? $"'{SelectedAutomation.Name}' completed successfully"
                    : $"'{SelectedAutomation.Name}' failed: {execution.Error}";
            }
        }

        private void BtnDuplicateAutomation(object sender, RoutedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            var duplicate = _automationService.DuplicateAutomation(SelectedAutomation);
            SelectedAutomation = duplicate;
            StatusText.Text = $"Duplicated automation as '{duplicate.Name}'";
        }

        private async void BtnExportAutomation(object sender, RoutedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Automation",
                FileName = $"{SelectedAutomation.Name.Replace(" ", "_")}.json",
                DefaultExt = ".json",
                Filter = "JSON files|*.json|All files|*.*"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var success = await _automationService.ExportAutomationAsync(SelectedAutomation, saveDialog.FileName);
                StatusText.Text = success
                    ? $"Exported '{SelectedAutomation.Name}' to {saveDialog.FileName}"
                    : "Export failed";
            }
        }

        private async void BtnDeleteAutomation(object sender, RoutedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{SelectedAutomation.Name}'?",
                "Delete Automation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var name = SelectedAutomation.Name;
                await _automationService.DeleteAutomationAsync(SelectedAutomation);
                SelectedAutomation = Automations.FirstOrDefault();
                StatusText.Text = $"Deleted '{name}'";
            }
        }

        private async void BtnImportAutomation(object sender, RoutedEventArgs e)
        {
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Automation",
                DefaultExt = ".json",
                Filter = "JSON files|*.json|All files|*.*"
            };

            if (openDialog.ShowDialog() == true)
            {
                var automation = await _automationService.ImportAutomationAsync(openDialog.FileName);
                if (automation != null)
                {
                    SelectedAutomation = automation;
                    StatusText.Text = $"Imported '{automation.Name}'";
                }
                else
                {
                    StatusText.Text = "Import failed - invalid file format";
                }
            }
        }

        #endregion

        #region Trigger Configuration

        private void UpdateTriggerTypeSelection()
        {
            if (SelectedAutomation?.Trigger == null)
            {
                TriggerTypeCombo.SelectedIndex = 0; // Manual
                UpdateTriggerConfigUI();
                return;
            }

            TriggerTypeCombo.SelectedIndex = SelectedAutomation.Trigger.TriggerType switch
            {
                TriggerType.Manual => 0,
                TriggerType.Clipboard => 1,
                TriggerType.Hotkey => 2,
                TriggerType.FileChange => 3,
                TriggerType.WindowContext => 4,
                TriggerType.Schedule => 5,
                TriggerType.AutomationChain => 6,
                _ => 0
            };
            
            UpdateTriggerConfigUI();
        }

        private void TriggerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            var selectedIndex = TriggerTypeCombo.SelectedIndex;
            AutomationTrigger newTrigger = selectedIndex switch
            {
                0 => new ManualTrigger { Name = "Manual Trigger" },
                1 => new ClipboardTrigger { Name = "Clipboard Trigger" },
                2 => new HotkeyTrigger { Name = "Hotkey Trigger" },
                3 => new FileChangeTrigger { Name = "File Change Trigger" },
                4 => new WindowContextTrigger { Name = "Window Context Trigger" },
                5 => new ScheduleTrigger { Name = "Schedule Trigger" },
                6 => new AutomationChainTrigger { Name = "Chain Trigger" },
                _ => new ManualTrigger { Name = "Manual Trigger" }
            };

            // Preserve ID if same type
            if (SelectedAutomation.Trigger?.TriggerType == newTrigger.TriggerType)
            {
                return;
            }

            SelectedAutomation.Trigger = newTrigger;
            UpdateTriggerConfigUI();
        }

        private void UpdateTriggerConfigUI()
        {
            if (SelectedAutomation?.Trigger == null)
            {
                TriggerConfigContent.Content = null;
                return;
            }

            FrameworkElement? configView = SelectedAutomation.Trigger switch
            {
                ClipboardTrigger => new ClipboardTriggerConfigView(),
                HotkeyTrigger => new HotkeyTriggerConfigView(),
                FileChangeTrigger => new FileChangeTriggerConfigView(),
                WindowContextTrigger => new WindowContextTriggerConfigView(),
                ScheduleTrigger => new ScheduleTriggerConfigView(),
                AutomationChainTrigger => CreateChainTriggerView(),
                PluginTrigger => new PluginTriggerConfigView(),
                ManualTrigger => null, // Manual trigger needs no configuration
                _ => null
            };

            if (configView != null)
            {
                configView.DataContext = SelectedAutomation.Trigger;
            }

            TriggerConfigContent.Content = configView;
        }

        private FrameworkElement CreateChainTriggerView()
        {
            var view = new AutomationChainTriggerConfigView();
            
            // Provide the list of available automations for chaining
            // Exclude the current automation to prevent self-referencing
            if (SelectedAutomation != null)
            {
                var availableAutomations = Automations
                    .Where(a => a.Id != SelectedAutomation.Id)
                    .ToList();
                
                // Create a wrapper to provide AvailableAutomations to the view
                view.DataContext = new ChainTriggerViewModel(
                    SelectedAutomation.Trigger as AutomationChainTrigger, 
                    availableAutomations);
            }
            
            return view;
        }

        #endregion

        #region Actions

        private void BtnAddAction(object sender, RoutedEventArgs e)
        {
            if (SelectedAutomation == null) return;

            var action = new AutomationAction
            {
                ActionType = ActionType.Notification,
                Name = "New Action"
            };

            SelectedAutomation.Actions.Add(action);
            StatusText.Text = "Added new action";
        }

        private void BtnRemoveAction(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AutomationAction action)
            {
                SelectedAutomation?.Actions.Remove(action);
                StatusText.Text = "Removed action";
            }
        }

        #endregion

        #region Running Executions

        private void BtnPauseExecution(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AutomationExecution execution)
            {
                var automation = Automations.FirstOrDefault(a => a.Id == execution.AutomationId);
                if (automation != null)
                {
                    _automationService.PauseAutomation(automation);
                    StatusText.Text = $"Paused '{automation.Name}'";
                }
            }
        }

        private async void BtnCancelExecution(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AutomationExecution execution)
            {
                var automation = Automations.FirstOrDefault(a => a.Id == execution.AutomationId);
                if (automation != null)
                {
                    await _automationService.CancelExecutionAsync(automation);
                    StatusText.Text = $"Cancelled '{automation.Name}'";
                }
            }
        }

        #endregion

        #region History

        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AutomationExecution execution)
            {
                // Show execution details dialog
                ShowExecutionDetails(execution);
            }
        }

        private void ShowExecutionDetails(AutomationExecution execution)
        {
            var detailsWindow = new Window
            {
                Title = $"Execution Details - {execution.AutomationName}",
                Width = 700,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 30, 30))
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16)
            };

            var stackPanel = new StackPanel();

            // Summary
            stackPanel.Children.Add(CreateDetailText($"Automation: {execution.AutomationName}", true));
            stackPanel.Children.Add(CreateDetailText($"Status: {execution.Status}"));
            stackPanel.Children.Add(CreateDetailText($"Started: {execution.StartedAt:g}"));
            stackPanel.Children.Add(CreateDetailText($"Duration: {execution.DurationText}"));
            stackPanel.Children.Add(CreateDetailText($"Iterations: {execution.CurrentIteration}"));
            stackPanel.Children.Add(CreateDetailText($"Tokens Used: {execution.TotalTokensUsed}"));
            stackPanel.Children.Add(CreateDetailText($"Trigger: {execution.TriggerDescription}"));

            if (!string.IsNullOrEmpty(execution.Error))
            {
                stackPanel.Children.Add(CreateDetailText($"Error: {execution.Error}", false, "#FF4444"));
            }

            // Result
            if (!string.IsNullOrEmpty(execution.Result))
            {
                stackPanel.Children.Add(CreateDetailText("Result:", true));
                var resultBox = new System.Windows.Controls.TextBox
                {
                    Text = execution.Result,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(45, 45, 48)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                stackPanel.Children.Add(resultBox);
            }

            // Trace Log
            stackPanel.Children.Add(CreateDetailText("Trace Log:", true));
            foreach (var trace in execution.TraceLog)
            {
                var traceText = $"[{trace.Timestamp:HH:mm:ss}] [{trace.Level}] {trace.Message}";
                if (!string.IsNullOrEmpty(trace.Details))
                {
                    traceText += $"\n    {trace.Details}";
                }
                stackPanel.Children.Add(CreateDetailText(traceText, false, trace.LevelColor));
            }

            scrollViewer.Content = stackPanel;
            detailsWindow.Content = scrollViewer;
            detailsWindow.ShowDialog();
        }

        private static TextBlock CreateDetailText(string text, bool isBold = false, string? color = null)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    color != null
                        ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)
                        : System.Windows.Media.Colors.White),
                FontWeight = isBold ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4)
            };
            return textBlock;
        }

        private async void BtnClearHistory(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to clear all execution history?",
                "Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _automationService.ClearHistoryAsync();
                StatusText.Text = "Execution history cleared";
            }
        }

        #endregion

        #region Save/Close

        private async void BtnSave(object sender, RoutedEventArgs e)
        {
            await _automationService.SaveSettingsAsync();

            foreach (var automation in Automations)
            {
                await _automationService.UpdateAutomationAsync(automation);
            }

            StatusText.Text = "All changes saved";
        }

        private void BtnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
