using System;
using System.ComponentModel;

namespace AIA.Models
{
    /// <summary>
    /// Represents a reusable chat message template
    /// </summary>
    public class ChatMessageTemplate : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title = string.Empty;
        private string _message = string.Empty;
        private string _description = string.Empty;
        private string _icon = "Chat12";
        private string _color = "#0078D4";
        private int _order;
        private bool _isEnabled = true;
        private bool _isPredefined;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Unique identifier for the template
        /// </summary>
        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        /// <summary>
        /// Display title for the template button
        /// </summary>
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        /// <summary>
        /// The message content to send when the template is used
        /// </summary>
        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(nameof(Message)); }
        }

        /// <summary>
        /// Short description/snippet shown on the button
        /// </summary>
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        /// <summary>
        /// Icon name (WPF UI SymbolRegular icon name)
        /// </summary>
        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        /// <summary>
        /// Color for the template button (hex format)
        /// </summary>
        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        /// <summary>
        /// Display order (lower numbers appear first)
        /// </summary>
        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(nameof(Order)); }
        }

        /// <summary>
        /// Whether this template is enabled and visible
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Whether this is a predefined system template
        /// </summary>
        public bool IsPredefined
        {
            get => _isPredefined;
            set { _isPredefined = value; OnPropertyChanged(nameof(IsPredefined)); }
        }

        public ChatMessageTemplate()
        {
            _id = Guid.NewGuid();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Settings for chat message templates
    /// </summary>
    public class ChatTemplateSettings : INotifyPropertyChanged
    {
        private bool _showTemplates = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether to show the templates panel
        /// </summary>
        public bool ShowTemplates
        {
            get => _showTemplates;
            set { _showTemplates = value; OnPropertyChanged(nameof(ShowTemplates)); }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
