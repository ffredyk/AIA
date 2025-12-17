using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AIA.Plugins.SDK;

// Alias to resolve ambiguous reference
using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.Outlook
{
    [Plugin("AIA.Outlook", "Outlook Integration", IconSymbol = "Mail20", IsBuiltIn = true)]
    public class OutlookPlugin : PluginBase
    {
        private OutlookTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Outlook";
        public override string Name => "Outlook Integration";
        public override string Description => "Integrates with Microsoft Outlook to display and manage flagged emails";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.ComAutomation |
            PluginPermissions.ReadReminders |
            PluginPermissions.WriteReminders;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Outlook plugin...");

            _tabViewModel = new OutlookTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            // Register the tab
            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "outlook",
                Title = "Outlook",
                IconSymbol = "Mail20",
                Order = 40,
                ViewModel = _tabViewModel,
                BadgeColor = "#33FF4444"
            });

            LogInfo("Outlook plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Outlook plugin...");

            // Start refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_tabViewModel != null)
                {
                    await _tabViewModel.RefreshAsync();
                }
            };
            _refreshTimer.Start();

            // Initial refresh
            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Outlook plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Outlook plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Outlook plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("outlook");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new OutlookSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Outlook tab
    /// </summary>
    public class OutlookTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly OutlookService _outlookService;
        private bool _isLoading;
        private bool _isAvailable;
        private string _statusMessage = string.Empty;
        private OutlookEmailViewModel? _selectedEmail;
        private DataTemplate? _cachedTemplate;

        public ObservableCollection<OutlookEmailViewModel> FlaggedEmails { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            set => SetProperty(ref _isAvailable, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public OutlookEmailViewModel? SelectedEmail
        {
            get => _selectedEmail;
            set
            {
                if (SetProperty(ref _selectedEmail, value))
                {
                    OnPropertyChanged(nameof(HasSelectedEmail));
                }
            }
        }

        public bool HasSelectedEmail => SelectedEmail != null;

        public int EmailCount => FlaggedEmails.Count;

        public OutlookTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _outlookService = new OutlookService();
        }

        public async Task InitializeAsync()
        {
            IsAvailable = _outlookService.IsOutlookAvailable();
            if (!IsAvailable)
            {
                StatusMessage = "Outlook is not installed or not available.";
            }
        }

        public async Task RefreshAsync()
        {
            if (!IsAvailable)
            {
                _outlookService.ResetAvailabilityCheck();
                IsAvailable = _outlookService.IsOutlookAvailable();
                if (!IsAvailable)
                {
                    StatusMessage = "Outlook is not available. Click refresh to retry.";
                    return;
                }
            }

            IsLoading = true;
            StatusMessage = "Loading flagged emails...";

            try
            {
                var (emails, timedOut) = await _outlookService.GetFlaggedEmailsWithTimeoutAsync();

                if (timedOut)
                {
                    StatusMessage = "Timeout loading emails. Outlook may be busy.";
                    IsLoading = false;
                    return;
                }

                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    FlaggedEmails.Clear();
                    foreach (var email in emails)
                    {
                        FlaggedEmails.Add(new OutlookEmailViewModel(email, _outlookService, this));
                    }
                });

                StatusMessage = FlaggedEmails.Count == 0
                    ? "No flagged emails found."
                    : $"{FlaggedEmails.Count} flagged email(s)";

                OnPropertyChanged(nameof(EmailCount));

                // Update tab badge
                UpdateBadge();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _context.Logger.Error($"Failed to refresh Outlook emails: {ex.Message}", ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task MarkEmailCompleteAsync(OutlookEmailViewModel email)
        {
            var success = await _outlookService.MarkFlagCompleteAsync(email.EntryId);
            if (success)
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    FlaggedEmails.Remove(email);
                    if (SelectedEmail == email)
                        SelectedEmail = null;
                });
                StatusMessage = $"{FlaggedEmails.Count} flagged email(s)";
                OnPropertyChanged(nameof(EmailCount));
                UpdateBadge();
                _context.UI.ShowToast("Email flag marked as complete", ToastType.Success);
            }
        }

        public async Task ClearEmailFlagAsync(OutlookEmailViewModel email)
        {
            var success = await _outlookService.ClearFlagAsync(email.EntryId);
            if (success)
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    FlaggedEmails.Remove(email);
                    if (SelectedEmail == email)
                        SelectedEmail = null;
                });
                StatusMessage = $"{FlaggedEmails.Count} flagged email(s)";
                OnPropertyChanged(nameof(EmailCount));
                UpdateBadge();
                _context.UI.ShowToast("Email flag cleared", ToastType.Info);
            }
        }

        public async Task OpenEmailInOutlookAsync(OutlookEmailViewModel email)
        {
            await _outlookService.OpenEmailAsync(email.EntryId);
        }

        private void UpdateBadge()
        {
            // Badge update would be handled through the UI service
        }

        public override DataTemplate GetDataTemplate()
        {
            // Return cached template or create a simple placeholder
            // The actual UI will be provided by the host application
            if (_cachedTemplate == null)
            {
                _cachedTemplate = CreateDataTemplate();
            }
            return _cachedTemplate;
        }

        private DataTemplate CreateDataTemplate()
        {
            // Create a functional data template programmatically
            var template = new DataTemplate();
            
            // Main grid
            var mainGrid = new FrameworkElementFactory(typeof(Grid));
            mainGrid.SetValue(Grid.MarginProperty, new Thickness(8));
            
            // Create a stack panel for content
            var mainPanel = new FrameworkElementFactory(typeof(StackPanel));
            
            // Status message
            var statusText = new FrameworkElementFactory(typeof(TextBlock));
            statusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusMessage"));
            statusText.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
            statusText.SetValue(TextBlock.FontSizeProperty, 12.0);
            statusText.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 8));
            mainPanel.AppendChild(statusText);
            
            // Emails header
            var emailsHeader = new FrameworkElementFactory(typeof(TextBlock));
            emailsHeader.SetValue(TextBlock.TextProperty, "FLAGGED EMAILS");
            emailsHeader.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
            emailsHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            emailsHeader.SetValue(TextBlock.FontSizeProperty, 11.0);
            emailsHeader.SetValue(TextBlock.MarginProperty, new Thickness(0, 8, 0, 4));
            mainPanel.AppendChild(emailsHeader);
            
            // Emails list
            var emailsItems = new FrameworkElementFactory(typeof(ItemsControl));
            emailsItems.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("FlaggedEmails"));
            
            // Email item template
            var emailItemTemplate = new DataTemplate();
            var emailBorder = new FrameworkElementFactory(typeof(Border));
            emailBorder.SetValue(Border.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 68, 68)));
            emailBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            emailBorder.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
            emailBorder.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            
            var emailGrid = new FrameworkElementFactory(typeof(Grid));
            
            // Two columns - content and actions
            var contentCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            contentCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var actionCol = new FrameworkElementFactory(typeof(ColumnDefinition));
            actionCol.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            
            var emailStack = new FrameworkElementFactory(typeof(StackPanel));
            emailStack.SetValue(Grid.ColumnProperty, 0);
            
            // Subject
            var emailSubject = new FrameworkElementFactory(typeof(TextBlock));
            emailSubject.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Subject"));
            emailSubject.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
            emailSubject.SetValue(TextBlock.FontSizeProperty, 13.0);
            emailSubject.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            emailSubject.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            emailStack.AppendChild(emailSubject);
            
            // Sender
            var emailSender = new FrameworkElementFactory(typeof(TextBlock));
            emailSender.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("SenderName"));
            emailSender.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.LightGray);
            emailSender.SetValue(TextBlock.FontSizeProperty, 11.0);
            emailStack.AppendChild(emailSender);
            
            // Preview
            var emailPreview = new FrameworkElementFactory(typeof(TextBlock));
            emailPreview.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("BodyPreview"));
            emailPreview.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
            emailPreview.SetValue(TextBlock.FontSizeProperty, 11.0);
            emailPreview.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            emailPreview.SetValue(TextBlock.MaxHeightProperty, 32.0);
            emailStack.AppendChild(emailPreview);
            
            // Date and flag status
            var emailMeta = new FrameworkElementFactory(typeof(StackPanel));
            emailMeta.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            emailMeta.SetValue(StackPanel.MarginProperty, new Thickness(0, 4, 0, 0));
            
            var emailDate = new FrameworkElementFactory(typeof(TextBlock));
            emailDate.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ReceivedDateText"));
            emailDate.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
            emailDate.SetValue(TextBlock.FontSizeProperty, 10.0);
            emailMeta.AppendChild(emailDate);
            
            var emailFlagDue = new FrameworkElementFactory(typeof(TextBlock));
            emailFlagDue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("FlagDueDateText"));
            emailFlagDue.SetValue(TextBlock.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 68, 68)));
            emailFlagDue.SetValue(TextBlock.FontSizeProperty, 10.0);
            emailFlagDue.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 0, 0));
            emailMeta.AppendChild(emailFlagDue);
            
            emailStack.AppendChild(emailMeta);
            emailGrid.AppendChild(emailStack);
            
            emailBorder.AppendChild(emailGrid);
            emailItemTemplate.VisualTree = emailBorder;
            emailsItems.SetValue(ItemsControl.ItemTemplateProperty, emailItemTemplate);
            
            mainPanel.AppendChild(emailsItems);
            
            // Wrap in ScrollViewer
            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollViewer.AppendChild(mainPanel);
            
            mainGrid.AppendChild(scrollViewer);
            
            template.VisualTree = mainGrid;
            
            return template;
        }

        public override void Refresh()
        {
            _ = RefreshAsync();
        }

        public void Dispose()
        {
            // Cleanup
        }
    }

    /// <summary>
    /// View model for individual Outlook emails
    /// </summary>
    public class OutlookEmailViewModel : INotifyPropertyChanged
    {
        private readonly OutlookTabViewModel _parent;
        private readonly OutlookService _service;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string EntryId { get; }
        public string Subject { get; }
        public string SenderName { get; }
        public string SenderEmail { get; }
        public DateTime ReceivedDate { get; }
        public string BodyPreview { get; }
        public string Body { get; }
        public bool IsRead { get; }
        public string Importance { get; }
        public DateTime? FlagDueDate { get; }

        public string ReceivedDateText => GetRelativeDateText(ReceivedDate);
        public string FlagDueDateText => FlagDueDate.HasValue ? $"Due: {GetRelativeDateText(FlagDueDate.Value)}" : "";
        public bool IsOverdue => FlagDueDate.HasValue && FlagDueDate.Value.Date < DateTime.Today;
        public string SenderInitials => GetInitials(SenderName);

        public OutlookEmailViewModel(OutlookEmailData data, OutlookService service, OutlookTabViewModel parent)
        {
            _service = service;
            _parent = parent;

            EntryId = data.EntryId;
            Subject = data.Subject;
            SenderName = data.SenderName;
            SenderEmail = data.SenderEmail;
            ReceivedDate = data.ReceivedDate;
            BodyPreview = data.BodyPreview;
            Body = data.Body;
            IsRead = data.IsRead;
            Importance = data.Importance;
            FlagDueDate = data.FlagDueDate;
        }

        public async Task MarkCompleteAsync() => await _parent.MarkEmailCompleteAsync(this);
        public async Task ClearFlagAsync() => await _parent.ClearEmailFlagAsync(this);
        public async Task OpenInOutlookAsync() => await _parent.OpenEmailInOutlookAsync(this);

        private static string GetRelativeDateText(DateTime date)
        {
            var diff = DateTime.Now - date;
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return date.ToString("MMM dd");
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            return name.Length > 0 ? name[0].ToString().ToUpper() : "?";
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Settings view model for Outlook plugin
    /// </summary>
    public class OutlookSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private int _maxEmailsToFetch = 100;

        public override string SettingsTitle => "Outlook Settings";
        public override string SettingsDescription => "Configure Outlook integration behavior";

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxEmailsToFetch
        {
            get => _maxEmailsToFetch;
            set => SetProperty(ref _maxEmailsToFetch, value);
        }

        public OutlookSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxEmailsToFetch = _storage.Get("MaxEmailsToFetch", 100);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxEmailsToFetch", MaxEmailsToFetch);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            RefreshIntervalMinutes = 5;
            MaxEmailsToFetch = 100;
        }
    }
}
