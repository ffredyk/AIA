using System;
using System.ComponentModel;
using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;

namespace AIA.Services
{
    /// <summary>
    /// Service for managing global hotkeys using NHotkey library
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private const string OverlayHotkeyName = "AIA.ToggleOverlay";
        
        private ModifierKeys _currentModifiers = ModifierKeys.Windows;
        private Key _currentKey = Key.Q;
        private bool _isRegistered;

        /// <summary>
        /// Event fired when the overlay hotkey is pressed
        /// </summary>
        public event EventHandler<HotkeyEventArgs>? OverlayHotkeyPressed;

        /// <summary>
        /// Gets whether the hotkey is currently registered
        /// </summary>
        public bool IsRegistered => _isRegistered;

        /// <summary>
        /// Gets the current hotkey as a display string
        /// </summary>
        public string CurrentHotkeyString => FormatHotkey(_currentModifiers, _currentKey);

        /// <summary>
        /// Registers the overlay toggle hotkey from a string like "Win+Q"
        /// </summary>
        public bool RegisterHotkey(string hotkeyString)
        {
            var (modifiers, key) = ParseHotkeyString(hotkeyString);
            return RegisterHotkey(modifiers, key);
        }

        /// <summary>
        /// Registers the overlay toggle hotkey with specific modifiers and key
        /// </summary>
        public bool RegisterHotkey(ModifierKeys modifiers, Key key)
        {
            // Validate input
            if (key == Key.None)
                return false;

            // Unregister existing hotkey if any
            UnregisterHotkey();

            try
            {
                HotkeyManager.Current.AddOrReplace(OverlayHotkeyName, key, modifiers, OnOverlayHotkeyPressed);
                _currentModifiers = modifiers;
                _currentKey = key;
                _isRegistered = true;
                return true;
            }
            catch (HotkeyAlreadyRegisteredException)
            {
                // Another application has registered this hotkey
                _isRegistered = false;
                return false;
            }
            catch (Exception)
            {
                _isRegistered = false;
                return false;
            }
        }

        /// <summary>
        /// Unregisters the current hotkey
        /// </summary>
        public void UnregisterHotkey()
        {
            try
            {
                HotkeyManager.Current.Remove(OverlayHotkeyName);
            }
            catch
            {
                // Ignore errors when removing non-existent hotkey
            }
            _isRegistered = false;
        }

        /// <summary>
        /// Tests if a hotkey combination can be registered without actually registering it permanently
        /// </summary>
        public bool TestHotkey(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None)
                return false;

            const string testHotkeyName = "AIA.TestHotkey";

            try
            {
                HotkeyManager.Current.AddOrReplace(testHotkeyName, key, modifiers, (s, e) => { });
                HotkeyManager.Current.Remove(testHotkeyName);
                return true;
            }
            catch
            {
                try
                {
                    HotkeyManager.Current.Remove(testHotkeyName);
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Parses a hotkey string like "Win+Q" or "Ctrl+Shift+A" into modifiers and key
        /// </summary>
        public static (ModifierKeys modifiers, Key key) ParseHotkeyString(string hotkeyString)
        {
            var modifiers = ModifierKeys.None;
            var key = Key.None;

            if (string.IsNullOrWhiteSpace(hotkeyString))
                return (modifiers, key);

            var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                var upperPart = part.ToUpperInvariant();

                switch (upperPart)
                {
                    case "WIN":
                    case "WINDOWS":
                    case "LWIN":
                    case "RWIN":
                        modifiers |= ModifierKeys.Windows;
                        break;
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= ModifierKeys.Control;
                        break;
                    case "ALT":
                        modifiers |= ModifierKeys.Alt;
                        break;
                    case "SHIFT":
                        modifiers |= ModifierKeys.Shift;
                        break;
                    default:
                        // Try to parse as a Key enum
                        if (Enum.TryParse<Key>(part, true, out var parsedKey))
                        {
                            key = parsedKey;
                        }
                        else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                        {
                            // Single character - try to map to key
                            var charUpper = char.ToUpperInvariant(part[0]);
                            if (charUpper >= 'A' && charUpper <= 'Z')
                            {
                                key = (Key)(Key.A + (charUpper - 'A'));
                            }
                            else if (charUpper >= '0' && charUpper <= '9')
                            {
                                key = (Key)(Key.D0 + (charUpper - '0'));
                            }
                        }
                        break;
                }
            }

            return (modifiers, key);
        }

        /// <summary>
        /// Formats modifiers and key into a display string like "Win + Q"
        /// </summary>
        public static string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");
            if (modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");

            if (key != Key.None)
            {
                var keyString = key switch
                {
                    >= Key.A and <= Key.Z => key.ToString(),
                    >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                    >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + (key - Key.NumPad0),
                    >= Key.F1 and <= Key.F24 => key.ToString(),
                    Key.Space => "Space",
                    Key.Tab => "Tab",
                    Key.Enter => "Enter",
                    Key.Escape => "Esc",
                    Key.Back => "Backspace",
                    Key.Delete => "Delete",
                    Key.Insert => "Insert",
                    Key.Home => "Home",
                    Key.End => "End",
                    Key.PageUp => "PageUp",
                    Key.PageDown => "PageDown",
                    Key.Left => "Left",
                    Key.Right => "Right",
                    Key.Up => "Up",
                    Key.Down => "Down",
                    Key.PrintScreen => "PrintScreen",
                    Key.Pause => "Pause",
                    Key.OemTilde => "`",
                    Key.OemMinus => "-",
                    Key.OemPlus => "=",
                    Key.OemOpenBrackets => "[",
                    Key.OemCloseBrackets => "]",
                    Key.OemPipe => "\\",
                    Key.OemSemicolon => ";",
                    Key.OemQuotes => "'",
                    Key.OemComma => ",",
                    Key.OemPeriod => ".",
                    Key.OemQuestion => "/",
                    _ => key.ToString()
                };
                parts.Add(keyString);
            }

            return string.Join(" + ", parts);
        }

        /// <summary>
        /// Determines if a key is a modifier key
        /// </summary>
        public static bool IsModifierKey(Key key)
        {
            return key switch
            {
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or
                Key.LWin or Key.RWin or
                Key.System => true,
                _ => false
            };
        }

        /// <summary>
        /// Converts keyboard modifier keys to ModifierKeys enum
        /// </summary>
        public static ModifierKeys GetModifiersFromKeyboard()
        {
            var modifiers = ModifierKeys.None;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                modifiers |= ModifierKeys.Control;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                modifiers |= ModifierKeys.Alt;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                modifiers |= ModifierKeys.Shift;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= ModifierKeys.Windows;

            return modifiers;
        }

        private void OnOverlayHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            OverlayHotkeyPressed?.Invoke(this, e);
            e.Handled = true;
        }

        public void Dispose()
        {
            UnregisterHotkey();
        }
    }
}
