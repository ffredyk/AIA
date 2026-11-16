using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// The hotkey that is being evaluated for focus protection.
    /// </summary>
    public enum HotkeyPurpose
    {
        Overlay,
        Automation
    }

    /// <summary>
    /// Snapshot of the foreground application and whether Windows considers it fullscreen.
    /// </summary>
    public sealed record ForegroundAppInfo(
        IntPtr Handle,
        uint ProcessId,
        string ProcessName,
        string WindowTitle,
        string WindowClass,
        bool IsFullscreen,
        bool IsExclusiveFullscreen,
        bool IsPresentationMode,
        bool IsLikelyGame,
        bool IsKnownProtectedApp,
        bool IsCustomProtectedApp,
        bool IsShellWindow)
    {
        public static ForegroundAppInfo Empty { get; } = new(
            IntPtr.Zero, 0, string.Empty, string.Empty, string.Empty,
            false, false, false, false, false, false, false);
    }

    /// <summary>
    /// Detects foreground apps that should not lose focus when an AIA global hotkey is pressed.
    /// </summary>
    public class WindowsIntegrationService
    {
        private const uint MonitorDefaultToNearest = 2;
        private const int FullscreenTolerancePixels = 4;
        private const int CacheDurationMilliseconds = 500;

        private static readonly HashSet<string> KnownProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Remote desktop / remote control
            "mstsc", "vmconnect", "wfica32", "anydesk", "teamviewer", "tvnviewer", "vncviewer", "vncserver",
            // Presentations and meeting / screen-sharing apps
            "powerpnt", "zoom", "teams", "ms-teams", "webexmta", "webex", "gotomeeting", "g2mstart", "g2mcomm"
        };

        private static readonly HashSet<string> GameWindowClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW30", "GLFW20",
            "CryENGINE", "RiotWindowClass", "Valve001", "D3DWindow", "GameWindow", "LWJGL"
        };

        private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"
        };

        private readonly object _cacheLock = new();
        private WindowsIntegrationSettings _settings = new();
        private HashSet<string> _customProtectedProcesses = new(StringComparer.OrdinalIgnoreCase);
        private IntPtr _cachedHandle = IntPtr.Zero;
        private ForegroundAppInfo? _cachedInfo;
        private long _cachedAt;

        /// <summary>
        /// Shared service instance used by the app and automation hotkeys.
        /// </summary>
        public static WindowsIntegrationService Current { get; } = new();

        /// <summary>
        /// Gets the settings currently used for protection checks.
        /// </summary>
        public WindowsIntegrationSettings Settings => _settings;

        /// <summary>
        /// Applies new settings and clears cached foreground information.
        /// </summary>
        public void Configure(WindowsIntegrationSettings? settings)
        {
            _settings = settings ?? new WindowsIntegrationSettings();
            _customProtectedProcesses = ParseProcessList(_settings.CustomProtectedProcesses);

            lock (_cacheLock)
            {
                _cachedHandle = IntPtr.Zero;
                _cachedInfo = null;
                _cachedAt = 0;
            }
        }

        /// <summary>
        /// Returns true when the current foreground app should keep focus instead of receiving AIA's hotkey.
        /// </summary>
        public bool ShouldSuppressHotkey(HotkeyPurpose purpose)
        {
            var settings = _settings;
            if (!settings.Enabled)
                return false;

            if (purpose == HotkeyPurpose.Automation && !settings.ApplyToAutomationHotkeys)
                return false;

            var info = GetForegroundAppInfo();
            if (info.Handle == IntPtr.Zero || info.IsShellWindow)
                return false;

            // Never treat AIA's own windows (overlay, settings, dialogs) as protected.
            if (info.ProcessId == (uint)Environment.ProcessId)
                return false;

            if (settings.ProtectFullscreenAndGames && (info.IsFullscreen || info.IsLikelyGame))
                return true;

            if (settings.ProtectKnownApps && (info.IsKnownProtectedApp || info.IsPresentationMode))
                return true;

            return info.IsCustomProtectedApp;
        }

        /// <summary>
        /// Gets a cached snapshot of the current foreground app.
        /// </summary>
        public ForegroundAppInfo GetForegroundAppInfo()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
                return ForegroundAppInfo.Empty;

            lock (_cacheLock)
            {
                if (_cachedInfo != null &&
                    _cachedHandle == handle &&
                    Environment.TickCount64 - _cachedAt < CacheDurationMilliseconds)
                {
                    return _cachedInfo;
                }
            }

            var info = QueryForegroundApp(handle);

            lock (_cacheLock)
            {
                _cachedHandle = handle;
                _cachedInfo = info;
                _cachedAt = Environment.TickCount64;
            }

            return info;
        }

        /// <summary>
        /// Gets the current foreground window handle.
        /// </summary>
        public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

        /// <summary>
        /// Tries to restore focus to a previously active window.
        /// </summary>
        public static bool TryRestoreForegroundWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return SetForegroundWindow(handle);
            }
            catch
            {
                return false;
            }
        }

        private ForegroundAppInfo QueryForegroundApp(IntPtr handle)
        {
            var title = GetWindowTitle(handle);
            var windowClass = GetWindowClassName(handle);

            GetWindowThreadProcessId(handle, out var processId);
            var processName = string.Empty;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                // The process may have exited between the foreground query and lookup.
            }

            var notificationState = GetNotificationState();
            var isFullscreenRect = IsWindowFullscreen(handle);
            var isPresentationMode = notificationState == UserNotificationState.PresentationMode;
            var isExclusiveFullscreen = notificationState == UserNotificationState.RunningD3dFullScreen;
            var isFullscreen = isFullscreenRect ||
                               isExclusiveFullscreen ||
                               notificationState == UserNotificationState.Busy ||
                               isPresentationMode;

            var isLikelyGame = GameWindowClasses.Contains(windowClass);
            var isKnownProtected = KnownProtectedProcesses.Contains(processName) || isPresentationMode;
            var isCustomProtected = _customProtectedProcesses.Contains(processName);
            var isShellWindow = ShellWindowClasses.Contains(windowClass) ||
                                (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title));

            return new ForegroundAppInfo(
                handle,
                processId,
                processName,
                title,
                windowClass,
                isFullscreen,
                isExclusiveFullscreen,
                isPresentationMode,
                isLikelyGame,
                isKnownProtected,
                isCustomProtected,
                isShellWindow);
        }

        private static bool IsWindowFullscreen(IntPtr handle)
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
                return false;

            if (!GetWindowRect(handle, out var windowRect))
                return false;

            if (windowRect.Width <= 0 || windowRect.Height <= 0)
                return false;

            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return false;

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            var bounds = monitorInfo.rcMonitor;

            return windowRect.Left <= bounds.Left + FullscreenTolerancePixels &&
                   windowRect.Top <= bounds.Top + FullscreenTolerancePixels &&
                   windowRect.Right >= bounds.Right - FullscreenTolerancePixels &&
                   windowRect.Bottom >= bounds.Bottom - FullscreenTolerancePixels;
        }

        private static HashSet<string> ParseProcessList(string? processList)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(processList))
                return result;

            foreach (var item in processList.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = item.Trim().Trim('"');
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];

                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(name);
            }

            return result;
        }

        private static UserNotificationState GetNotificationState()
        {
            try
            {
                return SHQueryUserNotificationState(out var state) == 0
                    ? state
                    : UserNotificationState.Unknown;
            }
            catch
            {
                return UserNotificationState.Unknown;
            }
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            var length = GetWindowTextLength(handle);
            if (length <= 0)
                return string.Empty;

            var builder = new StringBuilder(length + 1);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string GetWindowClassName(IntPtr handle)
        {
            var builder = new StringBuilder(256);
            GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        #region Native Methods

        private enum UserNotificationState
        {
            Unknown = 0,
            NotPresent = 1,
            Busy = 2,
            RunningD3dFullScreen = 3,
            PresentationMode = 4,
            AcceptsNotifications = 5,
            QuietTime = 6,
            App = 7
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out UserNotificationState state);

        #endregion
    }
}
