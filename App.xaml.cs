using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Reflection;
using AIA.Models;
using AIA.Plugins.Host;
using AIA.Services;

namespace AIA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? notifyIcon;
        private PluginManager? _pluginManager;
        private HotkeyService? _hotkeyService;
        private AppSettings? _appSettings;
        private MiniTaskOverlayHost? _miniTaskOverlayHost;

        /// <summary>
        /// Gets the plugin manager instance
        /// </summary>
        public PluginManager? PluginManager => _pluginManager;

        /// <summary>
        /// Gets the hotkey service instance
        /// </summary>
        public HotkeyService? HotkeyService => _hotkeyService;

        /// <summary>
        /// Gets the compact task list overlay host
        /// </summary>
        public MiniTaskOverlayHost? MiniTaskOverlayHost => _miniTaskOverlayHost;

        /// <summary>
        /// Gets the current App instance
        /// </summary>
        public static new App Current => (App)System.Windows.Application.Current;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppPaths.Initialize(message => System.Diagnostics.Debug.WriteLine(message));

            // Load app settings
            _appSettings = await AppSettingsService.LoadAppSettingsAsync();

            // Run a scheduled backup in the background when enabled
            _ = AppSettingsService.RunScheduledBackupAsync(_appSettings);

            // Report-only update check
            if (_appSettings.CheckForUpdatesOnStartup)
            {
                _ = CheckForUpdatesInBackgroundAsync();
            }

            // Apply focus protection before any global hotkey is registered
            WindowsIntegrationService.Current.Configure(_appSettings.WindowsIntegration);

            // Initialize localization with saved language preference
            if (!string.IsNullOrEmpty(_appSettings.Language))
            {
                LocalizationService.Instance.InitializeWithSavedLanguage(_appSettings.Language);
            }

            // Create the NotifyIcon
            notifyIcon = new NotifyIcon();
            
            // Load custom icon from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("AIA.Icons.AIA_s.ico");
            if (stream != null)
            {
                notifyIcon.Icon = new Icon(stream);
            }
            else
            {
                notifyIcon.Icon = SystemIcons.Application;
            }
            
            notifyIcon.Text = "AIA";
            notifyIcon.Visible = true;

            // Double-click to show/restore window
            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(LocalizationService.Instance.GetString("Tray_Show"), null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add(LocalizationService.Instance.GetString("Tray_Exit"), null, (s, args) => Shutdown());
            notifyIcon.ContextMenuStrip = contextMenu;

            // Initialize hotkey service
            InitializeHotkeyService();

            // Create the always-on compact task list after the main window exists
            Dispatcher.BeginInvoke(new Action(InitializeMiniTaskOverlay),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Initialize plugin system
            await InitializePluginsAsync();
        }

        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                var latest = await UpdateCheckService.GetLatestVersionAsync();
                if (latest != null && UpdateCheckService.IsNewer(latest, AppSettingsService.GetAppVersion()))
                {
                    notifyIcon?.ShowBalloonTip(
                        8000,
                        "AIA update available",
                        $"Version {latest} is available on GitHub. Open Settings → General to learn more.",
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup update check failed: {ex.Message}");
            }
        }

        private void InitializeHotkeyService()
        {
            _hotkeyService = new HotkeyService();
            _hotkeyService.OverlayHotkeyPressed += OnOverlayHotkeyPressed;

            // Register the configured hotkey
            var hotkeyString = _appSettings?.OverlayShortcut ?? "Alt+Q";
            var success = _hotkeyService.RegisterHotkey(hotkeyString);

            // Win+Q and other shell-reserved combinations cannot be registered; fall back.
            if (!success && hotkeyString.Equals("Win+Q", StringComparison.OrdinalIgnoreCase))
            {
                hotkeyString = "Alt+Q";
                success = _hotkeyService.RegisterHotkey(hotkeyString);
            }

            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {hotkeyString}");
                notifyIcon?.ShowBalloonTip(
                    5000,
                    "AIA hotkey unavailable",
                    $"The shortcut '{hotkeyString}' is already in use by another application. Open Settings → Overlay to choose another.",
                    ToolTipIcon.Warning);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Registered global hotkey: {hotkeyString}");
            }
        }

        private void OnOverlayHotkeyPressed(object? sender, NHotkey.HotkeyEventArgs e)
        {
            e.Handled = true;

            var windowsIntegration = WindowsIntegrationService.Current;
            if (windowsIntegration.ShouldSuppressHotkey(HotkeyPurpose.Overlay))
            {
                if (MainWindow is AIA.MainWindow overlay)
                {
                    if (overlay.IsVisible)
                    {
                        overlay.HideWithAnimation();
                    }
                    else if (windowsIntegration.Settings.HotkeyAction == ProtectedAppHotkeyAction.ShowWithoutActivation)
                    {
                        overlay.ShowWithAnimation(preserveForegroundFocus: true);
                    }
                }

                return;
            }

            ToggleMainWindow();
        }

        private void ToggleMainWindow()
        {
            if (MainWindow is AIA.MainWindow mainWindow)
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.HideWithAnimation();
                }
                else
                {
                    mainWindow.ShowWithAnimation();
                }
            }
        }

        /// <summary>
        /// Updates the global hotkey registration with a new shortcut
        /// </summary>
        public bool UpdateHotkey(string hotkeyString)
        {
            if (_hotkeyService == null)
                return false;

            return _hotkeyService.RegisterHotkey(hotkeyString);
        }

        /// <summary>
        /// Applies compact task list overlay settings without restarting the app.
        /// </summary>
        public void RefreshMiniTaskOverlaySettings(AppSettings settings)
        {
            _miniTaskOverlayHost?.ApplySettings(settings);
        }

        /// <summary>
        /// Updates the shortcut that toggles the compact task list's interactable mode.
        /// </summary>
        public bool UpdateMiniTaskOverlayHotkey(string hotkeyString)
        {
            return _miniTaskOverlayHost?.UpdateHotkey(hotkeyString) ?? false;
        }

        private void InitializeMiniTaskOverlay()
        {
            if (_appSettings == null)
                return;

            _miniTaskOverlayHost = new MiniTaskOverlayHost();
            _miniTaskOverlayHost.HotkeyRegistrationFailed += (_, shortcut) =>
            {
                notifyIcon?.ShowBalloonTip(
                    5000,
                    "AIA task overlay shortcut unavailable",
                    $"The shortcut '{shortcut}' is already in use. Open Settings → General to choose another.",
                    ToolTipIcon.Warning);
            };
            _miniTaskOverlayHost.Initialize(_appSettings);
        }

        /// <summary>
        /// Applies focus protection settings without restarting the app
        /// </summary>
        public void RefreshWindowsIntegrationSettings(AppSettings settings)
        {
            WindowsIntegrationService.Current.Configure(settings?.WindowsIntegration);
        }

        private async Task InitializePluginsAsync()
        {
            try
            {
                var pluginSettings = await AppSettingsService.LoadPluginSettingsAsync();
                if (!pluginSettings.EnablePlugins)
                {
                    System.Diagnostics.Debug.WriteLine("Plugin system disabled in settings.");
                    return;
                }

                // Determine plugins directory
                var pluginsDir = AppPaths.PluginsDirectory;

                // Create host services
                var hostServices = new PluginHostServices(() => OverlayViewModel.Singleton);

                // Set up OverlayViewModel to use plugin UI service
                OverlayViewModel.Singleton.SetPluginUIService(hostServices.UIService);

                // Create plugin manager
                _pluginManager = new PluginManager(pluginsDir, hostServices, AppPaths.PluginDataFolder);

                // Subscribe to plugin events
                _pluginManager.PluginLoaded += (s, args) =>
                    System.Diagnostics.Debug.WriteLine($"Plugin loaded: {args.Plugin.Name}");
                _pluginManager.PluginError += (s, args) =>
                    System.Diagnostics.Debug.WriteLine($"Plugin error: {args.Plugin.Name} - {args.Message}");

                // Load, initialize, and start all plugins
                await _pluginManager.LoadAllPluginsAsync();
                await _pluginManager.InitializeAllPluginsAsync();
                await _pluginManager.StartAllPluginsAsync();

                System.Diagnostics.Debug.WriteLine($"Plugin system initialized with {_pluginManager.Plugins.Count} plugin(s)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize plugins: {ex.Message}");
            }
        }

        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (MainWindow is AIA.MainWindow mainWindow)
            {
                mainWindow.ShowWithAnimation();
                mainWindow.WindowState = WindowState.Normal;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _miniTaskOverlayHost?.Dispose();
                _hotkeyService?.Dispose();

                // Shut plugins down synchronously: WPF does not await async handlers.
                if (_pluginManager != null)
                {
                    try
                    {
                        _pluginManager.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Plugin shutdown failed: {ex.Message}");
                    }

                    _pluginManager.Dispose();
                }

                // Flush tasks, reminders and chats; give the IO a bounded amount of time.
                var saves = new[]
                {
                    OverlayViewModel.Singleton.SaveTasksAndRemindersAsync(),
                    OverlayViewModel.Singleton.SaveChatsAsync()
                };

                if (!Task.WaitAll(saves, TimeSpan.FromSeconds(5)))
                {
                    System.Diagnostics.Debug.WriteLine("Timed out while saving data on exit.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to persist data on exit: {ex.Message}");
            }
            finally
            {
                notifyIcon?.Dispose();
                base.OnExit(e);
            }
        }
    }
}
