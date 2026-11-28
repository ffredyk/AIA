using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIA.Plugins.SDK;

using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.Jira
{
    [Plugin("AIA.Jira", "Jira Integration", IconSymbol = "Bug20", IsBuiltIn = true)]
    public class JiraPlugin : PluginBase
    {
        private JiraTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Jira";
        public override string Name => "Jira Integration";
        public override string Description => "Shows Jira issues assigned to you and lets you create issues and add comments";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.Network |
            PluginPermissions.UI;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Jira plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new JiraTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "jira",
                Title = "Jira",
                IconSymbol = "Bug20",
                Order = 80,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF0052CC"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-jira",
                Text = "Jira",
                IconSymbol = "Bug20",
                Tooltip = "Open Jira in browser",
                Order = 80,
                OnClick = () => _tabViewModel?.OpenJira()
            });

            LogInfo("Jira plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Jira plugin...");

            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 10), 1, 120);
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(refreshMinutes)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_tabViewModel != null)
                {
                    await _tabViewModel.RefreshAsync();
                }
            };
            _refreshTimer.Start();

            if (_tabViewModel != null)
            {
                await _tabViewModel.RefreshAsync();
            }

            LogInfo("Jira plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Jira plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Jira plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("jira");
            Context.UI.UnregisterToolbarButton("open-jira");
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new JiraSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Jira tab.
    /// </summary>
    public class JiraTabViewModel : PluginTabViewModelBase
    {
        private static readonly BooleanToVisibilityConverter BoolToVisibility = new();

        private readonly IPluginContext _context;
        private readonly JiraService _service;
        private readonly RelayCommand _saveConfigCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _createIssueCommand;
        private readonly RelayCommand _cancelCreateCommand;
        private readonly RelayCommand _addCommentCommand;
        private readonly RelayCommand _openIssueCommand;
        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private bool _isCreatingIssue;
        private string _siteUrlInput = string.Empty;
        private string _emailInput = string.Empty;
        private string _apiTokenInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _selectedIssueKey = string.Empty;
        private JiraIssueViewModel? _selectedIssue;
        private string _newProjectKey = string.Empty;
        private string _newIssueSummary = string.Empty;
        private string _newIssueDescription = string.Empty;
        private string _newIssueType = "Task";
        private string _commentText = string.Empty;
        private int _maxIssues = 50;

        public ObservableCollection<JiraIssueViewModel> Issues { get; } = new();

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool IsConfigured
        {
            get => _isConfigured;
            private set => SetProperty(ref _isConfigured, value);
        }

        public bool IsCreatingIssue
        {
            get => _isCreatingIssue;
            private set
            {
                if (SetProperty(ref _isCreatingIssue, value))
                {
                    OnPropertyChanged(nameof(IsNotCreatingIssue));
                }
            }
        }

        public bool IsNotCreatingIssue => !IsCreatingIssue;

        public string SiteUrlInput
        {
            get => _siteUrlInput;
            set
            {
                if (SetProperty(ref _siteUrlInput, value))
                {
                    _saveConfigCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string EmailInput
        {
            get => _emailInput;
            set
            {
                if (SetProperty(ref _emailInput, value))
                {
                    _saveConfigCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ApiTokenInput
        {
            get => _apiTokenInput;
            set
            {
                if (SetProperty(ref _apiTokenInput, value))
                {
                    _saveConfigCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public JiraIssueViewModel? SelectedIssue
        {
            get => _selectedIssue;
            set
            {
                if (SetProperty(ref _selectedIssue, value))
                {
                    _selectedIssueKey = value?.Key ?? string.Empty;
                    _addCommentCommand.RaiseCanExecuteChanged();
                    _openIssueCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewProjectKey
        {
            get => _newProjectKey;
            set
            {
                if (SetProperty(ref _newProjectKey, value))
                {
                    _createIssueCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewIssueSummary
        {
            get => _newIssueSummary;
            set
            {
                if (SetProperty(ref _newIssueSummary, value))
                {
                    _createIssueCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewIssueDescription
        {
            get => _newIssueDescription;
            set => SetProperty(ref _newIssueDescription, value);
        }

        public string NewIssueType
        {
            get => _newIssueType;
            set => SetProperty(ref _newIssueType, value);
        }

        public string CommentText
        {
            get => _commentText;
            set
            {
                if (SetProperty(ref _commentText, value))
                {
                    _addCommentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SaveConfigCommand => _saveConfigCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand CreateIssueCommand => _createIssueCommand;
        public ICommand CancelCreateCommand => _cancelCreateCommand;
        public ICommand AddCommentCommand => _addCommentCommand;
        public ICommand OpenIssueCommand => _openIssueCommand;
        public ICommand ToggleCreateCommand { get; }
        public ICommand OpenJiraCommand { get; }

        public JiraTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new JiraService(context);

            _saveConfigCommand = new RelayCommand(SaveConfigAsync);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _createIssueCommand = new RelayCommand(
                CreateIssueAsync,
                () => !string.IsNullOrWhiteSpace(NewProjectKey) && !string.IsNullOrWhiteSpace(NewIssueSummary));
            _cancelCreateCommand = new RelayCommand(() => { IsCreatingIssue = false; });
            _addCommentCommand = new RelayCommand(
                AddCommentAsync,
                () => SelectedIssue != null && !string.IsNullOrWhiteSpace(CommentText));
            _openIssueCommand = new RelayCommand(OpenSelectedIssue, () => SelectedIssue != null);
            ToggleCreateCommand = new RelayCommand(ToggleCreate);
            OpenJiraCommand = new RelayCommand(OpenJira);
        }

        public Task InitializeAsync()
        {
            SiteUrlInput = _context.Settings.Get("SiteUrl", string.Empty) ?? string.Empty;
            EmailInput = _context.Settings.Get("Email", string.Empty) ?? string.Empty;
            ApiTokenInput = _context.Settings.Get("ApiToken", string.Empty) ?? string.Empty;
            _maxIssues = Math.Clamp(_context.Settings.Get("MaxIssues", 50), 1, 200);

            _service.Configure(SiteUrlInput, EmailInput, ApiTokenInput);
            IsConfigured = _service.IsConfigured;

            if (!IsConfigured)
            {
                StatusMessage = "Add your Jira site URL, email and API token to get started.";
            }

            return Task.CompletedTask;
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                RunOnUi(() => { StatusMessage = "Add your Jira site URL, email and API token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            RunOnUi(() => { IsLoading = true; StatusMessage = "Connecting to Jira..."; });

            try
            {
                var user = await _service.TestConnectionAsync();
                var issues = await _service.GetMyOpenIssuesAsync(_maxIssues);

                RunOnUi(() =>
                {
                    Issues.Clear();
                    foreach (var issue in issues)
                    {
                        Issues.Add(new JiraIssueViewModel(issue));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected as {user.DisplayName} · {Issues.Count} open issue(s)";
                });

                var target = GetOnUi(() => Issues.FirstOrDefault(i => i.Key == _selectedIssueKey) ?? Issues.FirstOrDefault());
                RunOnUi(() => { SelectedIssue = target; });
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Jira refresh failed: {ex.Message}", ex);
                RunOnUi(() =>
                {
                    StatusMessage = ex.Message;
                    _context.UI.ShowToast(ex.Message, ToastType.Error);
                });
            }
            finally
            {
                RunOnUi(() => { IsLoading = false; });
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task SaveConfigAsync()
        {
            _service.Configure(SiteUrlInput, EmailInput, ApiTokenInput);

            _context.Settings.Set("SiteUrl", _service.BaseUrl);
            _context.Settings.Set("Email", EmailInput?.Trim() ?? string.Empty);
            _context.Settings.Set("ApiToken", ApiTokenInput?.Trim() ?? string.Empty);
            await _context.Settings.SaveAsync();

            RunOnUi(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Configuration saved. Connecting..." : "Configuration cleared.";
            });

            _refreshCommand.RaiseCanExecuteChanged();

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private void ToggleCreate()
        {
            IsCreatingIssue = !IsCreatingIssue;

            if (IsCreatingIssue)
            {
                NewProjectKey = SelectedIssue?.ProjectKey ??
                                (_context.Settings.Get("DefaultProjectKey", string.Empty) ?? string.Empty);
                NewIssueSummary = string.Empty;
                NewIssueDescription = string.Empty;
                NewIssueType = "Task";
            }
        }

        private async Task CreateIssueAsync()
        {
            try
            {
                var created = await _service.CreateIssueAsync(
                    NewProjectKey.Trim(),
                    NewIssueSummary.Trim(),
                    NewIssueDescription?.Trim() ?? string.Empty,
                    NewIssueType?.Trim() ?? "Task");

                _context.Settings.Set("DefaultProjectKey", NewProjectKey.Trim());
                _selectedIssueKey = created.Key;

                RunOnUi(() =>
                {
                    IsCreatingIssue = false;
                    StatusMessage = $"Created {created.Key}";
                });

                _context.UI.ShowToast($"Created Jira issue {created.Key}", ToastType.Success);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Jira create issue failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private async Task AddCommentAsync()
        {
            var issue = SelectedIssue;
            var comment = CommentText?.Trim();

            if (issue == null || string.IsNullOrEmpty(comment))
                return;

            try
            {
                await _service.AddCommentAsync(issue.Key, comment);
                RunOnUi(() =>
                {
                    CommentText = string.Empty;
                    StatusMessage = $"Comment added to {issue.Key}";
                });
                _context.UI.ShowToast($"Comment added to {issue.Key}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Jira add comment failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private void OpenSelectedIssue()
        {
            if (SelectedIssue == null)
                return;

            if (!TryOpenUrl($"{_service.BaseUrl}/browse/{SelectedIssue.Key}"))
            {
                _context.UI.ShowToast("Unable to open Jira.", ToastType.Warning);
            }
        }

        public void OpenJira()
        {
            if (string.IsNullOrEmpty(_service.BaseUrl) || !TryOpenUrl(_service.BaseUrl))
            {
                _context.UI.ShowToast("Configure the Jira site URL first.", ToastType.Warning);
            }
        }

        private static bool TryOpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override DataTemplate GetDataTemplate()
        {
            _cachedTemplate ??= CreateDataTemplate();
            return _cachedTemplate;
        }

        public override void Refresh()
        {
            _ = RefreshAsync();
        }

        public override void OnActivated()
        {
            if (Issues.Count == 0 && _service.IsConfigured)
            {
                _ = RefreshAsync();
            }
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        private static T GetOnUi<T>(Func<T> func)
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            return dispatcher != null && !dispatcher.CheckAccess()
                ? dispatcher.Invoke(func)
                : func();
        }

        #region Data template

        private DataTemplate CreateDataTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(12));

            root.AppendChild(CreateRow(GridLength.Auto));
            root.AppendChild(CreateRow(GridLength.Auto));
            root.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));

            var header = CreateHeader();
            header.SetValue(Grid.RowProperty, 0);
            root.AppendChild(header);

            var config = CreateConfigPanel();
            config.SetValue(Grid.RowProperty, 1);
            root.AppendChild(config);

            var content = CreateContentPanel();
            content.SetValue(Grid.RowProperty, 2);
            root.AppendChild(content);

            return new DataTemplate { VisualTree = root };
        }

        private FrameworkElementFactory CreateHeader()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));

            grid.AppendChild(CreateColumn(GridLength.Auto));
            grid.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(CreateColumn(GridLength.Auto));
            grid.AppendChild(CreateColumn(GridLength.Auto));
            grid.AppendChild(CreateColumn(GridLength.Auto));

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 1);
            titlePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            titlePanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            titlePanel.AppendChild(CreateText("Jira", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(CreateText(bindingPath: "StatusMessage", fontSize: 11, foreground: BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(titlePanel);

            var refresh = CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var newIssue = CreateButton("New issue", "ToggleCreateCommand", NeutralBrush);
            newIssue.SetValue(Grid.ColumnProperty, 3);
            newIssue.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(newIssue);

            var open = CreateButton("Open Jira", "OpenJiraCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 4);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var panel = new FrameworkElementFactory(typeof(Grid));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            panel.AppendChild(CreateColumn(GridLength.Auto));
            panel.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            panel.AppendChild(CreateRow(GridLength.Auto));
            panel.AppendChild(CreateRow(GridLength.Auto));
            panel.AppendChild(CreateRow(GridLength.Auto));
            panel.AppendChild(CreateRow(GridLength.Auto));

            panel.AppendChild(CreateFieldLabel("Site URL", 0));
            panel.AppendChild(CreateConfigTextBox("SiteUrlInput", 0, "https://your-domain.atlassian.net"));

            panel.AppendChild(CreateFieldLabel("Email", 1));
            panel.AppendChild(CreateConfigTextBox("EmailInput", 1, "you@company.com"));

            panel.AppendChild(CreateFieldLabel("API token", 2));
            panel.AppendChild(CreateConfigTextBox("ApiTokenInput", 2,
                "Create an API token at https://id.atlassian.com/manage-profile/security/api-tokens"));

            var actionPanel = new FrameworkElementFactory(typeof(StackPanel));
            actionPanel.SetValue(Grid.ColumnProperty, 1);
            actionPanel.SetValue(Grid.RowProperty, 3);
            actionPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            actionPanel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            actionPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));

            var hint = CreateText(
                "Jira Cloud uses Basic auth with email + API token.",
                fontSize: 10,
                foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF));
            hint.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hint.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            actionPanel.AppendChild(hint);

            var save = CreateButton("Save & Connect", "SaveConfigCommand", AccentBrush);
            actionPanel.AppendChild(save);
            panel.AppendChild(actionPanel);

            return panel;
        }

        private static FrameworkElementFactory CreateFieldLabel(string text, int row)
        {
            var label = CreateText(text, fontSize: 11, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(Grid.RowProperty, row);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 3));
            return label;
        }

        private static FrameworkElementFactory CreateConfigTextBox(string bindingPath, int row, string tooltip)
        {
            var box = CreateInput(bindingPath, tooltip);
            box.SetValue(Grid.ColumnProperty, 1);
            box.SetValue(Grid.RowProperty, row);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 3));
            return box;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(CreateColumn(new GridLength(360)));
            grid.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            var issuesPanel = new FrameworkElementFactory(typeof(Grid));
            issuesPanel.SetValue(Grid.ColumnProperty, 0);
            issuesPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            issuesPanel.AppendChild(CreateRow(GridLength.Auto));
            issuesPanel.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));

            var issuesHeader = CreateSectionHeader("MY OPEN ISSUES");
            issuesHeader.SetValue(Grid.RowProperty, 0);
            issuesPanel.AppendChild(issuesHeader);

            var list = new FrameworkElementFactory(typeof(ListBox));
            list.SetValue(Grid.RowProperty, 1);
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Issues"));
            list.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedIssue") { Mode = BindingMode.TwoWay });
            list.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            list.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            list.SetValue(ItemsControl.ItemTemplateProperty, CreateIssueItemTemplate());
            issuesPanel.AppendChild(list);

            grid.AppendChild(issuesPanel);

            var rightPanel = new FrameworkElementFactory(typeof(Grid));
            rightPanel.SetValue(Grid.ColumnProperty, 1);
            rightPanel.AppendChild(CreateDetailsPanel());
            rightPanel.AppendChild(CreateCreateForm());
            grid.AppendChild(rightPanel);

            return grid;
        }

        private FrameworkElementFactory CreateDetailsPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetBinding(UIElement.VisibilityProperty, new Binding("IsNotCreatingIssue")
            {
                Converter = BoolToVisibility
            });

            grid.AppendChild(CreateRow(GridLength.Auto));
            grid.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(CreateRow(GridLength.Auto));

            var header = new FrameworkElementFactory(typeof(Grid));
            header.SetValue(Grid.RowProperty, 0);
            header.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            header.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            header.AppendChild(CreateColumn(GridLength.Auto));

            var headerInfo = new FrameworkElementFactory(typeof(StackPanel));
            headerInfo.SetValue(Grid.ColumnProperty, 0);
            headerInfo.AppendChild(CreateText(bindingPath: "SelectedIssue.Key", fontSize: 14, fontWeight: FontWeights.SemiBold));
            headerInfo.AppendChild(CreateText(bindingPath: "SelectedIssue.MetaLine", fontSize: 11, foreground: BrushFrom(0x99, 0xFF, 0xFF, 0xFF), trim: true));
            header.AppendChild(headerInfo);

            var openButton = CreateButton("Open in Jira", "OpenIssueCommand", NeutralBrush);
            openButton.SetValue(Grid.ColumnProperty, 1);
            openButton.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            header.AppendChild(openButton);

            grid.AppendChild(header);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(Grid.RowProperty, 1);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            var details = new FrameworkElementFactory(typeof(StackPanel));

            details.AppendChild(CreateText(bindingPath: "SelectedIssue.Summary", fontSize: 15, fontWeight: FontWeights.SemiBold, wrap: true));

            var description = CreateText(bindingPath: "SelectedIssue.Description", fontSize: 12, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF), wrap: true);
            description.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            details.AppendChild(description);

            details.AppendChild(CreateText(bindingPath: "SelectedIssue.ExtraInfo", fontSize: 11, foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF), wrap: true, margin: new Thickness(0, 8, 0, 0)));

            scroll.AppendChild(details);
            grid.AppendChild(scroll);

            var commentRow = new FrameworkElementFactory(typeof(Grid));
            commentRow.SetValue(Grid.RowProperty, 2);
            commentRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            commentRow.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            commentRow.AppendChild(CreateColumn(GridLength.Auto));

            var commentBox = CreateInput("CommentText", "Add a comment to the selected issue");
            commentBox.SetValue(Grid.ColumnProperty, 0);
            commentRow.AppendChild(commentBox);

            var commentButton = CreateButton("Add comment", "AddCommentCommand", AccentBrush);
            commentButton.SetValue(Grid.ColumnProperty, 1);
            commentButton.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            commentRow.AppendChild(commentButton);

            grid.AppendChild(commentRow);
            return grid;
        }

        private FrameworkElementFactory CreateCreateForm()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetBinding(UIElement.VisibilityProperty, new Binding("IsCreatingIssue")
            {
                Converter = BoolToVisibility
            });

            grid.AppendChild(CreateRow(GridLength.Auto));
            grid.AppendChild(CreateRow(GridLength.Auto));
            grid.AppendChild(CreateRow(GridLength.Auto));
            grid.AppendChild(CreateRow(GridLength.Auto));
            grid.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));
            grid.AppendChild(CreateRow(GridLength.Auto));

            var title = CreateText("NEW ISSUE", fontSize: 12, fontWeight: FontWeights.SemiBold, foreground: BrushFrom(0xBB, 0xFF, 0xFF, 0xFF));
            title.SetValue(Grid.RowProperty, 0);
            title.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            grid.AppendChild(title);

            var projectRow = new FrameworkElementFactory(typeof(Grid));
            projectRow.SetValue(Grid.RowProperty, 1);
            projectRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            projectRow.AppendChild(CreateColumn(new GridLength(110)));
            projectRow.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            var projectLabel = CreateText("Project key", fontSize: 11, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            projectLabel.SetValue(Grid.ColumnProperty, 0);
            projectLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            projectRow.AppendChild(projectLabel);

            var projectBox = CreateInput("NewProjectKey", "Project key, for example ABC");
            projectBox.SetValue(Grid.ColumnProperty, 1);
            projectRow.AppendChild(projectBox);
            grid.AppendChild(projectRow);

            var summaryRow = new FrameworkElementFactory(typeof(Grid));
            summaryRow.SetValue(Grid.RowProperty, 2);
            summaryRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            summaryRow.AppendChild(CreateColumn(new GridLength(110)));
            summaryRow.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            var summaryLabel = CreateText("Summary", fontSize: 11, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            summaryLabel.SetValue(Grid.ColumnProperty, 0);
            summaryLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            summaryRow.AppendChild(summaryLabel);

            var summaryBox = CreateInput("NewIssueSummary", "Short issue summary");
            summaryBox.SetValue(Grid.ColumnProperty, 1);
            summaryRow.AppendChild(summaryBox);
            grid.AppendChild(summaryRow);

            var typeRow = new FrameworkElementFactory(typeof(Grid));
            typeRow.SetValue(Grid.RowProperty, 3);
            typeRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            typeRow.AppendChild(CreateColumn(new GridLength(110)));
            typeRow.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            var typeLabel = CreateText("Issue type", fontSize: 11, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            typeLabel.SetValue(Grid.ColumnProperty, 0);
            typeLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            typeRow.AppendChild(typeLabel);

            var typeBox = CreateInput("NewIssueType", "Task, Bug, Story, ...");
            typeBox.SetValue(Grid.ColumnProperty, 1);
            typeRow.AppendChild(typeBox);
            grid.AppendChild(typeRow);

            var description = CreateInput("NewIssueDescription", "Description");
            description.SetValue(Grid.RowProperty, 4);
            description.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
            description.SetValue(TextBox.AcceptsReturnProperty, true);
            description.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            description.SetValue(FrameworkElement.MinHeightProperty, 100.0);
            description.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            grid.AppendChild(description);

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(Grid.RowProperty, 5);
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);

            var cancel = CreateButton("Cancel", "CancelCreateCommand", NeutralBrush);
            cancel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            buttons.AppendChild(cancel);

            buttons.AppendChild(CreateButton("Create issue", "CreateIssueCommand", AccentBrush));
            grid.AppendChild(buttons);

            return grid;
        }

        private static DataTemplate CreateIssueItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, BrushFrom(0x12, 0xFF, 0xFF, 0xFF));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(CreateText(bindingPath: "KeyLine", fontSize: 10, foreground: BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            panel.AppendChild(CreateText(bindingPath: "Summary", fontSize: 12, fontWeight: FontWeights.SemiBold, trim: true));
            panel.AppendChild(CreateText(bindingPath: "MetaLine", fontSize: 10, foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF), trim: true));

            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        #endregion

        #region Template helpers

        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x52, 0xCC));
        private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

        private static Brush BrushFrom(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Binding CreateBinding(string path)
        {
            return new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
        }

        private static FrameworkElementFactory CreateRow(GridLength height)
        {
            var row = new FrameworkElementFactory(typeof(RowDefinition));
            row.SetValue(RowDefinition.HeightProperty, height);
            return row;
        }

        private static FrameworkElementFactory CreateColumn(GridLength width)
        {
            var column = new FrameworkElementFactory(typeof(ColumnDefinition));
            column.SetValue(ColumnDefinition.WidthProperty, width);
            return column;
        }

        private static FrameworkElementFactory CreateSectionHeader(string text)
        {
            var header = new FrameworkElementFactory(typeof(TextBlock));
            header.SetValue(TextBlock.TextProperty, text);
            header.SetValue(TextBlock.ForegroundProperty, BrushFrom(0xBB, 0xFF, 0xFF, 0xFF));
            header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            header.SetValue(TextBlock.FontSizeProperty, 10.0);
            header.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 6));
            return header;
        }

        private static FrameworkElementFactory CreateInput(string bindingPath, string tooltip)
        {
            var box = new FrameworkElementFactory(typeof(TextBox));
            box.SetBinding(TextBox.TextProperty, CreateBinding(bindingPath));
            box.SetValue(TextBox.BackgroundProperty, BrushFrom(0x22, 0xFF, 0xFF, 0xFF));
            box.SetValue(TextBox.ForegroundProperty, Brushes.White);
            box.SetValue(TextBox.BorderBrushProperty, BrushFrom(0x33, 0xFF, 0xFF, 0xFF));
            box.SetValue(TextBox.CaretBrushProperty, Brushes.White);
            box.SetValue(TextBox.PaddingProperty, new Thickness(6, 4, 6, 4));
            box.SetValue(TextBox.FontSizeProperty, 12.0);
            box.SetValue(FrameworkElement.ToolTipProperty, tooltip);
            return box;
        }

        private static FrameworkElementFactory CreateText(
            string? bindingPath = null,
            string? text = null,
            double fontSize = 12,
            Brush? foreground = null,
            FontWeight? fontWeight = null,
            bool wrap = false,
            bool trim = false,
            Thickness? margin = null)
        {
            var block = new FrameworkElementFactory(typeof(TextBlock));

            if (bindingPath != null)
            {
                block.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            }
            else
            {
                block.SetValue(TextBlock.TextProperty, text ?? string.Empty);
            }

            block.SetValue(TextBlock.FontSizeProperty, fontSize);
            block.SetValue(TextBlock.ForegroundProperty, foreground ?? Brushes.White);

            if (fontWeight.HasValue)
                block.SetValue(TextBlock.FontWeightProperty, fontWeight.Value);
            if (wrap)
                block.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            if (trim)
                block.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (margin.HasValue)
                block.SetValue(FrameworkElement.MarginProperty, margin.Value);

            return block;
        }

        private static FrameworkElementFactory CreateButton(string text, string commandPath, Brush background)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(ContentControl.ContentProperty, text);
            button.SetBinding(ButtonBase.CommandProperty, new Binding(commandPath));
            button.SetValue(Control.BackgroundProperty, background);
            button.SetValue(Control.ForegroundProperty, Brushes.White);
            button.SetValue(Control.PaddingProperty, new Thickness(12, 5, 12, 5));
            button.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            button.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            return button;
        }

        #endregion
    }

    /// <summary>
    /// View model for a Jira issue.
    /// </summary>
    public class JiraIssueViewModel
    {
        public string Key { get; }
        public string Summary { get; }
        public string Status { get; }
        public string Priority { get; }
        public string IssueType { get; }
        public string ProjectKey { get; }
        public string ProjectName { get; }
        public string Assignee { get; }
        public string Description { get; }
        public DateTime? Updated { get; }
        public DateTime? DueDate { get; }

        public string KeyLine => string.IsNullOrEmpty(IssueType) ? Key : $"{Key} · {IssueType}";
        public string MetaLine => BuildMetaLine();

        public string ExtraInfo
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrEmpty(ProjectName))
                    parts.Add($"Project: {ProjectName}");
                if (!string.IsNullOrEmpty(Assignee))
                    parts.Add($"Assignee: {Assignee}");
                if (DueDate.HasValue)
                    parts.Add($"Due: {DueDate.Value:yyyy-MM-dd}");

                return string.Join("   ", parts);
            }
        }

        public JiraIssueViewModel(JiraIssueData data)
        {
            Key = data.Key;
            Summary = data.Summary;
            Status = data.Status;
            Priority = data.Priority;
            IssueType = data.IssueType;
            ProjectKey = data.ProjectKey;
            ProjectName = data.ProjectName;
            Assignee = data.Assignee;
            Description = data.Description;
            Updated = data.Updated;
            DueDate = data.DueDate;
        }

        private string BuildMetaLine()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(Status))
                parts.Add(Status);
            if (!string.IsNullOrEmpty(Priority))
                parts.Add(Priority);
            if (Updated.HasValue)
                parts.Add(Updated.Value.ToString("MMM dd HH:mm"));

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Settings view model for the Jira plugin.
    /// </summary>
    public class JiraSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 10;
        private int _maxIssues = 50;
        private string _siteUrl = string.Empty;
        private string _email = string.Empty;
        private string _apiToken = string.Empty;

        public override string SettingsTitle => "Jira Settings";
        public override string SettingsDescription => "Configure the Jira Cloud connection";

        public string SiteUrl
        {
            get => _siteUrl;
            set => SetProperty(ref _siteUrl, value);
        }

        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        public string ApiToken
        {
            get => _apiToken;
            set => SetProperty(ref _apiToken, value);
        }

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxIssues
        {
            get => _maxIssues;
            set => SetProperty(ref _maxIssues, value);
        }

        public JiraSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            SiteUrl = _storage.Get("SiteUrl", string.Empty) ?? string.Empty;
            Email = _storage.Get("Email", string.Empty) ?? string.Empty;
            ApiToken = _storage.Get("ApiToken", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 10);
            MaxIssues = _storage.Get("MaxIssues", 50);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("SiteUrl", JiraService.NormalizeBaseUrl(SiteUrl));
            _storage.Set("Email", Email);
            _storage.Set("ApiToken", ApiToken);
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxIssues", MaxIssues);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            SiteUrl = string.Empty;
            Email = string.Empty;
            ApiToken = string.Empty;
            RefreshIntervalMinutes = 10;
            MaxIssues = 50;
        }
    }
}
