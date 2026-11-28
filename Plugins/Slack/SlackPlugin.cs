using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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

namespace AIA.Plugins.Slack
{
    [Plugin("AIA.Slack", "Slack Integration", IconSymbol = "Comment20", IsBuiltIn = true)]
    public class SlackPlugin : PluginBase
    {
        private SlackTabViewModel? _tabViewModel;
        private DispatcherTimer? _refreshTimer;

        public override string Id => "AIA.Slack";
        public override string Name => "Slack Integration";
        public override string Description => "Connects to Slack with a bot token to browse channels, read messages and send replies";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "AIA";

        public override PluginPermissions RequiredPermissions =>
            PluginPermissions.Network |
            PluginPermissions.UI;

        protected override async Task OnInitializeAsync()
        {
            LogInfo("Initializing Slack plugin...");
            await Context.Settings.LoadAsync();

            _tabViewModel = new SlackTabViewModel(Context);
            await _tabViewModel.InitializeAsync();

            Context.UI.RegisterTab(new PluginTabDefinition
            {
                TabId = "slack",
                Title = "Slack",
                IconSymbol = "Comment20",
                Order = 70,
                ViewModel = _tabViewModel,
                BadgeColor = "#FF4A154B"
            });

            Context.UI.RegisterToolbarButton(new PluginToolbarButton
            {
                ButtonId = "open-slack",
                Text = "Slack",
                IconSymbol = "Comment20",
                Tooltip = "Open Slack",
                Order = 70,
                OnClick = () => SlackTabViewModel.OpenSlackApp()
            });

            LogInfo("Slack plugin initialized");
        }

        protected override async Task OnStartAsync()
        {
            LogInfo("Starting Slack plugin...");

            var refreshMinutes = Math.Clamp(Context.Settings.Get("RefreshIntervalMinutes", 5), 1, 120);
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

            LogInfo("Slack plugin started");
        }

        protected override Task OnStopAsync()
        {
            LogInfo("Stopping Slack plugin...");
            _refreshTimer?.Stop();
            _refreshTimer = null;
            LogInfo("Slack plugin stopped");
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Context.UI.UnregisterTab("slack");
            Context.UI.UnregisterToolbarButton("open-slack");
            _tabViewModel?.Dispose();
        }

        public override IPluginSettingsViewModel? GetSettingsViewModel()
        {
            return new SlackSettingsViewModel(Context.Settings);
        }
    }

    /// <summary>
    /// View model for the Slack tab.
    /// </summary>
    public class SlackTabViewModel : PluginTabViewModelBase, IDisposable
    {
        private readonly IPluginContext _context;
        private readonly SlackService _service;
        private readonly RelayCommand _saveTokenCommand;
        private readonly RelayCommand _refreshCommand;
        private readonly RelayCommand _sendCommand;
        private readonly RelayCommand _openChannelCommand;
        private readonly Dictionary<string, string> _userNames = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _messageLoadCts;
        private DataTemplate? _cachedTemplate;
        private bool _isLoading;
        private bool _isConfigured;
        private string _tokenInput = string.Empty;
        private string _statusMessage = string.Empty;
        private string _messageText = string.Empty;
        private string _teamId = string.Empty;
        private SlackChannelViewModel? _selectedChannel;
        private int _maxMessages = 30;

        public ObservableCollection<SlackChannelViewModel> Channels { get; } = new();

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

