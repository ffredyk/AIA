using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using AIA.Models;
using Microsoft.Win32;

namespace AIA.Services
{
    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class AppSettingsService
    {
        private static string SettingsFolder => AppPaths.SettingsFolder;
        private static string AppSettingsFile => Path.Combine(SettingsFolder, "app-settings.json");
        private static string PluginSettingsFile => Path.Combine(SettingsFolder, "plugin-settings.json");
        private static string PluginInstanceSettingsFile => Path.Combine(SettingsFolder, "plugin-instance-settings.json");

        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AIA";

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);
        }

        #region App Settings

        public static async Task<AppSettings> LoadAppSettingsAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(AppSettingsFile))
            {
                var defaultSettings = new AppSettings();
                // Check if already running on startup
                defaultSettings.RunOnStartup = IsRunningOnStartup();
                return defaultSettings;
            }

            try
            {
                var settings = await JsonFileStore.ReadAsync<AppSettings>(AppSettingsFile, GetJsonOptions());
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                // Preserve the unreadable file instead of silently dropping user settings.
                var quarantined = JsonFileStore.Quarantine(AppSettingsFile);
                System.Diagnostics.Debug.WriteLine($"Failed to load app settings ({ex.Message}); preserved at '{quarantined}'.");
                return new AppSettings();
            }
        }

        public static async Task SaveAppSettingsAsync(AppSettings settings)
        {
            EnsureDirectoryExists();
            await JsonFileStore.WriteAsync(AppSettingsFile, settings, GetJsonOptions()).ConfigureAwait(false);
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions { WriteIndented = true };
        }

        #endregion

        #region Plugin Settings

        public static async Task<PluginSettings> LoadPluginSettingsAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(PluginSettingsFile))
            {
                return new PluginSettings();
            }

            try
            {
                var settings = await JsonFileStore.ReadAsync<PluginSettings>(PluginSettingsFile, GetJsonOptions());
                return settings ?? new PluginSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load plugin settings: {ex.Message}");
                return new PluginSettings();
            }
        }

        public static async Task SavePluginSettingsAsync(PluginSettings settings)
        {
            EnsureDirectoryExists();
            await JsonFileStore.WriteAsync(PluginSettingsFile, settings, GetJsonOptions()).ConfigureAwait(false);
        }

        public static async Task<List<PluginInstanceSettings>> LoadPluginInstanceSettingsAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(PluginInstanceSettingsFile))
            {
                return new List<PluginInstanceSettings>();
            }

            try
            {
                var settings = await JsonFileStore.ReadAsync<List<PluginInstanceSettings>>(PluginInstanceSettingsFile, GetJsonOptions());
                return settings ?? new List<PluginInstanceSettings>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load plugin instance settings: {ex.Message}");
                return new List<PluginInstanceSettings>();
            }
        }

        public static async Task SavePluginInstanceSettingsAsync(List<PluginInstanceSettings> settings)
        {
            EnsureDirectoryExists();
            await JsonFileStore.WriteAsync(PluginInstanceSettingsFile, settings, GetJsonOptions()).ConfigureAwait(false);
        }

        #endregion

        #region Windows Startup (Registry)

        public static bool IsRunningOnStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetRunOnStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                if (key == null) return false;

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        return true;
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    return true;
                }
            }
            catch
            {
                // Failed to modify registry
            }

            return false;
        }

        #endregion

        #region Hotkey Management

        /// <summary>
        /// Parses a shortcut string like "Win+Q" into modifiers and key.
        /// This is kept for backwards compatibility - use HotkeyService for new code.
        /// </summary>
        [Obsolete("Use HotkeyService.ParseHotkeyString instead")]
        public static (uint modifiers, uint virtualKey) ParseShortcut(string shortcut)
        {
            // Convert to the new format and back for compatibility
            var (modifierKeys, key) = HotkeyService.ParseHotkeyString(shortcut);
            
            uint modifiers = 0x4000; // MOD_NOREPEAT
            if (modifierKeys.HasFlag(ModifierKeys.Windows)) modifiers |= 0x0008;
            if (modifierKeys.HasFlag(ModifierKeys.Control)) modifiers |= 0x0002;
            if (modifierKeys.HasFlag(ModifierKeys.Alt)) modifiers |= 0x0001;
            if (modifierKeys.HasFlag(ModifierKeys.Shift)) modifiers |= 0x0004;
            
            uint virtualKey = key != Key.None ? (uint)KeyInterop.VirtualKeyFromKey(key) : 0;
            
            return (modifiers, virtualKey);
        }

        #endregion

        #region Backup

        public static string GetBackupFolder()
        {
            return AppPaths.BackupsFolder;
        }

        /// <summary>
        /// Creates a backup of all user data folders under the resolved data root.
        /// </summary>
        public static Task<bool> CreateBackupAsync()
        {
            return Task.Run(CreateBackupCore);
        }

        private static bool CreateBackupCore()
        {
            try
            {
                var backupFolder = GetBackupFolder();
                Directory.CreateDirectory(backupFolder);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = Path.Combine(backupFolder, $"backup_{timestamp}");
                Directory.CreateDirectory(backupPath);

                var sources = new (string Source, string Name)[]
                {
                    (AppPaths.UserDataFolder, "userdata"),
                    (AppPaths.DataBanksFolder, "databanks"),
                    (AppPaths.AutomationsFolder, "automations"),
                    (AppPaths.AiConfigFolder, "ai-config"),
                    (AppPaths.SettingsFolder, "settings")
                };

                foreach (var (source, name) in sources)
                {
                    if (Directory.Exists(source))
                    {
                        CopyDirectory(source, Path.Combine(backupPath, name));
                    }
                }

                var manifest = $"AIA backup{Environment.NewLine}Created: {DateTime.Now:O}{Environment.NewLine}Version: {GetAppVersion()}{Environment.NewLine}Source: {AppPaths.DataRoot}{Environment.NewLine}";
                File.WriteAllText(Path.Combine(backupPath, "backup-info.txt"), manifest);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Backup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a scheduled backup when the configured interval has elapsed.
        /// </summary>
        public static async Task RunScheduledBackupAsync(AppSettings settings)
        {
            if (settings == null || !settings.EnableAutoBackup)
                return;

            try
            {
                var backupFolder = GetBackupFolder();
                Directory.CreateDirectory(backupFolder);

                var interval = TimeSpan.FromHours(Math.Max(1, settings.AutoBackupIntervalHours));
                var newest = Directory.GetDirectories(backupFolder, "backup_*")
                    .Select(Directory.GetCreationTimeUtc)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

                if (DateTime.UtcNow - newest < interval)
                    return;

                if (await CreateBackupAsync())
                {
                    await CleanupOldBackupsAsync(Math.Max(1, settings.MaxBackupCount));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scheduled backup failed: {ex.Message}");
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        public static async Task CleanupOldBackupsAsync(int maxBackups)
        {
            try
            {
                var backupFolder = GetBackupFolder();
                if (!Directory.Exists(backupFolder))
                    return;

                var directories = Directory.GetDirectories(backupFolder, "backup_*")
                    .OrderByDescending(d => d)
                    .Skip(maxBackups)
                    .ToList();

                foreach (var dir in directories)
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        public static List<string> GetBackupList()
        {
            var backupFolder = GetBackupFolder();
            if (!Directory.Exists(backupFolder))
                return new List<string>();

            return Directory.GetDirectories(backupFolder, "backup_*")
                .OrderByDescending(d => d)
                .Select(d => Path.GetFileName(d))
                .ToList();
        }

        #endregion

        #region App Info

        public static string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }

        public static string GetPluginsDirectory()
        {
            return AppPaths.PluginsDirectory;
        }

        public static string GetDataDirectory()
        {
            return AppPaths.DataRoot;
        }

        #endregion
    }
}
