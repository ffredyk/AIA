using System;

namespace AIA.Models
{
    /// <summary>
    /// How the overlay hotkey should behave when a protected app has focus.
    /// </summary>
    public enum ProtectedAppHotkeyAction
    {
        /// <summary>
        /// Do nothing and keep the protected app focused.
        /// </summary>
        Ignore = 0,

        /// <summary>
        /// Show the overlay without activating it, then restore focus to the protected app.
        /// </summary>
        ShowWithoutActivation = 1
    }

    /// <summary>
    /// Settings for focus-aware Windows integration.
    /// </summary>
    public class WindowsIntegrationSettings
    {
        /// <summary>
        /// Whether AIA should avoid stealing focus from protected apps.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether fullscreen apps and games should be protected.
        /// </summary>
        public bool ProtectFullscreenAndGames { get; set; } = true;

        /// <summary>
        /// Whether known presentation, remote desktop, and meeting apps should be protected.
        /// </summary>
        public bool ProtectKnownApps { get; set; } = true;

        /// <summary>
        /// Whether automation trigger hotkeys should use the same protection.
        /// </summary>
        public bool ApplyToAutomationHotkeys { get; set; } = true;

        /// <summary>
        /// What the overlay hotkey does while a protected app is focused.
        /// </summary>
        public ProtectedAppHotkeyAction HotkeyAction { get; set; } = ProtectedAppHotkeyAction.Ignore;

        /// <summary>
        /// Comma, semicolon, or newline separated process names that should also be protected.
        /// </summary>
        public string CustomProtectedProcesses { get; set; } = string.Empty;
    }
}
