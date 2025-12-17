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

        /// <summary>
        /// Gets the plugin manager instance
        /// </summary>
        public PluginManager? PluginManager => _pluginManager;

        /// <summary>
        /// Gets the hotkey service instance
        /// </summary>
        public HotkeyService? HotkeyService => _hotkeyService;

        /// <summary>
        /// Gets the current App instance
        /// </summary>
        public static new App Current => (App)System.Windows.Application.Current;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load app settings
            _appSettings = await AppSettingsService.LoadAppSettingsAsync();

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
            contextMenu.Items.Add("Show", null, (s, args) => ShowMainWindow());
            contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());
            notifyIcon.ContextMenuStrip = contextMenu;

            // Initialize hotkey service
            InitializeHotkeyService();

            // Initialize plugin system
            await InitializePluginsAsync();
        }

        private void InitializeHotkeyService()
        {
            _hotkeyService = new HotkeyService();
            _hotkeyService.OverlayHotkeyPressed += OnOverlayHotkeyPressed;

            // Register the configured hotkey
            var hotkeyString = _appSettings?.OverlayShortcut ?? "Win+Q";
            var success = _hotkeyService.RegisterHotkey(hotkeyString);
            
            if (!success)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register hotkey: {hotkeyString}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Registered global hotkey: {hotkeyString}");
            }
        }

        private void OnOverlayHotkeyPressed(object? sender, NHotkey.HotkeyEventArgs e)
        {
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

        private async Task InitializePluginsAsync()
        {
            try
            {
                // Determine plugins directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var pluginsDir = Path.Combine(appDir, "Plugins");

                // Create host services
                var hostServices = new PluginHostServices(() => OverlayViewModel.Singleton);

                // Set up OverlayViewModel to use plugin UI service
                OverlayViewModel.Singleton.SetPluginUIService(hostServices.UIService);

                // Create plugin manager
                _pluginManager = new PluginManager(pluginsDir, hostServices);

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

        protected override async void OnExit(ExitEventArgs e)
        {
            // Dispose hotkey service
            _hotkeyService?.Dispose();

            // Shutdown plugin system
            if (_pluginManager != null)
            {
                await _pluginManager.ShutdownAsync();
                _pluginManager.Dispose();
            }

            // Save tasks and reminders before exiting
            await OverlayViewModel.Singleton.SaveTasksAndRemindersAsync();
            
            notifyIcon?.Dispose();
            base.OnExit(e);
        }
    }
}