        public string TokenInput
        {
            get => _tokenInput;
            set
            {
                if (SetProperty(ref _tokenInput, value))
                {
                    _saveTokenCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string MessageText
        {
            get => _messageText;
            set
            {
                if (SetProperty(ref _messageText, value))
                {
                    _sendCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public SlackChannelViewModel? SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    _sendCommand.RaiseCanExecuteChanged();
                    _openChannelCommand.RaiseCanExecuteChanged();
                    if (value != null)
                    {
                        _ = RememberChannelAsync(value.Id);
                        _ = LoadMessagesAsync(value);
                    }
                }
            }
        }

        public ICommand SaveTokenCommand => _saveTokenCommand;
        public ICommand RefreshCommand => _refreshCommand;
        public ICommand SendCommand => _sendCommand;
        public ICommand OpenChannelCommand => _openChannelCommand;
        public ICommand OpenSlackCommand { get; }

        public SlackTabViewModel(IPluginContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _service = new SlackService(context);

            _saveTokenCommand = new RelayCommand(SaveTokenAsync);
            _refreshCommand = new RelayCommand(RefreshAsync, () => !IsLoading && _service.IsConfigured);
            _sendCommand = new RelayCommand(SendMessageAsync, () => SelectedChannel != null && !string.IsNullOrWhiteSpace(MessageText));
            _openChannelCommand = new RelayCommand(() => OpenSelectedChannel(), () => SelectedChannel != null);
            OpenSlackCommand = new RelayCommand(() => OpenSlackApp());
        }

        public Task InitializeAsync()
        {
            TokenInput = _context.Settings.Get("BotToken", string.Empty) ?? string.Empty;
            _maxMessages = Math.Clamp(_context.Settings.Get("MaxMessages", 30), 1, 100);
            _service.Configure(TokenInput);
            IsConfigured = _service.IsConfigured;

            if (!IsConfigured)
            {
                StatusMessage = "Add a Slack bot token to get started.";
            }

            return Task.CompletedTask;
        }

        public async Task RefreshAsync()
        {
            if (!_service.IsConfigured)
            {
                RunOnUi(() => { StatusMessage = "Add a Slack bot token to get started."; });
                return;
            }

            if (IsLoading)
                return;

            RunOnUi(() => { IsLoading = true; StatusMessage = "Connecting to Slack..."; });

            try
            {
                var user = await _service.TestConnectionAsync();
                _teamId = user.TeamId;

                try
                {
                    var names = await _service.GetUserNamesAsync();
                    foreach (var pair in names)
                    {
                        _userNames[pair.Key] = pair.Value;
                    }
                }
                catch (Exception ex)
                {
                    _context.Logger.Warning($"Slack: unable to read user directory: {ex.Message}");
                }

                var channels = await _service.GetChannelsAsync();
                var lastChannelId = _context.Settings.Get("LastChannelId", string.Empty) ?? string.Empty;

                RunOnUi(() =>
                {
                    Channels.Clear();
                    foreach (var channel in channels)
                    {
                        Channels.Add(new SlackChannelViewModel(channel));
                    }

                    IsConfigured = true;
                    StatusMessage = $"Connected to {user.TeamName} as {user.UserName} · {Channels.Count} channel(s)";
                });

                var target = GetOnUi(() => Channels.FirstOrDefault(c => c.Id == lastChannelId) ?? Channels.FirstOrDefault());

                if (target != null)
                {
                    RunOnUi(() => { SelectedChannel = target; });
                    await LoadMessagesAsync(target);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Slack refresh failed: {ex.Message}", ex);
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

        private async Task RememberChannelAsync(string channelId)
        {
            try
            {
                _context.Settings.Set("LastChannelId", channelId);
                await _context.Settings.SaveAsync();
            }
            catch (Exception ex)
            {
                _context.Logger.Warning($"Slack: unable to save the selected channel: {ex.Message}");
            }
        }

        private async Task LoadMessagesAsync(SlackChannelViewModel channel)
        {
            _messageLoadCts?.Cancel();
            _messageLoadCts?.Dispose();
            var cts = new CancellationTokenSource();
            _messageLoadCts = cts;

            try
            {
                var messages = await _service.GetMessagesAsync(channel.Id, _maxMessages, _userNames, cts.Token);
                if (cts.IsCancellationRequested)
                    return;

                RunOnUi(() =>
                {
                    channel.Messages.Clear();
                    foreach (var message in messages)
                    {
                        channel.Messages.Add(new SlackMessageViewModel(message));
                    }

                    if (SelectedChannel == channel)
                    {
                        StatusMessage = $"{channel.DisplayName} · {channel.Messages.Count} recent message(s)";
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // A different channel was selected.
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                {
                    _context.Logger.Warning($"Slack: unable to load messages for '{channel.Name}': {ex.Message}");
                    RunOnUi(() =>
                    {
                        if (SelectedChannel == channel)
                        {
                            StatusMessage = ex.Message;
                        }
                    });
                }
            }
        }

        private async Task SaveTokenAsync()
        {
            var token = TokenInput?.Trim() ?? string.Empty;

            _context.Settings.Set("BotToken", token);
            await _context.Settings.SaveAsync();
            _service.Configure(token);

            RunOnUi(() =>
            {
                IsConfigured = _service.IsConfigured;
                StatusMessage = IsConfigured ? "Token saved. Connecting..." : "Token cleared.";
            });

            _refreshCommand.RaiseCanExecuteChanged();

            if (IsConfigured)
            {
                await RefreshAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            var channel = SelectedChannel;
            var text = MessageText?.Trim();

            if (channel == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                var sent = await _service.SendMessageAsync(channel.Id, text, _userNames);
                RunOnUi(() =>
                {
                    channel.Messages.Add(new SlackMessageViewModel(sent));
                    MessageText = string.Empty;
                    StatusMessage = $"Message sent to {channel.DisplayName}";
                });
                _context.UI.ShowToast($"Message sent to {channel.DisplayName}", ToastType.Success);
            }
            catch (Exception ex)
            {
                _context.Logger.Error($"Slack send failed: {ex.Message}", ex);
                RunOnUi(() => { StatusMessage = ex.Message; });
                _context.UI.ShowToast(ex.Message, ToastType.Error);
            }
        }

        private void OpenSelectedChannel()
        {
            if (SelectedChannel == null)
                return;

            var openUrl = string.IsNullOrEmpty(_teamId)
                ? $"https://app.slack.com/client/{SelectedChannel.Id}"
                : $"https://app.slack.com/client/{_teamId}/{SelectedChannel.Id}";

            if (TryOpenUrl(openUrl))
                return;

            _context.UI.ShowToast("Unable to open Slack.", ToastType.Warning);
        }

        public static bool OpenSlackApp()
        {
            return TryOpenUrl("slack://open") || TryOpenUrl("https://app.slack.com/client");
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
            if (Channels.Count == 0 && _service.IsConfigured)
            {
                _ = RefreshAsync();
            }
        }

        public void Dispose()
        {
            _messageLoadCts?.Cancel();
            _messageLoadCts?.Dispose();
            _messageLoadCts = null;
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

            var titlePanel = new FrameworkElementFactory(typeof(StackPanel));
            titlePanel.SetValue(Grid.ColumnProperty, 1);
            titlePanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            titlePanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
            titlePanel.AppendChild(CreateText("Slack", fontSize: 16, fontWeight: FontWeights.SemiBold));
            titlePanel.AppendChild(CreateText(bindingPath: "StatusMessage", fontSize: 11, foreground: BrushFrom(0x99, 0xFF, 0xFF, 0xFF)));
            grid.AppendChild(titlePanel);

            var refresh = CreateButton("Refresh", "RefreshCommand", AccentBrush);
            refresh.SetValue(Grid.ColumnProperty, 2);
            refresh.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            grid.AppendChild(refresh);

            var open = CreateButton("Open Slack", "OpenSlackCommand", NeutralBrush);
            open.SetValue(Grid.ColumnProperty, 3);
            grid.AppendChild(open);

            return grid;
        }

        private FrameworkElementFactory CreateConfigPanel()
        {
            var panel = new FrameworkElementFactory(typeof(Grid));
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            panel.AppendChild(CreateColumn(GridLength.Auto));
            panel.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            panel.AppendChild(CreateColumn(GridLength.Auto));

            var label = CreateText("Bot token", fontSize: 12, foreground: BrushFrom(0xCC, 0xFF, 0xFF, 0xFF));
            label.SetValue(Grid.ColumnProperty, 0);
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            panel.AppendChild(label);

            var tokenBox = new FrameworkElementFactory(typeof(TextBox));
            tokenBox.SetValue(Grid.ColumnProperty, 1);
            tokenBox.SetBinding(TextBox.TextProperty, CreateBinding("TokenInput"));
            tokenBox.SetValue(TextBox.BackgroundProperty, BrushFrom(0x22, 0xFF, 0xFF, 0xFF));
            tokenBox.SetValue(TextBox.ForegroundProperty, Brushes.White);
            tokenBox.SetValue(TextBox.BorderBrushProperty, BrushFrom(0x33, 0xFF, 0xFF, 0xFF));
            tokenBox.SetValue(TextBox.CaretBrushProperty, Brushes.White);
            tokenBox.SetValue(TextBox.PaddingProperty, new Thickness(6, 4, 6, 4));
            tokenBox.SetValue(TextBox.FontSizeProperty, 12.0);
            tokenBox.SetValue(TextBox.ToolTipProperty,
                "Bot token (xoxb-...) from https://api.slack.com/apps. Needed scopes: channels:read, channels:history, chat:write, users:read.");
            panel.AppendChild(tokenBox);

            var save = CreateButton("Save & Connect", "SaveTokenCommand", AccentBrush);
            save.SetValue(Grid.ColumnProperty, 2);
            save.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            panel.AppendChild(save);

            return panel;
        }

        private FrameworkElementFactory CreateContentPanel()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.AppendChild(CreateColumn(new GridLength(290)));
            grid.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));

            var channelsPanel = new FrameworkElementFactory(typeof(Grid));
            channelsPanel.SetValue(Grid.ColumnProperty, 0);
            channelsPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            channelsPanel.AppendChild(CreateRow(GridLength.Auto));
            channelsPanel.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));

            var channelsHeader = CreateSectionHeader("CHANNELS");
            channelsHeader.SetValue(Grid.RowProperty, 0);
            channelsPanel.AppendChild(channelsHeader);

            var list = new FrameworkElementFactory(typeof(ListBox));
            list.SetValue(Grid.RowProperty, 1);
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Channels"));
            list.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedChannel") { Mode = BindingMode.TwoWay });
            list.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            list.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            list.SetValue(ItemsControl.ItemTemplateProperty, CreateChannelItemTemplate());
            channelsPanel.AppendChild(list);

            grid.AppendChild(channelsPanel);

            var messagesPanel = new FrameworkElementFactory(typeof(Grid));
            messagesPanel.SetValue(Grid.ColumnProperty, 1);
            messagesPanel.AppendChild(CreateRow(GridLength.Auto));
            messagesPanel.AppendChild(CreateRow(new GridLength(1, GridUnitType.Star)));
            messagesPanel.AppendChild(CreateRow(GridLength.Auto));

            var selectedHeader = new FrameworkElementFactory(typeof(Grid));
            selectedHeader.SetValue(Grid.RowProperty, 0);
            selectedHeader.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            selectedHeader.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            selectedHeader.AppendChild(CreateColumn(GridLength.Auto));

            var selectedInfo = new FrameworkElementFactory(typeof(StackPanel));
            selectedInfo.SetValue(Grid.ColumnProperty, 0);
            selectedInfo.AppendChild(CreateText(bindingPath: "SelectedChannel.DisplayName", fontSize: 14, fontWeight: FontWeights.SemiBold));
            selectedInfo.AppendChild(CreateText(bindingPath: "SelectedChannel.Subtitle", fontSize: 11, foreground: BrushFrom(0x99, 0xFF, 0xFF, 0xFF), trim: true));
            selectedHeader.AppendChild(selectedInfo);

            var openChannel = CreateButton("Open in Slack", "OpenChannelCommand", NeutralBrush);
            openChannel.SetValue(Grid.ColumnProperty, 1);
            openChannel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            selectedHeader.AppendChild(openChannel);

            messagesPanel.AppendChild(selectedHeader);

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(Grid.RowProperty, 1);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            var messagesItems = new FrameworkElementFactory(typeof(ItemsControl));
            messagesItems.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SelectedChannel.Messages"));
            messagesItems.SetValue(ItemsControl.ItemTemplateProperty, CreateMessageItemTemplate());
            scroll.AppendChild(messagesItems);
            messagesPanel.AppendChild(scroll);

            var composer = new FrameworkElementFactory(typeof(Grid));
            composer.SetValue(Grid.RowProperty, 2);
            composer.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            composer.AppendChild(CreateColumn(new GridLength(1, GridUnitType.Star)));
            composer.AppendChild(CreateColumn(GridLength.Auto));

            var messageBox = new FrameworkElementFactory(typeof(TextBox));
            messageBox.SetValue(Grid.ColumnProperty, 0);
            messageBox.SetBinding(TextBox.TextProperty, CreateBinding("MessageText"));
            messageBox.SetValue(TextBox.BackgroundProperty, BrushFrom(0x22, 0xFF, 0xFF, 0xFF));
            messageBox.SetValue(TextBox.ForegroundProperty, Brushes.White);
            messageBox.SetValue(TextBox.BorderBrushProperty, BrushFrom(0x33, 0xFF, 0xFF, 0xFF));
            messageBox.SetValue(TextBox.CaretBrushProperty, Brushes.White);
            messageBox.SetValue(TextBox.PaddingProperty, new Thickness(8, 6, 8, 6));
            messageBox.SetValue(TextBox.FontSizeProperty, 12.0);
            messageBox.SetValue(TextBox.MinHeightProperty, 32.0);
            messageBox.SetValue(TextBox.ToolTipProperty, "Type a message and press Send.");
            composer.AppendChild(messageBox);

            var send = CreateButton("Send", "SendCommand", AccentBrush);
            send.SetValue(Grid.ColumnProperty, 1);
            send.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            composer.AppendChild(send);

            messagesPanel.AppendChild(composer);
            grid.AppendChild(messagesPanel);

            return grid;
        }

        private static DataTemplate CreateChannelItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, BrushFrom(0x12, 0xFF, 0xFF, 0xFF));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(CreateText(bindingPath: "DisplayName", fontSize: 12, fontWeight: FontWeights.SemiBold));
            panel.AppendChild(CreateText(bindingPath: "Subtitle", fontSize: 10, foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF), trim: true));

            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        private static DataTemplate CreateMessageItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, BrushFrom(0x14, 0xFF, 0xFF, 0xFF));

            var panel = new FrameworkElementFactory(typeof(StackPanel));

            var header = new FrameworkElementFactory(typeof(StackPanel));
            header.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            header.AppendChild(CreateText(bindingPath: "AuthorName", fontSize: 12, fontWeight: FontWeights.SemiBold));

            var timestamp = CreateText(bindingPath: "TimestampText", fontSize: 10, foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF));
            timestamp.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            header.AppendChild(timestamp);

            panel.AppendChild(header);

            var content = CreateText(bindingPath: "Text", fontSize: 12, foreground: BrushFrom(0xDD, 0xFF, 0xFF, 0xFF), wrap: true);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            panel.AppendChild(content);

            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }

