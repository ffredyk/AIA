using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AIA.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace AIA
{
    /// <summary>
    /// A popup notification window for reminders that appears in the upper right corner
    /// </summary>
    public partial class ReminderNotificationWindow : Window
    {
        private ReminderNotification? _notification;
        private DispatcherTimer? _autoDismissTimer;
        private DispatcherTimer? _progressTimer;
        private double _remainingSeconds;
        private double _totalSeconds;
        private bool _isClosing = false;

        /// <summary>
        /// Event fired when the user clicks "View" to see the reminder
        /// </summary>
        public event EventHandler<ReminderItem>? ViewRequested;

        /// <summary>
        /// Event fired when the user marks the reminder as complete
        /// </summary>
        public event EventHandler<ReminderItem>? CompletionRequested;

        /// <summary>
        /// Event fired when the user snoozes the reminder
        /// </summary>
        public event EventHandler<ReminderItem>? SnoozeRequested;

        public ReminderNotificationWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
            PlaySlideInAnimation();
        }

        /// <summary>
        /// Shows the notification for the specified reminder
        /// </summary>
        public void ShowNotification(ReminderNotification notification, int autoDismissSeconds = 10)
        {
            _notification = notification;
            _totalSeconds = autoDismissSeconds;
            _remainingSeconds = autoDismissSeconds;

            UpdateUI();

            if (autoDismissSeconds > 0)
            {
                StartAutoDismissTimer(autoDismissSeconds);
            }
            else
            {
                ProgressBorder.Visibility = Visibility.Collapsed;
            }

            Show();
        }

        private void UpdateUI()
        {
            if (_notification == null) return;

            // Set title
            ReminderTitle.Text = _notification.Reminder.Title;

            // Set time message
            TimeMessage.Text = _notification.Message;

            // Set due date text
            var dueDate = _notification.Reminder.DueDate;
            if (dueDate.Date == DateTime.Today)
            {
                DueDateText.Text = $"Due: Today at {dueDate:h:mm tt}";
            }
            else if (dueDate.Date == DateTime.Today.AddDays(1))
            {
                DueDateText.Text = $"Due: Tomorrow at {dueDate:h:mm tt}";
            }
            else if (dueDate.Date == DateTime.Today.AddDays(-1))
            {
                DueDateText.Text = $"Due: Yesterday at {dueDate:h:mm tt}";
            }
            else
            {
                DueDateText.Text = $"Due: {dueDate:MMM dd} at {dueDate:h:mm tt}";
            }

            // Set urgency-specific styling
            ApplyUrgencyStyle(_notification.Urgency);
        }

        private void ApplyUrgencyStyle(NotificationUrgency urgency)
        {
            Color accentColor;
            Color backgroundColor;
            string labelText;

            switch (urgency)
            {
                case NotificationUrgency.Warning:
                    accentColor = (Color)ColorConverter.ConvertFromString("#FFA500")!;
                    backgroundColor = (Color)ColorConverter.ConvertFromString("#33FFA500")!;
                    labelText = "APPROACHING";
                    UrgencyIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Clock12;
                    break;

                case NotificationUrgency.Urgent:
                    accentColor = (Color)ColorConverter.ConvertFromString("#FF6B00")!;
                    backgroundColor = (Color)ColorConverter.ConvertFromString("#44FF6B00")!;
                    labelText = "DUE SOON";
                    UrgencyIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Warning16;
                    break;

                case NotificationUrgency.Overdue:
                    accentColor = (Color)ColorConverter.ConvertFromString("#FF4444")!;
                    backgroundColor = (Color)ColorConverter.ConvertFromString("#55FF4444")!;
                    labelText = "OVERDUE";
                    UrgencyIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.AlertUrgent16;
                    break;

                default:
                    accentColor = (Color)ColorConverter.ConvertFromString("#0078D4")!;
                    backgroundColor = (Color)ColorConverter.ConvertFromString("#330078D4")!;
                    labelText = "REMINDER";
                    UrgencyIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Alert16;
                    break;
            }

            // Apply colors
            UrgencyLabel.Text = labelText;
            UrgencyLabel.Foreground = new SolidColorBrush(accentColor);
            UrgencyIconBorder.Background = new SolidColorBrush(backgroundColor);
            UrgencyIcon.Foreground = new SolidColorBrush(accentColor);
            HeaderBorder.Background = new SolidColorBrush(backgroundColor);
            TimeBadge.Background = new SolidColorBrush(backgroundColor);
            TimeIconColor.Color = accentColor;
            TimeTextColor.Color = accentColor;
            BorderColor.Color = Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B);
            ProgressBarElement.Background = new SolidColorBrush(accentColor);
        }

        private void PositionWindow()
        {
            // Position in upper right corner of primary screen
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;

            var workArea = screen.WorkingArea;
            var dpiScale = VisualTreeHelper.GetDpi(this);

            // Convert to WPF units
            double scaleFactor = dpiScale.DpiScaleX;
            double screenWidth = workArea.Width / scaleFactor;
            double screenHeight = workArea.Height / scaleFactor;
            double screenLeft = workArea.Left / scaleFactor;
            double screenTop = workArea.Top / scaleFactor;

            // Position with margin from edges
            Left = screenLeft + screenWidth - Width - 20;
            Top = screenTop + 20;
        }

        private void StartAutoDismissTimer(int seconds)
        {
            _autoDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            _autoDismissTimer.Tick += (s, e) =>
            {
                _autoDismissTimer?.Stop();
                DismissWithAnimation();
            };
            _autoDismissTimer.Start();

            // Progress bar animation
            ProgressBarElement.Width = ProgressBorder.ActualWidth;
            _progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _progressTimer.Tick += (s, e) =>
            {
                _remainingSeconds -= 0.05;
                if (_remainingSeconds <= 0)
                {
                    _progressTimer?.Stop();
                    return;
                }

                var progress = _remainingSeconds / _totalSeconds;
                ProgressBarElement.Width = ProgressBorder.ActualWidth * progress;
            };
            _progressTimer.Start();
        }

        private void PlaySlideInAnimation()
        {
            var storyboard = (Storyboard)FindResource("SlideInAnimation");
            storyboard.Begin(this);
        }

        private void DismissWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;

            _autoDismissTimer?.Stop();
            _progressTimer?.Stop();

            var storyboard = (Storyboard)FindResource("SlideOutAnimation");
            storyboard.Completed += (s, e) => Close();
            storyboard.Begin(this);
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e)
        {
            DismissWithAnimation();
        }

        private void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            if (_notification?.Reminder != null)
            {
                CompletionRequested?.Invoke(this, _notification.Reminder);
            }
            DismissWithAnimation();
        }

        private void BtnSnooze_Click(object sender, RoutedEventArgs e)
        {
            if (_notification?.Reminder != null)
            {
                SnoozeRequested?.Invoke(this, _notification.Reminder);
            }
            DismissWithAnimation();
        }

        private void BtnView_Click(object sender, RoutedEventArgs e)
        {
            if (_notification?.Reminder != null)
            {
                ViewRequested?.Invoke(this, _notification.Reminder);
            }
            DismissWithAnimation();
        }

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            // Pause auto-dismiss when mouse enters
            _autoDismissTimer?.Stop();
            _progressTimer?.Stop();
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            // Resume auto-dismiss when mouse leaves
            if (!_isClosing && _remainingSeconds > 0)
            {
                _autoDismissTimer?.Start();
                _progressTimer?.Start();
            }
        }
    }
}
