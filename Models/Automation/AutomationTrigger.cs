using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Base class for automation triggers
    /// </summary>
    public abstract class AutomationTrigger : INotifyPropertyChanged
    {
        private Guid _id;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private bool _isEnabled = true;
        private int _debounceMsec = 500;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique identifier for the trigger
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// User-friendly name for the trigger
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// Description of what this trigger does
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        /// <summary>
        /// Whether this trigger is active
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Debounce time in milliseconds for rapid triggers
        /// </summary>
        public int DebounceMsec
        {
            get => _debounceMsec;
            set { _debounceMsec = value; OnPropertyChanged(nameof(DebounceMsec)); }
        }

        /// <summary>
        /// The type of trigger
        /// </summary>
        [JsonIgnore]
        public abstract TriggerType TriggerType { get; }

        /// <summary>
        /// Icon symbol for the trigger type
        /// </summary>
        [JsonIgnore]
        public abstract string IconSymbol { get; }

        protected AutomationTrigger()
        {
            Id = Guid.NewGuid();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Trigger that fires when clipboard content changes
    /// </summary>
    public class ClipboardTrigger : AutomationTrigger
    {
        private ClipboardContentType _contentType = ClipboardContentType.All;
        private string _textFilter = string.Empty;
        private bool _useRegex;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.Clipboard;

        [JsonIgnore]
        public override string IconSymbol => "ClipboardPaste20";

        /// <summary>
        /// Content types to trigger on
        /// </summary>
        public ClipboardContentType ContentType
        {
            get => _contentType;
            set { _contentType = value; OnPropertyChanged(nameof(ContentType)); }
        }

        /// <summary>
        /// Text pattern filter (keywords or regex)
        /// </summary>
        public string TextFilter
        {
            get => _textFilter;
            set { _textFilter = value; OnPropertyChanged(nameof(TextFilter)); }
        }

        /// <summary>
        /// Whether to use regex for text filtering
        /// </summary>
        public bool UseRegex
        {
            get => _useRegex;
            set { _useRegex = value; OnPropertyChanged(nameof(UseRegex)); }
        }
    }

    /// <summary>
    /// Trigger that fires when a hotkey is pressed
    /// </summary>
    public class HotkeyTrigger : AutomationTrigger
    {
        private ModifierKeys _modifiers = ModifierKeys.None;
        private Key _key = Key.None;
        private string _hotkeyString = string.Empty;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.Hotkey;

        [JsonIgnore]
        public override string IconSymbol => "Keyboard20";

        /// <summary>
        /// Modifier keys (Ctrl, Alt, Shift, Win)
        /// </summary>
        public ModifierKeys Modifiers
        {
            get => _modifiers;
            set 
            { 
                _modifiers = value; 
                OnPropertyChanged(nameof(Modifiers));
                UpdateHotkeyString();
            }
        }

        /// <summary>
        /// The main key
        /// </summary>
        public Key Key
        {
            get => _key;
            set 
            { 
                _key = value; 
                OnPropertyChanged(nameof(Key));
                UpdateHotkeyString();
            }
        }

        /// <summary>
        /// Display string for the hotkey combination
        /// </summary>
        public string HotkeyString
        {
            get => _hotkeyString;
            set { _hotkeyString = value; OnPropertyChanged(nameof(HotkeyString)); }
        }

        private void UpdateHotkeyString()
        {
            HotkeyString = Services.HotkeyService.FormatHotkey(_modifiers, _key);
        }
    }

    /// <summary>
    /// Trigger that fires when files change in watched locations
    /// </summary>
    public class FileChangeTrigger : AutomationTrigger
    {
        private string _watchPath = string.Empty;
        private string _fileFilter = "*.*";
        private bool _includeSubdirectories = true;
        private FileChangeType _changeTypes = FileChangeType.All;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.FileChange;

        [JsonIgnore]
        public override string IconSymbol => "FolderOpen20";

        /// <summary>
        /// Path to watch for changes
        /// </summary>
        public string WatchPath
        {
            get => _watchPath;
            set { _watchPath = value; OnPropertyChanged(nameof(WatchPath)); }
        }

        /// <summary>
        /// File filter pattern (e.g., "*.json", "*.txt")
        /// </summary>
        public string FileFilter
        {
            get => _fileFilter;
            set { _fileFilter = value; OnPropertyChanged(nameof(FileFilter)); }
        }

        /// <summary>
        /// Whether to include subdirectories
        /// </summary>
        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set { _includeSubdirectories = value; OnPropertyChanged(nameof(IncludeSubdirectories)); }
        }

        /// <summary>
        /// Types of file changes to trigger on
        /// </summary>
        public FileChangeType ChangeTypes
        {
            get => _changeTypes;
            set 
            { 
                _changeTypes = value; 
                OnPropertyChanged(nameof(ChangeTypes));
                OnPropertyChanged(nameof(IsCreatedChecked));
                OnPropertyChanged(nameof(IsModifiedChecked));
                OnPropertyChanged(nameof(IsDeletedChecked));
                OnPropertyChanged(nameof(IsRenamedChecked));
            }
        }

        // Helper properties for checkbox bindings
        [JsonIgnore]
        public bool IsCreatedChecked
        {
            get => (_changeTypes & FileChangeType.Created) != 0;
            set => ChangeTypes = value 
                ? _changeTypes | FileChangeType.Created 
                : _changeTypes & ~FileChangeType.Created;
        }

        [JsonIgnore]
        public bool IsModifiedChecked
        {
            get => (_changeTypes & FileChangeType.Modified) != 0;
            set => ChangeTypes = value 
                ? _changeTypes | FileChangeType.Modified 
                : _changeTypes & ~FileChangeType.Modified;
        }

        [JsonIgnore]
        public bool IsDeletedChecked
        {
            get => (_changeTypes & FileChangeType.Deleted) != 0;
            set => ChangeTypes = value 
                ? _changeTypes | FileChangeType.Deleted 
                : _changeTypes & ~FileChangeType.Deleted;
        }

        [JsonIgnore]
        public bool IsRenamedChecked
        {
            get => (_changeTypes & FileChangeType.Renamed) != 0;
            set => ChangeTypes = value 
                ? _changeTypes | FileChangeType.Renamed 
                : _changeTypes & ~FileChangeType.Renamed;
        }
    }

    /// <summary>
    /// Trigger that fires when active window changes
    /// </summary>
    public class WindowContextTrigger : AutomationTrigger
    {
        private string _windowTitleFilter = string.Empty;
        private string _processNameFilter = string.Empty;
        private bool _useRegex;
        private bool _triggerOnMatch = true;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.WindowContext;

        [JsonIgnore]
        public override string IconSymbol => "AppGeneric20";

        /// <summary>
        /// Filter for window title (keywords or regex)
        /// </summary>
        public string WindowTitleFilter
        {
            get => _windowTitleFilter;
            set { _windowTitleFilter = value; OnPropertyChanged(nameof(WindowTitleFilter)); }
        }

        /// <summary>
        /// Filter for process name
        /// </summary>
        public string ProcessNameFilter
        {
            get => _processNameFilter;
            set { _processNameFilter = value; OnPropertyChanged(nameof(ProcessNameFilter)); }
        }

        /// <summary>
        /// Whether to use regex for filtering
        /// </summary>
        public bool UseRegex
        {
            get => _useRegex;
            set { _useRegex = value; OnPropertyChanged(nameof(UseRegex)); }
        }

        /// <summary>
        /// True = trigger when filter matches, False = trigger when filter doesn't match
        /// </summary>
        public bool TriggerOnMatch
        {
            get => _triggerOnMatch;
            set { _triggerOnMatch = value; OnPropertyChanged(nameof(TriggerOnMatch)); }
        }
    }

    /// <summary>
    /// Trigger provided by a plugin
    /// </summary>
    public class PluginTrigger : AutomationTrigger
    {
        private string _pluginId = string.Empty;
        private string _triggerId = string.Empty;
        private Dictionary<string, object> _configuration = new();

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.Plugin;

        [JsonIgnore]
        public override string IconSymbol => "PlugConnected20";

        /// <summary>
        /// ID of the plugin providing this trigger
        /// </summary>
        public string PluginId
        {
            get => _pluginId;
            set { _pluginId = value; OnPropertyChanged(nameof(PluginId)); }
        }

        /// <summary>
        /// ID of the trigger within the plugin
        /// </summary>
        public string TriggerId
        {
            get => _triggerId;
            set { _triggerId = value; OnPropertyChanged(nameof(TriggerId)); }
        }

        /// <summary>
        /// Plugin-specific configuration
        /// </summary>
        public Dictionary<string, object> Configuration
        {
            get => _configuration;
            set { _configuration = value; OnPropertyChanged(nameof(Configuration)); }
        }
    }

    /// <summary>
    /// Trigger that fires when another automation completes
    /// </summary>
    public class AutomationChainTrigger : AutomationTrigger
    {
        private Guid _sourceAutomationId;
        private bool _requireSuccess = true;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.AutomationChain;

        [JsonIgnore]
        public override string IconSymbol => "ArrowRepeatAll20";

        /// <summary>
        /// ID of the automation that triggers this one
        /// </summary>
        public Guid SourceAutomationId
        {
            get => _sourceAutomationId;
            set { _sourceAutomationId = value; OnPropertyChanged(nameof(SourceAutomationId)); }
        }

        /// <summary>
        /// Whether to require the source automation to succeed
        /// </summary>
        public bool RequireSuccess
        {
            get => _requireSuccess;
            set { _requireSuccess = value; OnPropertyChanged(nameof(RequireSuccess)); }
        }
    }

    /// <summary>
    /// Time-based scheduled trigger
    /// </summary>
    public class ScheduleTrigger : AutomationTrigger
    {
        private DateTime? _scheduledTime;
        private RecurrenceType _recurrence = RecurrenceType.None;
        private int _recurrenceInterval = 1;

        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.Schedule;

        [JsonIgnore]
        public override string IconSymbol => "Clock20";

        /// <summary>
        /// Scheduled execution time
        /// </summary>
        public DateTime? ScheduledTime
        {
            get => _scheduledTime;
            set 
            { 
                _scheduledTime = value; 
                OnPropertyChanged(nameof(ScheduledTime));
                OnPropertyChanged(nameof(ScheduledDate));
                OnPropertyChanged(nameof(ScheduledHour));
                OnPropertyChanged(nameof(ScheduledMinute));
            }
        }

        /// <summary>
        /// Recurrence pattern
        /// </summary>
        public RecurrenceType Recurrence
        {
            get => _recurrence;
            set 
            { 
                _recurrence = value; 
                OnPropertyChanged(nameof(Recurrence));
                OnPropertyChanged(nameof(RecurrenceIndex));
            }
        }

        /// <summary>
        /// Interval for recurrence
        /// </summary>
        public int RecurrenceInterval
        {
            get => _recurrenceInterval;
            set { _recurrenceInterval = value; OnPropertyChanged(nameof(RecurrenceInterval)); }
        }

        // Helper properties for UI binding
        [JsonIgnore]
        public DateTime? ScheduledDate
        {
            get => _scheduledTime?.Date;
            set
            {
                if (value.HasValue)
                {
                    var time = _scheduledTime?.TimeOfDay ?? TimeSpan.Zero;
                    ScheduledTime = value.Value.Date + time;
                }
                else
                {
                    ScheduledTime = null;
                }
            }
        }

        [JsonIgnore]
        public int ScheduledHour
        {
            get => _scheduledTime?.Hour ?? 0;
            set
            {
                if (_scheduledTime.HasValue)
                {
                    ScheduledTime = new DateTime(_scheduledTime.Value.Year, _scheduledTime.Value.Month, 
                        _scheduledTime.Value.Day, Math.Clamp(value, 0, 23), _scheduledTime.Value.Minute, 0);
                }
                else
                {
                    ScheduledTime = DateTime.Today.AddHours(Math.Clamp(value, 0, 23));
                }
            }
        }

        [JsonIgnore]
        public int ScheduledMinute
        {
            get => _scheduledTime?.Minute ?? 0;
            set
            {
                if (_scheduledTime.HasValue)
                {
                    ScheduledTime = new DateTime(_scheduledTime.Value.Year, _scheduledTime.Value.Month,
                        _scheduledTime.Value.Day, _scheduledTime.Value.Hour, Math.Clamp(value, 0, 59), 0);
                }
                else
                {
                    ScheduledTime = DateTime.Today.AddMinutes(Math.Clamp(value, 0, 59));
                }
            }
        }

        [JsonIgnore]
        public int RecurrenceIndex
        {
            get => (int)_recurrence;
            set
            {
                Recurrence = (RecurrenceType)value;
            }
        }
    }

    /// <summary>
    /// Manual trigger (user-initiated)
    /// </summary>
    public class ManualTrigger : AutomationTrigger
    {
        [JsonIgnore]
        public override TriggerType TriggerType => TriggerType.Manual;

        [JsonIgnore]
        public override string IconSymbol => "HandLeft20";
    }
}