        #endregion

        #region Template helpers

        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x15, 0x4B));
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

        private static FrameworkElementFactory CreateText(
            string? bindingPath = null,
            string? text = null,
            double fontSize = 12,
            Brush? foreground = null,
            FontWeight? fontWeight = null,
            bool wrap = false,
            bool trim = false)
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
    /// View model for a Slack channel.
    /// </summary>
    public class SlackChannelViewModel
    {
        public string Id { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public string Subtitle { get; }
        public ObservableCollection<SlackMessageViewModel> Messages { get; } = new();

        public SlackChannelViewModel(SlackChannelData data)
        {
            Id = data.Id;
            Name = data.Name;
            DisplayName = data.DisplayName;
            Subtitle = data.Subtitle;
        }
    }

    /// <summary>
    /// View model for a Slack message.
    /// </summary>
    public class SlackMessageViewModel
    {
        public string AuthorName { get; }
        public string Text { get; }
        public string TimestampText { get; }

        public SlackMessageViewModel(SlackMessageData data)
        {
            AuthorName = data.AuthorName;
            Text = data.Text;
            TimestampText = data.Timestamp.ToString("MMM dd HH:mm");
        }
    }

    /// <summary>
    /// Settings view model for the Slack plugin.
    /// </summary>
    public class SlackSettingsViewModel : PluginSettingsViewModelBase
    {
        private readonly IPluginSettingsStorage _storage;
        private int _refreshIntervalMinutes = 5;
        private int _maxMessages = 30;
        private string _botToken = string.Empty;

        public override string SettingsTitle => "Slack Settings";
        public override string SettingsDescription => "Configure the Slack bot connection";

        public string BotToken
        {
            get => _botToken;
            set => SetProperty(ref _botToken, value);
        }

        public int RefreshIntervalMinutes
        {
            get => _refreshIntervalMinutes;
            set => SetProperty(ref _refreshIntervalMinutes, value);
        }

        public int MaxMessages
        {
            get => _maxMessages;
            set => SetProperty(ref _maxMessages, value);
        }

        public SlackSettingsViewModel(IPluginSettingsStorage storage)
        {
            _storage = storage;
            Load();
        }

        public override void Load()
        {
            BotToken = _storage.Get("BotToken", string.Empty) ?? string.Empty;
            RefreshIntervalMinutes = _storage.Get("RefreshIntervalMinutes", 5);
            MaxMessages = _storage.Get("MaxMessages", 30);
            IsDirty = false;
        }

        public override void Save()
        {
            _storage.Set("BotToken", BotToken);
            _storage.Set("RefreshIntervalMinutes", RefreshIntervalMinutes);
            _storage.Set("MaxMessages", MaxMessages);
            _ = _storage.SaveAsync();
            IsDirty = false;
        }

        public override void Reset()
        {
            BotToken = string.Empty;
            RefreshIntervalMinutes = 5;
            MaxMessages = 30;
        }
    }
}
