using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIA.Models.Automation;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfColor = System.Windows.Media.Color;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AIA.Views.Automation
{
    /// <summary>
    /// Configuration view for Hotkey trigger
    /// </summary>
    public partial class HotkeyTriggerConfigView : WpfUserControl
    {
        private bool _isCapturing;

        public HotkeyTriggerConfigView()
        {
            InitializeComponent();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (!_isCapturing) return;

            e.Handled = true;

            // Get the actual key (handle system keys)
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore modifier-only presses
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Get modifiers
            var modifiers = Keyboard.Modifiers;

            // Update the trigger
            if (DataContext is HotkeyTrigger trigger)
            {
                trigger.Key = key;
                trigger.Modifiers = modifiers;

                UpdateStatus($"Hotkey set: {trigger.HotkeyString}", true);
            }
        }

        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isCapturing = true;
            HotkeyTextBox.Background = new SolidColorBrush(WpfColor.FromRgb(0x40, 0x40, 0x45));
            UpdateStatus("Press your desired key combination...", false);
        }

        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isCapturing = false;
            HotkeyTextBox.Background = new SolidColorBrush(WpfColor.FromRgb(0x2D, 0x2D, 0x30));
            StatusBorder.Visibility = Visibility.Collapsed;
        }

        private void BtnClearHotkey(object sender, RoutedEventArgs e)
        {
            if (DataContext is HotkeyTrigger trigger)
            {
                trigger.Key = Key.None;
                trigger.Modifiers = ModifierKeys.None;
                UpdateStatus("Hotkey cleared", true);
            }
        }

        private void UpdateStatus(string message, bool success)
        {
            StatusBorder.Visibility = Visibility.Visible;
            StatusBorder.Background = new SolidColorBrush(
                success ? WpfColor.FromArgb(0x33, 0x1E, 0xB7, 0x5F) : WpfColor.FromArgb(0x33, 0x00, 0x78, 0xD4));
            StatusText.Text = message;
            StatusText.Foreground = new SolidColorBrush(
                success ? WpfColor.FromRgb(0x1E, 0xB7, 0x5F) : WpfColor.FromRgb(0x00, 0x78, 0xD4));
        }
    }
}
