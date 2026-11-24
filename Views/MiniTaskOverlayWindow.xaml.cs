using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AIA.Models;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using TaskStatus = AIA.Models.TaskStatus;
using WinForms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace AIA.Views
{
    /// <summary>
    /// Always-on compact task list shown in the upper-right corner of the active screen.
    /// </summary>
    public partial class MiniTaskOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int MaxVisibleTasks = 5;

        private readonly ObservableCollection<MiniTaskEntry> _entries = new();
        private readonly DispatcherTimer _cursorTimer;
        private readonly DispatcherTimer _refreshTimer;
        private readonly OverlayViewModel _viewModel = OverlayViewModel.Singleton;

        private MiniTaskOverlaySettings _settings = new();
        private IntPtr _handle;
        private bool _isInitialized;
        private bool _isFaded;
        private bool _isCardVisible = true;
        private bool _isAnimatingCard;
        private bool _isRelocating;
        private bool _isLeftDocked;
        private DateTime _drawerCollapseAt = DateTime.MinValue;
        private DateTime _relocateCooldownUntil = DateTime.MinValue;
        private string _currentScreenDevice = string.Empty;

        public MiniTaskOverlayWindow()
        {
            InitializeComponent();

            TaskList.ItemsSource = _entries;

            _cursorTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _cursorTimer.Tick += (_, _) => UpdateCursorState();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += (_, _) => RefreshEntries();

            SourceInitialized += MiniTaskOverlayWindow_SourceInitialized;
            Loaded += MiniTaskOverlayWindow_Loaded;
            Closing += MiniTaskOverlayWindow_Closing;

            _viewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;
        }

        /// <summary>
        /// Applies a new settings instance and keeps the window in sync with later changes.
        /// </summary>
        public void ApplySettings(MiniTaskOverlaySettings settings)
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _settings = settings ?? new MiniTaskOverlaySettings();
            _settings.PropertyChanged += Settings_PropertyChanged;

            ApplySettingsState();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ApplySettingsState));
                return;
            }

            ApplySettingsState();
        }

        private void ApplySettingsState()
        {
            ModeHint.Text = _settings.InteractableMode
                ? "PINNED"
                : _settings.DrawerMode
                    ? "DRAWER"
                    : _settings.RelocateMode
                        ? "AUTO-MOVE"
                        : "AUTO-HIDE";
            ModeHint.Foreground = _settings.InteractableMode
                ? new SolidColorBrush(Color.FromRgb(0x5A, 0xB8, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x84));

            ApplyClickThrough(!_settings.InteractableMode);

            if (!_isInitialized)
            {
                Opacity = _settings.Opacity;
                return;
            }

            if (_settings.InteractableMode)
            {
                EnsureCardVisible(animate: true);
                SetOverlayVisible(true);
            }
            else if (!_settings.DrawerMode)
            {
                EnsureCardVisible(animate: true);
                ApplyOpacityTarget(animate: true);
            }
            else
            {
                ApplyOpacityTarget(animate: true);
            }
        }

        private void MiniTaskOverlayWindow_SourceInitialized(object? sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            ApplyClickThrough(!_settings.InteractableMode);
        }

        private void MiniTaskOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _handle = new WindowInteropHelper(this).Handle;

            var screen = GetCursorScreen();
            _currentScreenDevice = screen.DeviceName ?? string.Empty;
            _isLeftDocked = false;
            PositionWindow(screen);

            Opacity = _settings.Opacity;

            if (_settings.DrawerMode && !_settings.InteractableMode)
            {
                HideCardImmediately();
            }
            else
            {
                ShowCardImmediately();
            }

            RefreshEntries();
            ApplyClickThrough(!_settings.InteractableMode);
            _cursorTimer.Start();
            _refreshTimer.Start();
        }

        private void MiniTaskOverlayWindow_Closing(object? sender, CancelEventArgs e)
        {
            _cursorTimer.Stop();
            _refreshTimer.Stop();
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _viewModel.Tasks.CollectionChanged -= Tasks_CollectionChanged;
        }

        #region Cursor proximity

        private void UpdateCursorState()
        {
            if (!_isInitialized || _handle == IntPtr.Zero || !IsVisible)
                return;

            try
            {
                var cursorScreen = GetCursorScreen();
                if (!string.Equals(cursorScreen.DeviceName, _currentScreenDevice, StringComparison.Ordinal))
                {
                    _currentScreenDevice = cursorScreen.DeviceName ?? string.Empty;
                    _isLeftDocked = false;
                    PositionWindow(cursorScreen);
                }

                var cursor = WinForms.Cursor.Position;
                var cursorPoint = new WpfPoint(cursor.X, cursor.Y);
                var cardRect = _isCardVisible ? GetElementRectPhysical(Card) : GetWindowRectPhysical();
                var dpi = VisualTreeHelper.GetDpi(this);
                var proximity = 64 * ((dpi.DpiScaleX + dpi.DpiScaleY) / 2);

                var nearWindow = Inflate(cardRect, proximity).Contains(cursorPoint);

                if (!_settings.IsEnabled)
                {
                    SetOverlayVisible(false);
                    return;
                }

                if (_settings.InteractableMode)
                {
                    if (_settings.DrawerMode)
                        EnsureCardVisible(animate: true);

                    SetOverlayVisible(true);
                    return;
                }

                if (Application.Current?.MainWindow is MainWindow mainWindow && mainWindow.IsVisible)
                {
                    SetOverlayVisible(false);
                    return;
                }

                if (_settings.DrawerMode)
                {
                    SetOverlayVisible(true);
                    UpdateDrawerState(cursorPoint, proximity);
                    return;
                }

                if (_settings.RelocateMode)
                {
                    SetOverlayVisible(true);
                    if (nearWindow)
                        TryRelocate();
                    return;
                }

                SetOverlayVisible(!nearWindow);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mini task overlay cursor update failed: {ex.Message}");
            }
        }

        private void UpdateDrawerState(WpfPoint cursorPoint, double proximity)
        {
            if (_isRelocating || _isAnimatingCard)
                return;

            var nearBall = false;
            if (Ball.Visibility == Visibility.Visible && Ball.ActualWidth > 0)
            {
                var ballRect = GetElementRectPhysical(Ball);
                nearBall = Inflate(ballRect, proximity * 0.45).Contains(cursorPoint);
            }

            var nearCard = _isCardVisible &&
                           Inflate(GetElementRectPhysical(Card), proximity * 0.5).Contains(cursorPoint);

            if (nearBall || (_isCardVisible && nearCard))
            {
                _drawerCollapseAt = DateTime.UtcNow.AddMilliseconds(450);
                if (!_isCardVisible)
                    AnimateBallToCard();
            }
            else if (_isCardVisible && DateTime.UtcNow >= _drawerCollapseAt)
            {
                AnimateCardToBall();
            }
        }

        private void TryRelocate()
        {
            if (_isRelocating || _isAnimatingCard)
                return;

            if (DateTime.UtcNow < _relocateCooldownUntil)
                return;

            _isRelocating = true;
            _relocateCooldownUntil = DateTime.UtcNow.AddMilliseconds(1800);

            var screen = GetCursorScreen();
            AnimateCardToBall(() =>
            {
                _isLeftDocked = !_isLeftDocked;
                var targetLeft = GetAnchorLeft(screen, _isLeftDocked);

                var move = new DoubleAnimation
                {
                    From = Left,
                    To = targetLeft,
                    Duration = TimeSpan.FromMilliseconds(360),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };

                move.Completed += (_, _) =>
                {
                    BeginAnimation(LeftProperty, null);
                    Left = targetLeft;
                    ApplyDockAlignment(screen);
                    AnimateBallToCard(() => _isRelocating = false);
                };

                BeginAnimation(LeftProperty, move);
            });
        }

        private WpfRect GetWindowRectPhysical()
        {
            var topLeft = PointToScreen(new WpfPoint(0, 0));
            var bottomRight = PointToScreen(new WpfPoint(ActualWidth, ActualHeight));
            return new WpfRect(topLeft, bottomRight);
        }

        private WpfRect GetElementRectPhysical(FrameworkElement element)
        {
            var topLeft = element.PointToScreen(new WpfPoint(0, 0));
            var bottomRight = element.PointToScreen(new WpfPoint(element.ActualWidth, element.ActualHeight));
            return new WpfRect(topLeft, bottomRight);
        }

        private static WpfRect Inflate(WpfRect rect, double amount)
        {
            rect.Inflate(amount, amount);
            return rect;
        }

        #endregion

        #region Positioning

        private static WinForms.Screen GetCursorScreen()
        {
            return WinForms.Screen.FromPoint(WinForms.Cursor.Position) ?? WinForms.Screen.PrimaryScreen!;
        }

        private void PositionWindow(WinForms.Screen screen)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var workArea = screen.WorkingArea;

            Top = (workArea.Top / dpi.DpiScaleY) + 12;
            Left = GetAnchorLeft(screen, _isLeftDocked);

            ApplyDockAlignment(screen);
        }

        private double GetAnchorLeft(WinForms.Screen screen, bool leftDocked)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var workArea = screen.WorkingArea;

            return leftDocked
                ? (workArea.Left / dpi.DpiScaleX) + 8
                : (workArea.Right / dpi.DpiScaleX) - Width - 8;
        }

        private void ApplyDockAlignment(WinForms.Screen screen)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Card.HorizontalAlignment = _isLeftDocked ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
            Ball.HorizontalAlignment = _isLeftDocked ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
            Card.RenderTransformOrigin = _isLeftDocked ? new WpfPoint(0, 0) : new WpfPoint(1, 0);
            Top = (screen.WorkingArea.Top / dpi.DpiScaleY) + 12;
        }

        #endregion

        #region Visibility and animations

        private void SetOverlayVisible(bool visible, bool animate = true)
        {
            if (_isFaded == !visible)
                return;

            _isFaded = !visible;

            if (!_isInitialized || !animate)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = visible ? _settings.Opacity : 0;
                return;
            }

            var animation = new DoubleAnimation
            {
                From = Opacity,
                To = visible ? _settings.Opacity : 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            animation.Completed += (_, _) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = visible ? _settings.Opacity : 0;
            };

            BeginAnimation(OpacityProperty, animation);
        }

        private void ApplyOpacityTarget(bool animate)
        {
            if (_isFaded)
                return;

            if (!_isInitialized || !animate)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = _settings.Opacity;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = _settings.Opacity,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, animation);
        }

        private void EnsureCardVisible(bool animate)
        {
            if (_isCardVisible && !_isAnimatingCard)
                return;

            if (_isAnimatingCard)
            {
                ShowCardImmediately();
                return;
            }

            if (animate && Ball.Visibility == Visibility.Visible)
                AnimateBallToCard();
            else
                ShowCardImmediately();
        }

        private void ShowCardImmediately()
        {
            ResetScale(CardScale, 1);
            ResetOpacity(Card, 1);
            ResetOpacity(Ball, 1);
            Card.Visibility = Visibility.Visible;
            Ball.Visibility = Visibility.Collapsed;
            _isCardVisible = true;
            _isAnimatingCard = false;
        }

        private void HideCardImmediately()
        {
            ResetScale(CardScale, 1);
            ResetOpacity(Card, 1);
            ResetOpacity(Ball, 1);
            Card.Visibility = Visibility.Collapsed;
            Ball.Visibility = Visibility.Visible;
            _isCardVisible = false;
            _isAnimatingCard = false;
        }

        private void AnimateCardToBall(Action? completed = null)
        {
            if (_isAnimatingCard)
                return;

            _isAnimatingCard = true;
            Card.Visibility = Visibility.Visible;
            Ball.Visibility = Visibility.Visible;

            ResetScale(CardScale, 1);
            ResetScale(BallScale, 0.4);
            ResetOpacity(Card, 1);
            ResetOpacity(Ball, 0);

            var duration = TimeSpan.FromMilliseconds(170);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var cardScale = new DoubleAnimation(1, 0.12, duration) { EasingFunction = easeIn };
            var cardFade = new DoubleAnimation(1, 0, duration) { EasingFunction = easeIn };
            var ballFade = new DoubleAnimation(0, 1, duration);
            var ballScale = new DoubleAnimation(0.4, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            cardFade.Completed += (_, _) =>
            {
                Card.Visibility = Visibility.Collapsed;
                _isCardVisible = false;
                ResetScale(BallScale, 1);
                ResetOpacity(Card, 1);
                ResetOpacity(Ball, 1);
                _isAnimatingCard = false;
                completed?.Invoke();
            };

            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, cardScale);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, cardScale);
            Card.BeginAnimation(OpacityProperty, cardFade);
            Ball.BeginAnimation(OpacityProperty, ballFade);
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, ballScale);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, ballScale);
        }

        private void AnimateBallToCard(Action? completed = null)
        {
            if (_isAnimatingCard)
                return;

            _isAnimatingCard = true;
            Card.Visibility = Visibility.Visible;
            Ball.Visibility = Visibility.Visible;
            ResetScale(CardScale, 0.18);
            ResetScale(BallScale, 1);
            ResetOpacity(Card, 0);
            ResetOpacity(Ball, 1);

            var duration = TimeSpan.FromMilliseconds(160);
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var cardScale = new DoubleAnimation(0.18, 1, duration) { EasingFunction = easeOut };
            var cardFade = new DoubleAnimation(0, 1, duration);
            var ballFade = new DoubleAnimation(1, 0, duration);
            var ballScale = new DoubleAnimation(1, 0.4, duration) { EasingFunction = easeOut };

            cardFade.Completed += (_, _) =>
            {
                Ball.Visibility = Visibility.Collapsed;
                ResetScale(CardScale, 1);
                ResetOpacity(Card, 1);
                ResetOpacity(Ball, 1);
                _isCardVisible = true;
                _isAnimatingCard = false;
                completed?.Invoke();
            };

            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, cardScale);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, cardScale);
            Card.BeginAnimation(OpacityProperty, cardFade);
            Ball.BeginAnimation(OpacityProperty, ballFade);
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, ballScale);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, ballScale);
        }

        private static void ResetScale(ScaleTransform transform, double value)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = value;
            transform.ScaleY = value;
        }

        private static void ResetOpacity(UIElement element, double value)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = value;
        }

        #endregion

        #region Click-through

        private void ApplyClickThrough(bool clickThrough)
        {
            if (_handle == IntPtr.Zero)
                return;

            long style = GetWindowExStyle(_handle);
            style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

            if (clickThrough)
                style |= WS_EX_TRANSPARENT;
            else
                style &= ~WS_EX_TRANSPARENT;

            SetWindowExStyle(_handle, style);
        }

        private static long GetWindowExStyle(IntPtr handle)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt64()
                : GetWindowLong32(handle, GWL_EXSTYLE);
        }

        private static void SetWindowExStyle(IntPtr handle, long style)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(handle, GWL_EXSTYLE, new IntPtr(style));
            else
                SetWindowLong32(handle, GWL_EXSTYLE, (int)style);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newValue);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newValue);

        #endregion

        #region Task list

        private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshEntries));
                return;
            }

            RefreshEntries();
        }

        private void RefreshEntries()
        {
            var activeTasks = _viewModel.Tasks
                .Where(task => task != null &&
                               !task.IsArchived &&
                               !task.ParentTaskId.HasValue &&
                               task.Status != TaskStatus.Completed &&
                               task.Status != TaskStatus.Cancelled)
                .OrderBy(task => task.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(task => task.Priority)
                .ThenBy(task => task.CreatedDate)
                .ToList();

            var visibleTasks = activeTasks.Take(MaxVisibleTasks).ToList();

            var unchanged = _entries.Count == visibleTasks.Count &&
                            _entries.Select(entry => entry.Task.Id).SequenceEqual(visibleTasks.Select(task => task.Id));

            if (unchanged)
            {
                foreach (var entry in _entries)
                    entry.Refresh();
            }
            else
            {
                _entries.Clear();
                foreach (var task in visibleTasks)
                    _entries.Add(new MiniTaskEntry(task));
            }

            TaskCountText.Text = activeTasks.Count.ToString();
            BallCountText.Text = activeTasks.Count > 99 ? "99" : activeTasks.Count.ToString();
            Ball.ToolTip = activeTasks.Count == 1
                ? "1 active task"
                : $"{activeTasks.Count} active tasks";

            MoreText.Text = activeTasks.Count > visibleTasks.Count
                ? $"+{activeTasks.Count - visibleTasks.Count} more"
                : string.Empty;
            MoreText.Visibility = activeTasks.Count > visibleTasks.Count
                ? Visibility.Visible
                : Visibility.Collapsed;

            EmptyText.Visibility = activeTasks.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskList.Visibility = activeTasks.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void TaskStatus_Click(object sender, RoutedEventArgs e)
        {
            if (!_settings.InteractableMode)
                return;

            if (sender is FrameworkElement element && element.Tag is MiniTaskEntry entry)
            {
                var task = entry.Task;
                if (task.Status == TaskStatus.Completed)
                {
                    task.Status = TaskStatus.NotStarted;
                    task.CompletedDate = null;
                }
                else
                {
                    task.Status = TaskStatus.Completed;
                }

                _viewModel.QueueTasksSave();
                RefreshEntries();
            }
        }

        private void TaskRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.InteractableMode || e.ClickCount < 2)
                return;

            if (sender is FrameworkElement element && element.Tag is MiniTaskEntry entry)
            {
                _viewModel.SelectedTask = entry.Task;

                if (Application.Current?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ShowWithAnimation();
                    mainWindow.Activate();
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Formats a due date using the largest suitable unit.
        /// </summary>
        public static string FormatCountdown(DateTime? dueDate)
        {
            if (!dueDate.HasValue)
                return "--";

            var difference = dueDate.Value - DateTime.Now;
            var overdue = difference < TimeSpan.Zero;
            var span = difference.Duration();

            string value;
            if (span.TotalDays >= 365)
                value = $"{(int)(span.TotalDays / 365)}y";
            else if (span.TotalDays >= 30)
                value = $"{(int)(span.TotalDays / 30)}mo";
            else if (span.TotalDays >= 7)
                value = $"{(int)(span.TotalDays / 7)}w";
            else if (span.TotalDays >= 1)
                value = $"{(int)span.TotalDays}d";
            else if (span.TotalHours >= 1)
                value = $"{(int)span.TotalHours}h";
            else if (span.TotalMinutes >= 1)
                value = $"{(int)span.TotalMinutes}m";
            else
                value = "<1m";

            return overdue ? $"-{value}" : value;
        }

        #endregion
    }

    /// <summary>
    /// Item displayed in the compact task list.
    /// </summary>
    public sealed class MiniTaskEntry : INotifyPropertyChanged
    {
        private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

        public MiniTaskEntry(TaskItem task)
        {
            Task = task;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TaskItem Task { get; }

        public string Title => string.IsNullOrWhiteSpace(Task.Title) ? "(untitled)" : Task.Title;

        public string CountdownText => MiniTaskOverlayWindow.FormatCountdown(Task.DueDate);

        public Brush CountdownBrush
        {
            get
            {
                if (!Task.DueDate.HasValue)
                    return GetBrush("#7A7A84");

                var difference = Task.DueDate.Value - DateTime.Now;
                if (difference < TimeSpan.Zero)
                    return GetBrush("#FF6B6B");
                if (difference <= TimeSpan.FromHours(1))
                    return GetBrush("#FFB454");
                if (difference <= TimeSpan.FromDays(1))
                    return GetBrush("#9CDCFE");

                return GetBrush("#9A9AA4");
            }
        }

        public Brush PriorityBrush => GetBrush(Task.PriorityColor);

        public Brush StatusBrush => GetBrush(Task.StatusColor);

        public string StatusGlyph => Task.Status switch
        {
            TaskStatus.InProgress => "\u25B6",
            TaskStatus.OnHold => "\u2161",
            TaskStatus.Completed => "\u2713",
            TaskStatus.Cancelled => "\u00D7",
            _ => "\u25CB"
        };

        public string Tooltip
        {
            get
            {
                var line1 = Title;
                var line2 = Task.DueDate.HasValue
                    ? $"{Task.DueDateText} ({CountdownText})"
                    : Task.DueDateText;
                return $"{line1}\n{line2}\n{Task.Status} \u00B7 {Task.Priority} priority";
            }
        }

        public void Refresh()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountdownBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PriorityBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tooltip)));
        }

        private static Brush GetBrush(string color)
        {
            if (BrushCache.TryGetValue(color, out var cached))
                return cached;

            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            BrushCache[color] = brush;
            return brush;
        }
    }
}
