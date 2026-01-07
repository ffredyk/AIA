using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using AIA.Models.Automation;
using NHotkey;
using NHotkey.Wpf;
using WpfClipboard = System.Windows.Clipboard;

namespace AIA.Services.Automation
{
    /// <summary>
    /// Service that monitors various trigger sources and fires events
    /// </summary>
    public class TriggerMonitorService : IDisposable
    {
        private readonly AutomationService _automationService;
        private readonly Dictionary<Guid, AutomationTrigger> _registeredTriggers = new();
        private readonly Dictionary<Guid, DateTime> _lastTriggerTimes = new();
        private readonly object _lockObject = new();

        // Monitors
        private ClipboardMonitor? _clipboardMonitor;
        private readonly Dictionary<Guid, FileSystemWatcher> _fileWatchers = new();
        private DispatcherTimer? _windowContextTimer;
        private IntPtr _lastActiveWindow;
        private string _lastWindowTitle = string.Empty;

        private bool _isRunning;
        private bool _isPaused;
        private bool _isDisposed;

        /// <summary>
        /// Event fired when a trigger activates
        /// </summary>
        public event EventHandler<TriggerFiredEventArgs>? TriggerFired;

        public TriggerMonitorService(AutomationService automationService)
        {
            _automationService = automationService;
        }

        #region Lifecycle

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            // Initialize clipboard monitor
            _clipboardMonitor = new ClipboardMonitor();
            _clipboardMonitor.ClipboardChanged += OnClipboardChanged;

            // Initialize window context timer
            _windowContextTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _windowContextTimer.Tick += OnWindowContextTimerTick;
            _windowContextTimer.Start();
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            // Stop clipboard monitor
            if (_clipboardMonitor != null)
            {
                _clipboardMonitor.ClipboardChanged -= OnClipboardChanged;
                _clipboardMonitor.Dispose();
                _clipboardMonitor = null;
            }

            // Stop file watchers
            foreach (var watcher in _fileWatchers.Values)
            {
                watcher.Dispose();
            }
            _fileWatchers.Clear();

            // Stop window context timer
            _windowContextTimer?.Stop();
            _windowContextTimer = null;

            // Unregister all hotkeys
            foreach (var triggerId in _registeredTriggers.Keys.ToList())
            {
                UnregisterHotkey(triggerId);
            }
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        #endregion

        #region Trigger Registration

        public void RegisterTrigger(Guid automationId, AutomationTrigger trigger)
        {
            lock (_lockObject)
            {
                _registeredTriggers[automationId] = trigger;
            }

            switch (trigger)
            {
                case HotkeyTrigger hotkey:
                    RegisterHotkey(automationId, hotkey);
                    break;

                case FileChangeTrigger fileChange:
                    RegisterFileWatcher(automationId, fileChange);
                    break;
            }
        }

        public void UnregisterTrigger(Guid automationId)
        {
            AutomationTrigger? trigger;
            lock (_lockObject)
            {
                if (!_registeredTriggers.TryGetValue(automationId, out trigger))
                    return;

                _registeredTriggers.Remove(automationId);
            }

            switch (trigger)
            {
                case HotkeyTrigger:
                    UnregisterHotkey(automationId);
                    break;

                case FileChangeTrigger:
                    UnregisterFileWatcher(automationId);
                    break;
            }
        }

        #endregion

        #region Clipboard Monitoring

