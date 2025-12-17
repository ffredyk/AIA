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
using AIA.Services.AI;

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
            OutlookTab.DataContext = Models.OverlayViewModel.Singleton;
            TeamsTab.DataContext = Models.OverlayViewModel.Singleton;

            // Wire up toolbar events
            Toolbar.NewTaskClicked += OnNewTaskClicked;
            Toolbar.NewReminderClicked += OnNewReminderClicked;
            Toolbar.OrchestrationClicked += OnOrchestrationClicked;
            Toolbar.ShutdownClicked += OnShutdownClicked;
            Toolbar.CloseClicked += OnCloseClicked;

            // Wire up toast events from all child views
            DataAssets.ToastRequested += OnToastRequested;
            OutlookTab.ToastRequested += OnToastRequested;
            TeamsTab.ToastRequested += OnToastRequested;

            // Subscribe to reminder notifications
            Models.OverlayViewModel.Singleton.ReminderNotificationTriggered += OnReminderNotificationTriggered;

            // Handle key events at the Window level
            KeyUp += KeyUpOverlay;

            // Set fullscreen overlay when window loads
            Loaded += MainWindow_Loaded;
            
            // Prevent window state changes
            StateChanged += PreventWindowStateChange;
            
            // Handle when window becomes visible
            IsVisibleChanged += MainWindow_IsVisibleChanged;
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

        private void OnOrchestrationClicked(object? sender, EventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            var orchestrationWindow = new OrchestrationWindow(viewModel.AIOrchestration);
            orchestrationWindow.Owner = this;
            orchestrationWindow.ShowDialog();
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

        private void ShowToast(string message)
        {
            var toast = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 183, 95)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 100),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = System.Windows.Media.Brushes.White,
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

        public new void Show()
        {
            base.Show();
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