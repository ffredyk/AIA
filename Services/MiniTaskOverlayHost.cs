using System;
using System.Windows;
using System.Windows.Input;
using AIA.Models;
using AIA.Views;
using NHotkey;
using NHotkey.Wpf;

namespace AIA.Services
{
    /// <summary>
    /// Owns the always-on compact task list window and its global shortcut.
    /// </summary>
    public sealed class MiniTaskOverlayHost : IDisposable
    {
        private const string HotkeyName = "AIA.ToggleMiniTaskOverlayInteractable";

        private MiniTaskOverlayWindow? _window;
        private MiniTaskOverlaySettings? _settings;
        private bool _isHotkeyRegistered;
        private bool _isDisposed;

        /// <summary>
        /// Raised when the configured shortcut cannot be registered.
        /// </summary>
        public event EventHandler<string>? HotkeyRegistrationFailed;

        public bool IsEnabled => _settings?.IsEnabled == true;

        public bool IsInteractable => _settings?.InteractableMode == true;

        public void Initialize(AppSettings settings)
        {
            _settings = settings?.MiniTaskOverlay ?? new MiniTaskOverlaySettings();
            ApplyCurrentSettings(registerHotkey: true);
        }

        /// <summary>
        /// Applies settings changed in the settings window.
        /// </summary>
        public void ApplySettings(AppSettings settings)
        {
            if (settings == null || _isDisposed)
                return;

            _settings = settings.MiniTaskOverlay ?? new MiniTaskOverlaySettings();
            ApplyCurrentSettings(registerHotkey: true);
        }

        /// <summary>
        /// Registers a new shortcut for toggling interactable mode.
        /// </summary>
        public bool UpdateHotkey(string shortcut)
        {
            if (_isDisposed || _settings == null)
                return false;

            _settings.Shortcut = shortcut ?? string.Empty;
            return RegisterHotkey(_settings.Shortcut);
        }

        /// <summary>
        /// Temporarily releases the shortcut so it can be captured by the settings window.
        /// </summary>
        public void SuspendHotkey()
        {
            UnregisterHotkey();
        }

        /// <summary>
        /// Re-registers the configured shortcut.
        /// </summary>
        public void ResumeHotkey()
        {
            if (_isDisposed || _settings == null || !_settings.IsEnabled)
                return;

            RegisterHotkey(_settings.Shortcut);
        }

        /// <summary>
        /// Toggles between passive and interactable mode.
        /// </summary>
        public void ToggleInteractable()
        {
            if (_isDisposed || _settings == null || !_settings.IsEnabled)
                return;

            _settings.InteractableMode = !_settings.InteractableMode;

            EnsureWindow();
            ShowWindow();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            UnregisterHotkey();

            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
        }

        private void ApplyCurrentSettings(bool registerHotkey)
        {
            if (_settings == null || _isDisposed)
                return;

            if (!_settings.IsEnabled)
            {
                UnregisterHotkey();
                _window?.Hide();
                return;
            }

            EnsureWindow();
            _window!.ApplySettings(_settings);
            ShowWindow();

            if (registerHotkey)
                RegisterHotkey(_settings.Shortcut);
        }

        private void EnsureWindow()
        {
            if (_window != null)
                return;

            _window = new MiniTaskOverlayWindow();
            _window.ApplySettings(_settings ?? new MiniTaskOverlaySettings());
        }

        private void ShowWindow()
        {
            if (_window == null)
                return;

            if (!_window.IsVisible)
                _window.Show();
            else if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
        }

        private bool RegisterHotkey(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                UnregisterHotkey();
                return true;
            }

            var (modifiers, key) = HotkeyService.ParseHotkeyString(shortcut);
            if (key == Key.None)
            {
                UnregisterHotkey();
                return false;
            }

            try
            {
                HotkeyManager.Current.AddOrReplace(HotkeyName, key, modifiers, OnHotkeyPressed);
                _isHotkeyRegistered = true;
                return true;
            }
            catch (Exception ex)
            {
                _isHotkeyRegistered = false;
                System.Diagnostics.Debug.WriteLine($"Failed to register mini task overlay hotkey '{shortcut}': {ex.Message}");
                HotkeyRegistrationFailed?.Invoke(this, shortcut);
                return false;
            }
        }

        private void UnregisterHotkey()
        {
            if (!_isHotkeyRegistered)
                return;

            try
            {
                HotkeyManager.Current.Remove(HotkeyName);
            }
            catch
            {
                // Ignore removal errors during shutdown.
            }

            _isHotkeyRegistered = false;
        }

        private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
        {
            e.Handled = true;

            if (WindowsIntegrationService.Current.ShouldSuppressHotkey(HotkeyPurpose.Overlay))
                return;

            ToggleInteractable();
        }
    }
}