        private void OnClipboardChanged(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            var clipboardTriggers = GetTriggersOfType<ClipboardTrigger>();
            if (!clipboardTriggers.Any()) return;

            // Get clipboard data
            ClipboardContentType contentType = ClipboardContentType.None;
            string? textContent = null;
            List<string>? filePaths = null;

            try
            {
                if (WpfClipboard.ContainsText())
                {
                    contentType |= ClipboardContentType.Text;
                    textContent = WpfClipboard.GetText();
                }
                if (WpfClipboard.ContainsImage())
                {
                    contentType |= ClipboardContentType.Image;
                }
                if (WpfClipboard.ContainsFileDropList())
                {
                    contentType |= ClipboardContentType.FilePaths;
                    filePaths = WpfClipboard.GetFileDropList().Cast<string>().ToList();
                }
            }
            catch
            {
                return;
            }

            foreach (var (automationId, trigger) in clipboardTriggers)
            {
                if (!CheckDebounce(automationId, trigger.DebounceMsec))
                    continue;

                if (!trigger.IsEnabled)
                    continue;

                // Check content type filter
                if ((trigger.ContentType & contentType) == 0)
                    continue;

                // Check text filter
                if (!string.IsNullOrEmpty(trigger.TextFilter) && textContent != null)
                {
                    bool matches;
                    if (trigger.UseRegex)
                    {
                        try
                        {
                            matches = Regex.IsMatch(textContent, trigger.TextFilter);
                        }
                        catch
                        {
                            matches = false;
                        }
                    }
                    else
                    {
                        matches = textContent.Contains(trigger.TextFilter, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matches)
                        continue;
                }

                FireTrigger(automationId, trigger, new ClipboardTriggerData
                {
                    ContentType = contentType,
                    TextContent = textContent,
                    FilePaths = filePaths
                });
            }
        }

        #endregion

        #region Hotkey Monitoring

        private void RegisterHotkey(Guid automationId, HotkeyTrigger trigger)
        {
            if (trigger.Key == Key.None) return;

            var hotkeyName = $"Automation_{automationId:N}";

            try
            {
                HotkeyManager.Current.AddOrReplace(hotkeyName, trigger.Key, trigger.Modifiers, (s, e) =>
                {
                    if (_isPaused) return;

                    if (CheckDebounce(automationId, trigger.DebounceMsec))
                    {
                        FireTrigger(automationId, trigger, null);
                    }

                    e.Handled = true;
                });
            }
            catch (HotkeyAlreadyRegisteredException)
            {
                // Hotkey is already in use
            }
        }

        private void UnregisterHotkey(Guid automationId)
        {
            var hotkeyName = $"Automation_{automationId:N}";
            try
            {
                HotkeyManager.Current.Remove(hotkeyName);
            }
            catch { }
        }

        #endregion

        #region File System Monitoring

        private void RegisterFileWatcher(Guid automationId, FileChangeTrigger trigger)
        {
            if (string.IsNullOrEmpty(trigger.WatchPath) || !Directory.Exists(trigger.WatchPath))
                return;

            UnregisterFileWatcher(automationId);

            var watcher = new FileSystemWatcher(trigger.WatchPath)
            {
                Filter = trigger.FileFilter,
                IncludeSubdirectories = trigger.IncludeSubdirectories,
                EnableRaisingEvents = true
            };

            if (trigger.ChangeTypes.HasFlag(FileChangeType.Created))
                watcher.Created += (s, e) => OnFileChanged(automationId, trigger, e, FileChangeType.Created);

            if (trigger.ChangeTypes.HasFlag(FileChangeType.Modified))
                watcher.Changed += (s, e) => OnFileChanged(automationId, trigger, e, FileChangeType.Modified);

            if (trigger.ChangeTypes.HasFlag(FileChangeType.Deleted))
                watcher.Deleted += (s, e) => OnFileChanged(automationId, trigger, e, FileChangeType.Deleted);

            if (trigger.ChangeTypes.HasFlag(FileChangeType.Renamed))
                watcher.Renamed += (s, e) => OnFileRenamed(automationId, trigger, e);

            _fileWatchers[automationId] = watcher;
        }

        private void UnregisterFileWatcher(Guid automationId)
        {
            if (_fileWatchers.TryGetValue(automationId, out var watcher))
            {
                watcher.Dispose();
                _fileWatchers.Remove(automationId);
            }
        }

        private void OnFileChanged(Guid automationId, FileChangeTrigger trigger, FileSystemEventArgs e, FileChangeType changeType)
        {
            if (_isPaused) return;

            if (!CheckDebounce(automationId, trigger.DebounceMsec))
                return;

            FireTrigger(automationId, trigger, new FileChangeTriggerData
            {
                ChangeType = changeType,
                FilePath = e.FullPath,
                FileName = Path.GetFileName(e.FullPath),
                Extension = Path.GetExtension(e.FullPath)
            });
        }

        private void OnFileRenamed(Guid automationId, FileChangeTrigger trigger, RenamedEventArgs e)
        {
            if (_isPaused) return;

            if (!CheckDebounce(automationId, trigger.DebounceMsec))
                return;

            FireTrigger(automationId, trigger, new FileChangeTriggerData
            {
                ChangeType = FileChangeType.Renamed,
                FilePath = e.FullPath,
                OldPath = e.OldFullPath,
                FileName = Path.GetFileName(e.FullPath),
                Extension = Path.GetExtension(e.FullPath)
            });
        }

        #endregion

        #region Window Context Monitoring

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private void OnWindowContextTimerTick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            var windowContextTriggers = GetTriggersOfType<WindowContextTrigger>();
            if (!windowContextTriggers.Any()) return;

            var activeWindow = GetForegroundWindow();
            if (activeWindow == _lastActiveWindow)
                return;

            _lastActiveWindow = activeWindow;

            // Get window title
            var titleBuilder = new StringBuilder(256);
            GetWindowText(activeWindow, titleBuilder, 256);
            var windowTitle = titleBuilder.ToString();

            if (windowTitle == _lastWindowTitle)
                return;

            _lastWindowTitle = windowTitle;

            // Get process name
            GetWindowThreadProcessId(activeWindow, out var processId);
            string processName = string.Empty;
            try
            {
                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch { }

            foreach (var (automationId, trigger) in windowContextTriggers)
            {
                if (!trigger.IsEnabled)
                    continue;

                if (!CheckDebounce(automationId, trigger.DebounceMsec))
                    continue;

                bool matches = MatchesWindowFilter(trigger, windowTitle, processName);

                if (matches == trigger.TriggerOnMatch)
                {
                    FireTrigger(automationId, trigger, new WindowContextTriggerData
                    {
                        WindowTitle = windowTitle,
                        ProcessName = processName,
                        WindowHandle = activeWindow
                    });
                }
            }
        }

        private bool MatchesWindowFilter(WindowContextTrigger trigger, string windowTitle, string processName)
        {
            bool titleMatches = true;
            bool processMatches = true;

            if (!string.IsNullOrEmpty(trigger.WindowTitleFilter))
            {
                if (trigger.UseRegex)
                {
                    try
                    {
                        titleMatches = Regex.IsMatch(windowTitle, trigger.WindowTitleFilter, RegexOptions.IgnoreCase);
                    }
                    catch
                    {
                        titleMatches = false;
                    }
                }
                else
                {
                    titleMatches = windowTitle.Contains(trigger.WindowTitleFilter, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!string.IsNullOrEmpty(trigger.ProcessNameFilter))
            {
                processMatches = processName.Contains(trigger.ProcessNameFilter, StringComparison.OrdinalIgnoreCase);
            }

            return titleMatches && processMatches;
        }

        #endregion

        #region Helpers

        private IEnumerable<(Guid AutomationId, T Trigger)> GetTriggersOfType<T>() where T : AutomationTrigger
        {
            lock (_lockObject)
            {
                return _registeredTriggers
                    .Where(kv => kv.Value is T)
                    .Select(kv => (kv.Key, (T)kv.Value))
                    .ToList();
            }
        }

        private bool CheckDebounce(Guid automationId, int debounceMsec)
        {
            var now = DateTime.Now;

            lock (_lockObject)
            {
                if (_lastTriggerTimes.TryGetValue(automationId, out var lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < debounceMsec)
                        return false;
                }

                _lastTriggerTimes[automationId] = now;
            }

            return true;
        }

        private void FireTrigger(Guid automationId, AutomationTrigger trigger, TriggerData? data)
        {
            TriggerFired?.Invoke(this, new TriggerFiredEventArgs
            {
                AutomationId = automationId,
                Trigger = trigger,
                TriggerData = data
            });
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
        }
    }

    /// <summary>
    /// Helper class to monitor clipboard changes
    /// </summary>
    internal class ClipboardMonitor : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _lastClipboardContent = string.Empty;

        public event EventHandler? ClipboardChanged;

        public ClipboardMonitor()
        {
            // Use a timer-based approach since WPF doesn't have built-in clipboard monitoring
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                var currentContent = string.Empty;

                if (WpfClipboard.ContainsText())
                {
                    currentContent = WpfClipboard.GetText();
                }
                else if (WpfClipboard.ContainsImage())
                {
                    currentContent = "[IMAGE]";
                }
                else if (WpfClipboard.ContainsFileDropList())
                {
                    var files = WpfClipboard.GetFileDropList();
                    currentContent = $"[FILES:{files.Count}]";
                }

                if (currentContent != _lastClipboardContent)
                {
                    _lastClipboardContent = currentContent;
                    ClipboardChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch
            {
                // Clipboard access can fail if another app has it locked
            }
        }

        public void Dispose()
        {
            _timer.Stop();
        }
    }
}
