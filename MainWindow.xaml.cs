using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using AIWrap;
using AIA.Models;

namespace AIA
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isAnimating = false;
        
        public MainWindow()
        {
            InitializeComponent();
            DataContext = Models.OverlayViewModel.Singleton;

            // Handle key events at the Window level
            KeyUp += KeyUpOverlay;

            // Set fullscreen overlay when window loads
            Loaded += MainWindow_Loaded;
            
            // Prevent window state changes
            StateChanged += PreventWindowStateChange;
            
            // Handle when window becomes visible
            IsVisibleChanged += MainWindow_IsVisibleChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set overlay window handle for screen capture service
            var helper = new WindowInteropHelper(this);
            Models.OverlayViewModel.Singleton.SetOverlayWindowHandle(helper.Handle);
            
            SetFullscreenOverlay();
            
            // Pause tracking and capture data assets before showing
            Models.OverlayViewModel.Singleton.PauseWindowTracking();
            Models.OverlayViewModel.Singleton.CaptureCurrentDataAssets();
            
            AnimateSlideIn();
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                SetFullscreenOverlay();
                
                // Pause tracking and capture data assets when becoming visible
                Models.OverlayViewModel.Singleton.PauseWindowTracking();
                Models.OverlayViewModel.Singleton.CaptureCurrentDataAssets();
                
                AnimateSlideIn();
            }
            else
            {
                // Resume tracking when hidden
                Models.OverlayViewModel.Singleton.ResumeWindowTracking();
            }
        }

        private void SetFullscreenOverlay()
        {
            // Get the primary screen bounds
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            
            // Set window to cover entire screen
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

            // Create subtle slide animation from top (only 50 pixels)
            var slideAnimation = new DoubleAnimation
            {
                From = -50,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // Create fade in animation
            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(100)
            };

            // Create transform if it doesn't exist
            if (RenderTransform == null || RenderTransform == Transform.Identity)
            {
                RenderTransform = new TranslateTransform();
            }

            slideAnimation.Completed += (s, e) => { isAnimating = false; };

            // Apply animations
            RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
            BeginAnimation(OpacityProperty, fadeAnimation);
        }

        private void AnimateSlideOut(Action onCompleted)
        {
            if (isAnimating) return;
            isAnimating = true;

            // Create subtle slide animation going up (only 30 pixels)
            var slideAnimation = new DoubleAnimation
            {
                From = 0,
                To = -30,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            // Create fade out animation
            var fadeAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(100)
            };

            slideAnimation.Completed += (s, e) =>
            {
                isAnimating = false;
                // Reset transform for next show
                if (RenderTransform is TranslateTransform transform)
                {
                    transform.Y = 0;
                }
                onCompleted?.Invoke();
            };

            // Apply animations
            RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
            BeginAnimation(OpacityProperty, fadeAnimation);
        }

        private void PreventWindowStateChange(object? sender, EventArgs e)
        {
            // Only allow Normal state for fullscreen overlay
            if (WindowState != WindowState.Normal && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            
            // Handle minimize to tray
            if (WindowState == WindowState.Minimized)
            {
                AnimateSlideOut(() => Hide());
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            AnimateSlideOut(() => Hide());
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Ensure fullscreen when window is shown again
            if (Visibility == Visibility.Visible)
            {
                SetFullscreenOverlay();
            }
        }

        public new void Show()
        {
            base.Show();
            // Animation will be triggered by IsVisibleChanged event
        }

        private void BtnClose(object sender, RoutedEventArgs e)
        {
            AnimateSlideOut(() => Hide());
        }

        private void BtnShutdown(object sender, RoutedEventArgs e)
        {
            // Animate out before shutting down
            AnimateSlideOut(() => System.Windows.Application.Current.Shutdown());
        }

        private void KeyUpOverlay(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if(e.Key == Key.Escape)
            {
                AnimateSlideOut(() => Hide());
            }
        }

        #region Task Management Events

        private void BtnNewTask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.IsAddingNewTask = true;
            NewTaskTitleInput?.Focus();
        }

        private void BtnConfirmNewTask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.AddNewTask(viewModel.NewTaskTitle);
        }

        private void BtnCancelNewTask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.NewTaskTitle = string.Empty;
            viewModel.IsAddingNewTask = false;
        }

        private void NewTaskTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewTask(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewTask(sender, e);
                e.Handled = true;
            }
        }

        private void TaskItem_Click(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TaskItem task)
            {
                viewModel.SelectedTask = task;
            }
        }

        private void BtnToggleExpand(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is TaskItem task)
            {
                task.IsExpanded = !task.IsExpanded;
            }
            e.Handled = true;
        }

        private void BtnDeleteTask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedTask == null) return;

            viewModel.DeleteTask(viewModel.SelectedTask);
        }

        private void BtnDeleteSubtask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is TaskItem subtask)
            {
                viewModel.DeleteTask(subtask);
            }
        }

        private void BtnAddSubtask(object sender, RoutedEventArgs e)
        {
            NewSubtaskInputPanel.Visibility = Visibility.Visible;
            NewSubtaskTitleInput?.Focus();
        }

        private void BtnConfirmNewSubtask(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedTask == null) return;

            var title = NewSubtaskTitleInput?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                viewModel.AddSubtask(viewModel.SelectedTask, title);
                viewModel.SelectedTask.IsExpanded = true;
            }

            NewSubtaskTitleInput.Text = string.Empty;
            NewSubtaskInputPanel.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelNewSubtask(object sender, RoutedEventArgs e)
        {
            NewSubtaskTitleInput.Text = string.Empty;
            NewSubtaskInputPanel.Visibility = Visibility.Collapsed;
        }

        private void NewSubtaskTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewSubtask(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewSubtask(sender, e);
                e.Handled = true;
            }
        }

        #endregion

        #region Reminder Management Events

        private void BtnNewReminder(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.IsAddingNewReminder = true;
            // Focus the input using FindName since it's in a TabItem
            var input = FindName("NewReminderTitleInput") as System.Windows.Controls.TextBox;
            input?.Focus();
        }

        private void BtnConfirmNewReminder(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.AddNewReminder(viewModel.NewReminderTitle);
        }

        private void BtnCancelNewReminder(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.NewReminderTitle = string.Empty;
            viewModel.IsAddingNewReminder = false;
        }

        private void NewReminderTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewReminder(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewReminder(sender, e);
                e.Handled = true;
            }
        }

        private void ReminderItem_Click(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is ReminderItem reminder)
            {
                viewModel.SelectedReminder = reminder;
                
                // Update time combo boxes to match the selected reminder's time
                var hourCombo = FindName("ReminderHourCombo") as System.Windows.Controls.ComboBox;
                var minuteCombo = FindName("ReminderMinuteCombo") as System.Windows.Controls.ComboBox;
                
                if (hourCombo != null)
                    hourCombo.SelectedIndex = reminder.DueDate.Hour;
                
                // Map minute to combo box index (0, 5, 10, 15... -> 0, 1, 2, 3...)
                if (minuteCombo != null)
                {
                    int minuteIndex = reminder.DueDate.Minute / 5;
                    if (minuteIndex >= 0 && minuteIndex < minuteCombo.Items.Count)
                    {
                        minuteCombo.SelectedIndex = minuteIndex;
                    }
                }
            }
        }

        private void BtnDeleteReminder(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedReminder == null) return;

            viewModel.DeleteReminder(viewModel.SelectedReminder);
        }

        private void BtnToggleReminderComplete(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is ReminderItem reminder)
            {
                viewModel.ToggleReminderComplete(reminder);
            }
            e.Handled = true;
        }

        private void ReminderTimeChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedReminder == null) return;

            var hourCombo = FindName("ReminderHourCombo") as System.Windows.Controls.ComboBox;
            var minuteCombo = FindName("ReminderMinuteCombo") as System.Windows.Controls.ComboBox;

            if (hourCombo == null || minuteCombo == null) return;

            // Prevent updating during initial selection
            if (hourCombo.SelectedIndex < 0 || minuteCombo.SelectedIndex < 0)
                return;

            int hour = hourCombo.SelectedIndex;
            int minute = minuteCombo.SelectedIndex * 5; // Convert index to actual minutes (0, 5, 10...)

            var currentDate = viewModel.SelectedReminder.DueDate;
            viewModel.SelectedReminder.DueDate = new DateTime(
                currentDate.Year, 
                currentDate.Month, 
                currentDate.Day, 
                hour, 
                minute, 
                0);
        }

        #endregion

        #region Chat Events

        private async void BtnSendMessage(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private void MessageInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                _ = SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            var viewModel = DataContext as Models.OverlayViewModel;
            if (viewModel == null || viewModel.SelectedChatSession == null || App.AIRoot == null)
                return;

            var userMessage = MessageInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            MessageInput.Clear();
            MessageInput.Focus();

            var userChatMessage = new Models.ChatMessage
            {
                Role = "user",
                Content = userMessage
            };
            viewModel.SelectedChatSession.Messages.Add(userChatMessage);

            var assistantMessage = new Models.ChatMessage
            {
                Role = "assistant",
                Content = "Thinking..."
            };
            viewModel.SelectedChatSession.Messages.Add(assistantMessage);

            try
            {
                var response = await Task.Run(() => 
                {
                    var aiRootType = App.AIRoot.GetType();

                    var request = App.AIRoot.CreateRequest();

                    var generateMethod = aiRootType.GetMethod("Generate", new[] { typeof(string) });
                    if (generateMethod != null)
                    {
                        var result = generateMethod.Invoke(App.AIRoot, new object[] { userMessage });
                        return result?.ToString() ?? "No response generated.";
                    }

                    var chatMethod = aiRootType.GetMethod("Chat", new[] { typeof(string) });
                    if (chatMethod != null)
                    {
                        var result = chatMethod.Invoke(App.AIRoot, new object[] { userMessage });
                        return result?.ToString() ?? "No response generated.";
                    }

                    var completeMethod = aiRootType.GetMethod("Complete", new[] { typeof(string) });
                    if (completeMethod != null)
                    {
                        var result = completeMethod.Invoke(App.AIRoot, new object[] { userMessage });
                        return result?.ToString() ?? "No response generated.";
                    }

                    return $"AIController type: {aiRootType.FullName}. Available methods: {string.Join(", ", aiRootType.GetMethods().Select(m => m.Name).Distinct())}";
                });

                assistantMessage.Content = response;
            }
            catch (Exception ex)
            {
                assistantMessage.Content = $"Error: {ex.Message}";
            }
        }

        #endregion

        #region Data Bank Management Events

        private void BtnAddCategory(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.IsAddingNewCategory = true;
            var input = FindName("NewCategoryNameInput") as System.Windows.Controls.TextBox;
            input?.Focus();
        }

        private async void BtnConfirmNewCategory(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            await viewModel.AddNewCategoryAsync(viewModel.NewCategoryName);
        }

        private void BtnCancelNewCategory(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.NewCategoryName = string.Empty;
            viewModel.IsAddingNewCategory = false;
        }

        private void NewCategoryNameInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewCategory(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewCategory(sender, e);
                e.Handled = true;
            }
        }

        private void CategoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankCategory category)
            {
                viewModel.SelectedCategory = category;
                viewModel.SelectedDataEntry = null;
            }
        }

        private async void BtnDeleteCategory(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankCategory category)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to delete the category '{category.Name}' and all its entries?",
                    "Delete Category",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await viewModel.DeleteCategoryAsync(category);
                }
            }
            e.Handled = true;
        }

        private void BtnAddEntry(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null || viewModel.SelectedCategory == null) return;

            viewModel.IsAddingNewEntry = true;
            var input = FindName("NewEntryTitleInput") as System.Windows.Controls.TextBox;
            input?.Focus();
        }

        private async void BtnConfirmNewEntry(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            await viewModel.AddNewEntryAsync(viewModel.NewEntryTitle);
        }

        private void BtnCancelNewEntry(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            viewModel.NewEntryTitle = string.Empty;
            viewModel.IsAddingNewEntry = false;
        }

        private void NewEntryTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewEntry(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewEntry(sender, e);
                e.Handled = true;
            }
        }

        private async void BtnImportFile(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null || viewModel.SelectedCategory == null) return;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import File to Data Bank",
                Filter = "All Supported Files|*.txt;*.md;*.pdf;*.eml;*.msg;*.json;*.xml;*.csv;*.log|" +
                         "Text Files|*.txt;*.md;*.log|" +
                         "PDF Files|*.pdf|" +
                         "Email Files|*.eml;*.msg|" +
                         "Data Files|*.json;*.xml;*.csv|" +
                         "All Files|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var filePath in openFileDialog.FileNames)
                {
                    await viewModel.ImportFileAsync(filePath);
                }
            }
        }

        private void DataEntryItem_Click(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankEntry entry)
            {
                // Refresh preview when selecting an entry
                entry.RefreshPreview();
                viewModel.SelectedDataEntry = entry;
            }
        }

        private async void BtnDeleteEntry(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedDataEntry == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{viewModel.SelectedDataEntry.Title}'?",
                "Delete Entry",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await viewModel.DeleteEntryAsync(viewModel.SelectedDataEntry);
            }
        }

        private async void DataEntryField_LostFocus(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedDataEntry == null) return;

            await viewModel.UpdateEntryAsync();
        }

        private async void DataEntryType_Changed(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedDataEntry == null) return;

            // Refresh preview when type changes
            viewModel.SelectedDataEntry.RefreshPreview();
            await viewModel.UpdateEntryAsync();
        }

        private void BtnOpenFile(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            if (viewModel?.SelectedDataEntry == null) return;

            var filePath = viewModel.SelectedDataEntry.FilePath;
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                System.Windows.MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Data Assets Events

        private void BtnRefreshDataAssets(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as OverlayViewModel;
            viewModel?.CaptureCurrentDataAssets();
        }

        private void DataAssetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is DataAsset asset)
            {
                // Show the full image in a preview window
                ShowDataAssetPreview(asset);
            }
        }

        private void ShowDataAssetPreview(DataAsset asset)
        {
            if (asset.FullImage == null) return;

            var previewWindow = new Window
            {
                Title = asset.Name,
                Width = Math.Min(asset.FullImage.PixelWidth + 40, SystemParameters.PrimaryScreenWidth * 0.9),
                Height = Math.Min(asset.FullImage.PixelHeight + 80, SystemParameters.PrimaryScreenHeight * 0.9),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 30, 30, 30)),
                ResizeMode = ResizeMode.CanResize,
                Owner = this
            };

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var image = new System.Windows.Controls.Image
            {
                Source = asset.FullImage,
                Stretch = Stretch.None
            };

            scrollViewer.Content = image;
            previewWindow.Content = scrollViewer;
            previewWindow.ShowDialog();
        }

        private void BtnCopyAssetToClipboard(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            if (viewModel.CopyDataAssetToClipboard(asset))
            {
                // Show brief feedback
                ShowToast("Copied to clipboard!");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to copy to clipboard.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAssetToDisk(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            var filePath = viewModel.SaveDataAssetToFile(asset);
            if (!string.IsNullOrEmpty(filePath))
            {
                ShowToast($"Saved to {System.IO.Path.GetFileName(filePath)}");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save file.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAssetAs(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null) return;

            var filePath = viewModel.SaveDataAssetWithDialog(asset);
            if (!string.IsNullOrEmpty(filePath))
            {
                ShowToast($"Saved to {System.IO.Path.GetFileName(filePath)}");
            }
        }

        private async void BtnSaveAssetToDataBank(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            var viewModel = DataContext as OverlayViewModel;
            if (viewModel == null || viewModel.SelectedCategory == null)
            {
                System.Windows.MessageBox.Show("Please select a data bank category first.", "No Category Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var success = await viewModel.SaveDataAssetToDataBankAsync(asset);
            if (success)
            {
                ShowToast($"Saved to '{viewModel.SelectedCategory.Name}' data bank");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save to data bank.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowToast(string message)
        {
            // Create a simple toast notification
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

            // Add to the main grid
            if (Content is Grid mainGrid)
            {
                Grid.SetRow(toast, 2);
                Grid.SetColumnSpan(toast, 3);
                mainGrid.Children.Add(toast);

                // Fade out and remove after 2 seconds
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
    }
}