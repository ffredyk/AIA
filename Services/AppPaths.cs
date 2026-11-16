using System;
using System.IO;
using System.Text.Json;

namespace AIA.Services
{
    /// <summary>
    /// Resolves all application data locations. Data lives under %LOCALAPPDATA%\AIA by default
    /// (the install directory is often read-only), with a one-time migration from the legacy
    /// install-directory layout and an optional custom root from settings.
    /// </summary>
    public static class AppPaths
    {
        public const string AppFolderName = "AIA";

        private const string MigrationMarker = ".migrated";
        private const string StoragePathPointer = "data-path.txt";
        private static string? _dataRoot;

        /// <summary>
        /// Directory containing the application binaries (plugins are still loaded from here).
        /// </summary>
        public static string InstallDirectory => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Default writable data root: %LOCALAPPDATA%\AIA
        /// </summary>
        public static string DefaultDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

        /// <summary>
        /// Effective data root (default root or a custom path from settings).
        /// </summary>
        public static string DataRoot => _dataRoot ?? DefaultDataRoot;

        public static string SettingsFolder => Path.Combine(DataRoot, "settings");
        public static string UserDataFolder => Path.Combine(DataRoot, "userdata");
        public static string DataBanksFolder => Path.Combine(DataRoot, "databanks");
        public static string DataBankFilesFolder => Path.Combine(DataBanksFolder, "files");
        public static string ScreenshotsFolder => Path.Combine(DataRoot, "screenshots");
        public static string AutomationsFolder => Path.Combine(DataRoot, "automations");
        public static string AutomationHistoryFolder => Path.Combine(AutomationsFolder, "history");
        public static string AiConfigFolder => Path.Combine(DataRoot, "ai-config");
        public static string BackupsFolder => Path.Combine(DataRoot, "backups");

        /// <summary>
        /// Plugin assemblies are loaded from the install directory.
        /// </summary>
        public static string PluginsDirectory => Path.Combine(InstallDirectory, "Plugins");

        /// <summary>
        /// Writable plugin configuration/data root.
        /// </summary>
        public static string PluginDataFolder => Path.Combine(DataRoot, "Plugins");

        private static readonly string[] LegacyFolders =
        {
            "settings", "userdata", "databanks", "ai-config", "automations", "screenshots", "backups", "chats"
        };

        /// <summary>
        /// Prepares the data root: migrates legacy install-directory data, applies a custom
        /// storage path from settings, and creates the directory tree.
        /// </summary>
        public static void Initialize(Action<string>? log = null)
        {
            try
            {
                Directory.CreateDirectory(DefaultDataRoot);
                TryMigrateLegacyData(log);
                ApplyStoragePathPointer(log);
                ApplyDataStorageOverride(log);
                EnsureDirectories();
            }
            catch (Exception ex)
            {
                log?.Invoke($"Failed to initialize data directories: {ex.Message}");
            }
        }

        public static void EnsureDirectories()
        {
            foreach (var path in new[]
            {
                DataRoot, SettingsFolder, UserDataFolder, DataBanksFolder, DataBankFilesFolder,
                ScreenshotsFolder, AutomationsFolder, AutomationHistoryFolder, AiConfigFolder,
                BackupsFolder, PluginDataFolder
            })
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create directory '{path}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Copies data from the legacy install-directory layout to the default root. Copies only
        /// files that do not already exist and never deletes anything.
        /// </summary>
        public static bool TryMigrateLegacyData(Action<string>? log = null)
        {
            var marker = Path.Combine(DefaultDataRoot, MigrationMarker);
            if (File.Exists(marker))
                return false;

            var migratedAnything = false;

            try
            {
                foreach (var folder in LegacyFolders)
                {
                    var source = Path.Combine(InstallDirectory, folder);
                    if (!Directory.Exists(source))
                        continue;

                    var destination = Path.Combine(DefaultDataRoot, folder);
                    CopyDirectoryMissing(source, destination);
                    migratedAnything = true;
                }

                // Legacy template files were stored next to the executable.
                var legacyTaskTemplates = Path.Combine(InstallDirectory, "task-templates.json");
                if (File.Exists(legacyTaskTemplates))
                {
                    var destination = Path.Combine(DefaultDataRoot, "settings", "task-templates.json");
                    if (!File.Exists(destination))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(legacyTaskTemplates, destination, false);
                        migratedAnything = true;
                    }
                }

                // Plugin configuration previously lived inside the plugins directory.
                var legacyPluginConfig = Path.Combine(InstallDirectory, "Plugins", "plugins.config.json");
                if (File.Exists(legacyPluginConfig))
                {
                    var destination = Path.Combine(DefaultDataRoot, "Plugins", "plugins.config.json");
                    if (!File.Exists(destination))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(legacyPluginConfig, destination, false);
                        migratedAnything = true;
                    }
                }

                var legacyPluginData = Path.Combine(InstallDirectory, "Plugins", "Data");
                if (Directory.Exists(legacyPluginData))
                {
                    CopyDirectoryMissing(legacyPluginData, Path.Combine(DefaultDataRoot, "Plugins", "Data"));
                    migratedAnything = true;
                }

                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
                log?.Invoke(migratedAnything
                    ? $"Migrated legacy application data to '{DefaultDataRoot}'."
                    : $"No legacy data found; initialized '{DefaultDataRoot}'.");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Legacy data migration failed: {ex.Message}");
            }

