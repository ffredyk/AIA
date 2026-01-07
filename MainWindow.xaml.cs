using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Windows.Interop;
using System.Linq;
using AIA.Models;
using AIA.Models.AI;
using AIA.Services;
using AIA.Services.AI;
using AIA.Services.Automation;
using AIA.Plugins.Host.Services;
using AIA.Plugins.SDK;

// Alias to resolve ambiguous references
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfTabControl = System.Windows.Controls.TabControl;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace AIA
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isAnimating = false;
        private readonly List<ReminderNotificationWindow> _activeNotifications = new();
        
        // Track conversation history for AI
        private readonly List<AIMessage> _conversationHistory = new();

        // Automation service
        private AutomationService? _automationService;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = Models.OverlayViewModel.Singleton;

            // Set DataContext for child views
            TasksTab.DataContext = Models.OverlayViewModel.Singleton;
            RemindersTab.DataContext = Models.OverlayViewModel.Singleton;
            ChatPanel.DataContext = Models.OverlayViewModel.Singleton;
            DataAssets.DataContext = Models.OverlayViewModel.Singleton;
            DataBanksTab.DataContext = Models.OverlayViewModel.Singleton;

            // Wire up toolbar events
            Toolbar.NewTaskClicked += OnNewTaskClicked;
            Toolbar.NewReminderClicked += OnNewReminderClicked;
            Toolbar.SettingsClicked += OnSettingsClicked;
            Toolbar.OrchestrationClicked += OnOrchestrationClicked;
            Toolbar.AutomationClicked += OnAutomationClicked;
            Toolbar.ShutdownClicked += OnShutdownClicked;
            Toolbar.CloseClicked += OnCloseClicked;

            // Wire up toast events from all child views
            DataAssets.ToastRequested += OnToastRequested;

            // Subscribe to reminder notifications
            Models.OverlayViewModel.Singleton.ReminderNotificationTriggered += OnReminderNotificationTriggered;

            // Subscribe to notification service events
            NotificationService.Instance.ToastRequested += OnNotificationServiceToastRequested;
            NotificationService.Instance.RichNotificationRequested += OnNotificationServiceRichNotificationRequested;

            // Subscribe to plugin UI events when service is available
            var viewModel = Models.OverlayViewModel.Singleton;
            if (viewModel.PluginUIService != null)
            {
                WireUpPluginUIEvents(viewModel.PluginUIService);
            }
            else
            {
                // Wait for plugin service to be set
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // Handle key events at the Window level
            KeyUp += KeyUpOverlay;

            // Set fullscreen overlay when window loads
            Loaded += MainWindow_Loaded;
            
            // Prevent window state changes
            StateChanged += PreventWindowStateChange;
            
            // Handle when window becomes visible
            IsVisibleChanged += MainWindow_IsVisibleChanged;

            // Get automation service from ViewModel and wire up events
            WireUpAutomationService();
        }

        private void WireUpAutomationService()
        {
            _automationService = Models.OverlayViewModel.Singleton.AutomationService;
            
            if (_automationService != null)
            {
                // Wire up automation events
                _automationService.ExecutionStarted += OnAutomationExecutionStarted;
                _automationService.ExecutionCompleted += OnAutomationExecutionCompleted;
                _automationService.ConfirmationRequired += OnAutomationConfirmationRequired;
            }
        }

        private void OnAutomationExecutionStarted(object? sender, Models.Automation.AutomationExecution execution)
        {
            if (_automationService?.Settings.ShowExecutionNotifications == true)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowToast($"Automation started: {execution.AutomationName}", WpfColor.FromArgb(220, 0, 120, 212));
                });
            }
        }

        private void OnAutomationExecutionCompleted(object? sender, Models.Automation.AutomationExecution execution)
        {
            if (_automationService?.Settings.ShowExecutionNotifications == true)
            {
                Dispatcher.Invoke(() =>
                {
                    var color = execution.IsSuccess
                        ? WpfColor.FromArgb(220, 30, 183, 95)
                        : WpfColor.FromArgb(220, 255, 68, 68);
                    var message = execution.IsSuccess
                        ? $"Automation completed: {execution.AutomationName}"
                        : $"Automation failed: {execution.AutomationName}";
                    ShowToast(message, color);
                });
            }
        }

        private void OnAutomationConfirmationRequired(object? sender, ConfirmationRequestEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var result = System.Windows.MessageBox.Show(
                    e.Message,
                    "Automation Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    e.Confirm();
                }
                else
                {
                    e.Deny();
                }
            });
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OverlayViewModel.PluginUIService))
            {
                var viewModel = Models.OverlayViewModel.Singleton;
                if (viewModel.PluginUIService != null)
                {
                    WireUpPluginUIEvents(viewModel.PluginUIService);
                }
            }
            else if (e.PropertyName == nameof(OverlayViewModel.AutomationService))
            {
                WireUpAutomationService();
            }
        }

        private void WireUpPluginUIEvents(HostPluginUIService pluginUIService)
        {
            pluginUIService.ToastRequested += OnPluginToastRequested;
            
            // Bind plugin tabs to the TabControl
            pluginUIService.Tabs.CollectionChanged += OnPluginTabsChanged;
            
            // Add existing tabs
            foreach (var tab in pluginUIService.Tabs)
            {
                AddPluginTab(tab);
            }
            
            // Bind plugin toolbar buttons
            pluginUIService.ToolbarButtons.CollectionChanged += OnPluginToolbarButtonsChanged;
        }

        private void OnPluginTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (PluginTabDefinition tab in e.NewItems)
                {
                    AddPluginTab(tab);
                }
            }

            if (e.OldItems != null)
            {
                foreach (PluginTabDefinition tab in e.OldItems)
                {
                    RemovePluginTab(tab);
                }
            }
        }

        private void AddPluginTab(PluginTabDefinition tab)
        {
            Dispatcher.Invoke(() =>
            {
                var tabItem = new TabItem
                {
                    Tag = tab.TabId
                };

                // Create header with icon and badge
                var headerPanel = new StackPanel { Orientation = WpfOrientation.Horizontal };
                
                var icon = new Wpf.Ui.Controls.SymbolIcon
                {
                    Symbol = GetSymbolFromName(tab.IconSymbol),
                    Margin = new Thickness(0, 0, 6, 0)
                };
                headerPanel.Children.Add(icon);
                
                var titleText = new TextBlock { Text = tab.Title };
                headerPanel.Children.Add(titleText);
                
                // Badge for notifications
                if (tab.BadgeCount > 0)
                {
                    var badge = new Border
                    {
                        Margin = new Thickness(6, 0, 0, 0),
                        Padding = new Thickness(4, 1, 4, 1),
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(tab.BadgeColor))
                    };
                    badge.Child = new TextBlock
                    {
                        Text = tab.BadgeCount.ToString(),
                        FontSize = 10,
                        Foreground = WpfBrushes.White
                    };
                    headerPanel.Children.Add(badge);
                }
                
                tabItem.Header = headerPanel;

                // Set content using the plugin's data template
                if (tab.ViewModel != null)
                {
                    var contentPresenter = new ContentPresenter
                    {
                        Content = tab.ViewModel,
                        ContentTemplate = tab.ViewModel.GetDataTemplate()
                    };
                    tabItem.Content = contentPresenter;
                }

                // Find the right position to insert based on Order
                var mainTabControl = FindMainTabControl();
                if (mainTabControl != null)
                {
                    var insertIndex = mainTabControl.Items.Count;
                    for (int i = 0; i < mainTabControl.Items.Count; i++)
                    {
                        if (mainTabControl.Items[i] is TabItem existingTab && existingTab.Tag is string tagId)
                        {
                            // Check if this is a plugin tab with higher order
                            var existingPluginTab = Models.OverlayViewModel.Singleton.PluginTabs?
                                .FirstOrDefault(t => t.TabId == tagId);
                            if (existingPluginTab != null && existingPluginTab.Order > tab.Order)
                            {
                                insertIndex = i;
                                break;
                            }
                        }
                    }
                    mainTabControl.Items.Insert(insertIndex, tabItem);
                }
            });
        }

        private void RemovePluginTab(PluginTabDefinition tab)
        {
            Dispatcher.Invoke(() =>
            {
                var mainTabControl = FindMainTabControl();
                if (mainTabControl != null)
                {
                    var tabToRemove = mainTabControl.Items.Cast<TabItem>()
                        .FirstOrDefault(t => t.Tag as string == tab.TabId);
                    if (tabToRemove != null)
                    {
                        mainTabControl.Items.Remove(tabToRemove);
                    }
                }
            });
        }

        private WpfTabControl? FindMainTabControl()
        {
            // Find the main TabControl by name
            return MainTabControl;
        }

        private static Wpf.Ui.Controls.SymbolRegular GetSymbolFromName(string symbolName)
        {
            if (Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symbolName, out var symbol))
            {
                return symbol;
            }
            return Wpf.Ui.Controls.SymbolRegular.Document20;
        }

        private void OnPluginToolbarButtonsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Toolbar buttons are handled by the ToolbarView through binding
            // This is a placeholder for any additional handling needed
        }

        private void OnPluginToastRequested(object? sender, ToastEventArgs e)
        {
            var color = e.Type switch
            {
                ToastType.Success => WpfColor.FromArgb(220, 30, 183, 95),
                ToastType.Warning => WpfColor.FromArgb(220, 255, 165, 0),
                ToastType.Error => WpfColor.FromArgb(220, 255, 68, 68),
                _ => WpfColor.FromArgb(220, 0, 120, 212)
            };
            ShowToast(e.Message, color);
        }

        private void OnNotificationServiceToastRequested(object? sender, NotificationEventArgs e)
        {
            var color = e.Type switch
            {
                NotificationType.Success => WpfColor.FromArgb(220, 30, 183, 95),
                NotificationType.Warning => WpfColor.FromArgb(220, 255, 165, 0),
                NotificationType.Error => WpfColor.FromArgb(220, 255, 68, 68),
                _ => WpfColor.FromArgb(220, 0, 120, 212)
            };
            ShowToast(e.Message, color);
        }

        private void OnNotificationServiceRichNotificationRequested(object? sender, NotificationEventArgs e)
        {
            // For rich notifications, create a notification similar to reminder notifications
            // but with automation styling
            ShowAutomationNotification(e);
        }

        private void ShowAutomationNotification(NotificationEventArgs e)
        {
            // Use the same toast system for now, but with title
            var color = e.Type switch
            {
                NotificationType.Success => WpfColor.FromArgb(220, 30, 183, 95),
                NotificationType.Warning => WpfColor.FromArgb(220, 255, 165, 0),
                NotificationType.Error => WpfColor.FromArgb(220, 255, 68, 68),
                _ => WpfColor.FromArgb(220, 0, 120, 212)
            };

            var message = string.IsNullOrEmpty(e.Title) 
                ? e.Message 
                : $"{e.Title}: {e.Message}";
            
            ShowToast(message, color);
        }

        #region Toolbar Events

        private void OnNewTaskClicked(object? sender, EventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.IsAddingNewTask = true;
            TasksTab.FocusNewTaskInput();
        }

        private void OnNewReminderClicked(object? sender, EventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.IsAddingNewReminder = true;
            RemindersTab.FocusNewReminderInput();
        }

        private void OnSettingsClicked(object? sender, EventArgs e)
        {
            var settingsWindow = new SettingsWindow(App.Current.PluginManager);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void OnOrchestrationClicked(object? sender, EventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            var orchestrationWindow = new OrchestrationWindow(viewModel.AIOrchestration);
            orchestrationWindow.Owner = this;
            orchestrationWindow.ShowDialog();
        }

        private void OnAutomationClicked(object? sender, EventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            var automationWindow = new AutomationWindow(viewModel.AutomationService);
            automationWindow.Owner = this;
            automationWindow.ShowDialog();
        }

        private void OnShutdownClicked(object? sender, EventArgs e)
        {
            AnimateSlideOut(() => System.Windows.Application.Current.Shutdown());
        }

        private void OnCloseClicked(object? sender, EventArgs e)
        {
            AnimateSlideOut(() => Hide());
        }

        #endregion

        #region Toast Notifications

        private void OnToastRequested(object? sender, string message)
        {
            ShowToast(message);
        }

        private void ShowToast(string message, WpfColor? backgroundColor = null)
        {
            var bgColor = backgroundColor ?? WpfColor.FromArgb(220, 30, 183, 95);
            
            var toast = new Border
            {
                Background = new SolidColorBrush(bgColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 100),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = WpfBrushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13
                }
            };

            if (Content is Grid mainGrid)
            {
                Grid.SetRow(toast, 2);
                Grid.SetColumnSpan(toast, 3);
                mainGrid.Children.Add(toast);

                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    BeginTime = TimeSpan.FromSeconds(1.5)
                };

                fadeOut.Completed += (s, args) => mainGrid.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        #endregion

        #region Window Lifecycle

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            Models.OverlayViewModel.Singleton.SetOverlayWindowHandle(helper.Handle);
            
            SetFullscreenOverlay();
            
            Models.OverlayViewModel.Singleton.PauseWindowTracking();
            Models.OverlayViewModel.Singleton.CaptureCurrentDataAssets();
            
            AnimateSlideIn();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                SetFullscreenOverlay();
                
                Models.OverlayViewModel.Singleton.PauseWindowTracking();
                Models.OverlayViewModel.Singleton.CaptureCurrentDataAssets();
                
                AnimateSlideIn();
            }
            else
            {
                Models.OverlayViewModel.Singleton.ResumeWindowTracking();
            }
        }

        private void SetFullscreenOverlay()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            
            Left = screen.Left;
            Top = screen.Top;
            Width = screen.Width;
            Height = screen.Height;
            WindowState = WindowState.Normal;
        }

        private void AnimateSlideIn()
        {
            if (isAnimating) return;
            isAnimating = true;

            var slideAnimation = new DoubleAnimation
            {
                From = -50,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(100)
            };

            if (RenderTransform == null || RenderTransform == Transform.Identity)
            {
                RenderTransform = new TranslateTransform();
            }

            slideAnimation.Completed += (s, e) => { isAnimating = false; };

            RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
            BeginAnimation(OpacityProperty, fadeAnimation);
        }

        private void AnimateSlideOut(Action onCompleted)
        {
            if (isAnimating) return;
            isAnimating = true;

            var slideAnimation = new DoubleAnimation
            {
                From = 0,
                To = -30,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var fadeAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(100)
            };

            slideAnimation.Completed += (s, e) =>
            {
                isAnimating = false;
                if (RenderTransform is TranslateTransform transform)
                {
                    transform.Y = 0;
                }
                onCompleted?.Invoke();
            };

            RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
            BeginAnimation(OpacityProperty, fadeAnimation);
        }

        public new void Show()
        {
            base.Show();
        }

        /// <summary>
        /// Shows the window with slide-in animation
        /// </summary>
        public void ShowWithAnimation()
        {
            // Reset opacity and transform before showing to ensure animation plays correctly
            Opacity = 0;
            if (RenderTransform is TranslateTransform transform)
            {
                transform.Y = -50;
            }
            
            base.Show();
            Activate();
        }

        /// <summary>
        /// Hides the window with slide-out animation
        /// </summary>
        public void HideWithAnimation()
        {
            AnimateSlideOut(() => Hide());
        }

        private void PreventWindowStateChange(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Normal && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            
            if (WindowState == WindowState.Minimized)
            {
                AnimateSlideOut(() => Hide());
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            AnimateSlideOut(() => Hide());
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (Visibility == Visibility.Visible)
            {
                SetFullscreenOverlay();
            }
        }

        private void KeyUpOverlay(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                AnimateSlideOut(() => Hide());
            }
        }

        #endregion

        #region Reminder Notifications

        private void OnReminderNotificationTriggered(object? sender, ReminderNotification notification)
        {
            ShowReminderNotification(notification);
        }

        private void ShowReminderNotification(ReminderNotification notification)
        {
            var notificationWindow = new ReminderNotificationWindow();

            notificationWindow.ViewRequested += OnNotificationViewRequested;
            notificationWindow.CompletionRequested += OnNotificationCompletionRequested;
            notificationWindow.SnoozeRequested += OnNotificationSnoozeRequested;
            notificationWindow.Closed += OnNotificationWindowClosed;

            PositionNotificationWindow(notificationWindow);

            _activeNotifications.Add(notificationWindow);

            var settings = Models.OverlayViewModel.Singleton.NotificationSettings;
            notificationWindow.ShowNotification(notification, settings.NotificationDurationSeconds);
        }

        private void PositionNotificationWindow(ReminderNotificationWindow window)
        {
            int notificationIndex = _activeNotifications.Count;
            double verticalOffset = notificationIndex * 130;

            window.Loaded += (s, e) =>
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                var workArea = screen.WorkingArea;
                var dpiScale = VisualTreeHelper.GetDpi(window);
                double scaleFactor = dpiScale.DpiScaleX;
                double screenTop = workArea.Top / scaleFactor;

                window.Top = screenTop + 20 + verticalOffset;
            };
        }

        private void OnNotificationWindowClosed(object? sender, EventArgs e)
        {
            if (sender is ReminderNotificationWindow window)
            {
                _activeNotifications.Remove(window);
                RepositionActiveNotifications();
            }
        }

        private void RepositionActiveNotifications()
        {
            for (int i = 0; i < _activeNotifications.Count; i++)
            {
                var window = _activeNotifications[i];
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) continue;

                var workArea = screen.WorkingArea;
                var dpiScale = VisualTreeHelper.GetDpi(window);
                double scaleFactor = dpiScale.DpiScaleX;
                double screenTop = workArea.Top / scaleFactor;

                double targetTop = screenTop + 20 + (i * 130);

                var animation = new DoubleAnimation
                {
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                window.BeginAnimation(TopProperty, animation);
            }
        }

        private void OnNotificationViewRequested(object? sender, ReminderItem reminder)
        {
            var viewModel = Models.OverlayViewModel.Singleton;
            viewModel.SelectedReminder = reminder;

            Show();
            Activate();
        }

        private void OnNotificationCompletionRequested(object? sender, ReminderItem reminder)
        {
            var viewModel = Models.OverlayViewModel.Singleton;
            viewModel.ToggleReminderComplete(reminder);
        }

        private void OnNotificationSnoozeRequested(object? sender, ReminderItem reminder)
        {
            var viewModel = Models.OverlayViewModel.Singleton;
            viewModel.SnoozeReminder(reminder);
        }

        #endregion
    }
}