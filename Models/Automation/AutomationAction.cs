using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AIA.Models.Automation
{
    /// <summary>
    /// Represents an action that an automation can perform
    /// </summary>
    public class AutomationAction : INotifyPropertyChanged
    {
        private Guid _id;
        private ActionType _actionType = ActionType.Notification;
        private string _name = string.Empty;
        private bool _isEnabled = true;
        private bool _requireConfirmation;
        private Dictionary<string, object> _parameters = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique identifier for the action
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// Type of action to perform
        /// </summary>
        public ActionType ActionType
        {
            get => _actionType;
            set 
            { 
                _actionType = value; 
                OnPropertyChanged(nameof(ActionType));
                OnPropertyChanged(nameof(IconSymbol));
            }
        }

        /// <summary>
        /// User-friendly name for this action
        /// </summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// Whether this action is enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Whether to require user confirmation before executing
        /// </summary>
        public bool RequireConfirmation
        {
            get => _requireConfirmation;
            set { _requireConfirmation = value; OnPropertyChanged(nameof(RequireConfirmation)); }
        }

        /// <summary>
        /// Action-specific parameters
        /// </summary>
        public Dictionary<string, object> Parameters
        {
            get => _parameters;
            set { _parameters = value; OnPropertyChanged(nameof(Parameters)); }
        }

        /// <summary>
        /// Icon for the action type
        /// </summary>
        [JsonIgnore]
        public string IconSymbol => ActionType switch
        {
            ActionType.Notification => "Alert20",
            ActionType.CreateTask => "ClipboardTaskAdd20",
            ActionType.CreateReminder => "Lightbulb20",
            ActionType.SaveToDataBank => "Database20",
            ActionType.StoreResult => "Save20",
            ActionType.RunAutomation => "ArrowRepeatAll20",
            ActionType.PluginAction => "PlugConnected20",
            ActionType.CopyToClipboard => "ClipboardPaste20",
            ActionType.SaveToFile => "Document20",
            _ => "Settings20"
        };

        public AutomationAction()
        {
            Id = Guid.NewGuid();
        }

        /// <summary>
        /// Gets a parameter value with type conversion
        /// </summary>
        public T? GetParameter<T>(string key, T? defaultValue = default)
        {
            if (Parameters.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is T typedValue)
                        return typedValue;
                    
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Sets a parameter value
        /// </summary>
        public void SetParameter(string key, object value)
        {
            Parameters[key] = value;
            OnPropertyChanged(nameof(Parameters));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Notification action parameters
    /// </summary>
    public static class NotificationActionParams
    {
        public const string Title = "Title";
        public const string Message = "Message";
        public const string Duration = "Duration";
        public const string PlaySound = "PlaySound";
    }

    /// <summary>
    /// Create task action parameters
    /// </summary>
    public static class CreateTaskActionParams
    {
        public const string Title = "Title";
        public const string Description = "Description";
        public const string Priority = "Priority";
        public const string DueDateOffset = "DueDateOffset";
    }

    /// <summary>
    /// Create reminder action parameters
    /// </summary>
    public static class CreateReminderActionParams
    {
        public const string Title = "Title";
        public const string DueDateOffset = "DueDateOffset";
        public const string Severity = "Severity";
    }

    /// <summary>
    /// Save to data bank action parameters
    /// </summary>
    public static class SaveToDataBankActionParams
    {
        public const string CategoryId = "CategoryId";
        public const string Title = "Title";
        public const string Tags = "Tags";
    }

    /// <summary>
    /// Run automation action parameters
    /// </summary>
    public static class RunAutomationActionParams
    {
        public const string AutomationId = "AutomationId";
        public const string Mode = "Mode";
        public const string PassContext = "PassContext";
    }

    /// <summary>
    /// Plugin action parameters
    /// </summary>
    public static class PluginActionParams
    {
        public const string PluginId = "PluginId";
        public const string ActionId = "ActionId";
        public const string Configuration = "Configuration";
    }

    /// <summary>
    /// Save to file action parameters
    /// </summary>
    public static class SaveToFileActionParams
    {
        public const string FilePath = "FilePath";
        public const string FileName = "FileName";
        public const string AppendMode = "AppendMode";
    }
}
