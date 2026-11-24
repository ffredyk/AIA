using System;
using System.ComponentModel;

namespace AIA.Models
{
    /// <summary>
    /// Settings for the always-on compact task list overlay.
    /// </summary>
    public class MiniTaskOverlaySettings : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private string _shortcut = "Alt+Shift+Q";
        private bool _drawerMode;
        private bool _interactableMode;
        private bool _relocateMode;
        private double _opacity = 0.9;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Whether the compact task list overlay is shown at all.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// Global shortcut that toggles interactable mode.
        /// </summary>
        public string Shortcut
        {
            get => _shortcut;
            set { _shortcut = value ?? string.Empty; OnPropertyChanged(nameof(Shortcut)); }
        }

        /// <summary>
        /// When enabled, only a small ball is shown until the cursor approaches it.
        /// </summary>
        public bool DrawerMode
        {
            get => _drawerMode;
            set { _drawerMode = value; OnPropertyChanged(nameof(DrawerMode)); }
        }

        /// <summary>
        /// When enabled, the overlay stays visible and accepts clicks.
        /// </summary>
        public bool InteractableMode
        {
            get => _interactableMode;
            set { _interactableMode = value; OnPropertyChanged(nameof(InteractableMode)); }
        }

        /// <summary>
        /// When enabled, the overlay collapses into a ball and moves to the opposite screen side instead of fading.
        /// </summary>
        public bool RelocateMode
        {
            get => _relocateMode;
            set { _relocateMode = value; OnPropertyChanged(nameof(RelocateMode)); }
        }

        /// <summary>
        /// Opacity of the overlay (0.2 to 1.0).
        /// </summary>
        public double Opacity
        {
            get => _opacity;
            set
            {
                _opacity = Math.Max(0.2, Math.Min(1.0, value));
                OnPropertyChanged(nameof(Opacity));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
