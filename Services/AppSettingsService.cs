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
        private static readonly string SettingsFolder;
        private static readonly string AppSettingsFile;
        private static readonly string PluginSettingsFile;
        private static readonly string PluginInstanceSettingsFile;

        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AIA";

        static AppSettingsService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            SettingsFolder = Path.Combine(exeDirectory, "settings");
            AppSettingsFile = Path.Combine(SettingsFolder, "app-settings.json");
            PluginSettingsFile = Path.Combine(SettingsFolder, "plugin-settings.json");
            PluginInstanceSettingsFile = Path.Combine(SettingsFolder, "plugin-instance-settings.json");
        }

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
                var json = await File.ReadAllTextAsync(AppSettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static async Task SaveAppSettingsAsync(AppSettings settings)
        {
            EnsureDirectoryExists();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(AppSettingsFile, json);
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
                var json = await File.ReadAllTextAsync(PluginSettingsFile);
                var settings = JsonSerializer.Deserialize<PluginSettings>(json);
                return settings ?? new PluginSettings();
            }
            catch
            {
                return new PluginSettings();
            }
        }

        public static async Task SavePluginSettingsAsync(PluginSettings settings)
        {
            EnsureDirectoryExists();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(PluginSettingsFile, json);
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
                var json = await File.ReadAllTextAsync(PluginInstanceSettingsFile);
                var settings = JsonSerializer.Deserialize<List<PluginInstanceSettings>>(json);
                return settings ?? new List<PluginInstanceSettings>();
            }
            catch
            {
                return new List<PluginInstanceSettings>();
            }
        }

        public static async Task SavePluginInstanceSettingsAsync(List<PluginInstanceSettings> settings)
        {
            EnsureDirectoryExists();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(PluginInstanceSettingsFile, json);
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
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exeDirectory, "backups");
        }

        public static async Task<bool> CreateBackupAsync()
        {
            try
            {
                var backupFolder = GetBackupFolder();
                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var backupPath = Path.Combine(backupFolder, $"backup_{timestamp}");
                Directory.CreateDirectory(backupPath);

                var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;

                // Copy data files
                var dataBanksFolder = Path.Combine(exeDirectory, "databanks");
                if (Directory.Exists(dataBanksFolder))
                {
                    CopyDirectory(dataBanksFolder, Path.Combine(backupPath, "databanks"));
                }

                // Copy tasks and reminders
                var tasksFile = Path.Combine(exeDirectory, "tasks.json");
                if (File.Exists(tasksFile))
                {
                    File.Copy(tasksFile, Path.Combine(backupPath, "tasks.json"));
                }

                var remindersFile = Path.Combine(exeDirectory, "reminders.json");
                if (File.Exists(remindersFile))
                {
                    File.Copy(remindersFile, Path.Combine(backupPath, "reminders.json"));
                }

                // Copy chat sessions
                var chatsFolder = Path.Combine(exeDirectory, "chats");
                if (Directory.Exists(chatsFolder))
                {
                    CopyDirectory(chatsFolder, Path.Combine(backupPath, "chats"));
                }

                // Copy settings
                if (Directory.Exists(SettingsFolder))
                {
                    CopyDirectory(SettingsFolder, Path.Combine(backupPath, "settings"));
                }

                return true;
            }
            catch
            {
                return false;
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
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        }

        public static string GetDataDirectory()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        #endregion
    }
}