            return migratedAnything;
        }

        private static void CopyDirectoryMissing(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destination = Path.Combine(destinationDir, Path.GetFileName(file));
                if (!File.Exists(destination))
                    File.Copy(file, destination, false);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryMissing(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
            }
        }

        /// <summary>
        /// Applies the optional DataStoragePath setting. The setting is read without using
        /// AppSettingsService so it can run before the services are used.
        /// </summary>
        private static void ApplyDataStorageOverride(Action<string>? log)
        {
            try
            {
                var settingsFile = Path.Combine(DataRoot, "settings", "app-settings.json");
                if (!File.Exists(settingsFile))
                    return;

                using var document = JsonDocument.Parse(File.ReadAllText(settingsFile));
                if (document.RootElement.TryGetProperty(nameof(Models.AppSettings.DataStoragePath), out var element) &&
                    element.ValueKind == JsonValueKind.String)
                {
                    SetDataStoragePath(element.GetString(), log);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not read custom data storage path: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the pointer file that records where the user's settings live. This must be
        /// checked before any settings file is read, otherwise a custom root would be forgotten.
        /// </summary>
        private static void ApplyStoragePathPointer(Action<string>? log)
        {
            try
            {
                var pointerFile = Path.Combine(DefaultDataRoot, StoragePathPointer);
                if (!File.Exists(pointerFile))
                    return;

                var path = File.ReadAllText(pointerFile).Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    SetDataStoragePath(path, log);
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Could not read the data storage pointer: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies the existing data folders to a new root so switching storage locations keeps
        /// the user's data. Existing files at the destination are never overwritten.
        /// </summary>
        public static void CopyDataTo(string destination)
        {
            var destinationRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination.Trim()));
            Directory.CreateDirectory(destinationRoot);

            foreach (var folder in new[] { "settings", "userdata", "databanks", "ai-config", "automations", "screenshots", "Plugins" })
            {
                var source = Path.Combine(DataRoot, folder);
                if (Directory.Exists(source))
                {
                    CopyDirectoryMissing(source, Path.Combine(destinationRoot, folder));
                }
            }
        }

        /// <summary>
        /// Records the custom data storage path so it is restored on the next launch.
        /// </summary>
        public static void SaveDataStoragePath(string? path)
        {
            try
            {
                Directory.CreateDirectory(DefaultDataRoot);
                var pointerFile = Path.Combine(DefaultDataRoot, StoragePathPointer);

                if (string.IsNullOrWhiteSpace(path))
                {
                    if (File.Exists(pointerFile))
                        File.Delete(pointerFile);
                    return;
                }

                File.WriteAllText(pointerFile, Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to persist data storage path: {ex.Message}");
            }
        }

        /// <summary>
        /// Overrides the data root if a valid path is provided.
        /// </summary>
        public static void SetDataStoragePath(string? path, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _dataRoot = null;
                return;
            }

            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
                var full = Path.GetFullPath(expanded);
                Directory.CreateDirectory(full);
                _dataRoot = full;
                log?.Invoke($"Using custom data storage path '{full}'.");
            }
            catch (Exception ex)
            {
                _dataRoot = null;
                log?.Invoke($"Invalid data storage path '{path}', using default: {ex.Message}");
            }
        }
    }
}
