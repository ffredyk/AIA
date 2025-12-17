using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AIA.Plugins.SDK;

// Alias to resolve ambiguous reference
using WpfApplication = System.Windows.Application;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of plugin UI service
    /// </summary>
    public class HostPluginUIService : IPluginUIService
    {
        private readonly Dictionary<string, PluginTabDefinition> _registeredTabs = new();
        private readonly Dictionary<string, PluginToolbarButton> _registeredButtons = new();

        /// <summary>
        /// Observable collection of registered tabs for UI binding
        /// </summary>
        public ObservableCollection<PluginTabDefinition> Tabs { get; } = new();

        /// <summary>
        /// Observable collection of registered toolbar buttons for UI binding
        /// </summary>
        public ObservableCollection<PluginToolbarButton> ToolbarButtons { get; } = new();

        /// <summary>
        /// Event fired when a toast should be shown
        /// </summary>
        public event EventHandler<ToastEventArgs>? ToastRequested;

        /// <summary>
        /// Event fired when a dialog should be shown
        /// </summary>
        public event EventHandler<DialogEventArgs>? DialogRequested;

        public void RegisterTab(PluginTabDefinition tabDefinition)
        {
            if (tabDefinition == null)
                throw new ArgumentNullException(nameof(tabDefinition));
            if (string.IsNullOrEmpty(tabDefinition.TabId))
                throw new ArgumentException("TabId is required", nameof(tabDefinition));

            if (_registeredTabs.ContainsKey(tabDefinition.TabId))
            {
                throw new InvalidOperationException($"Tab with ID '{tabDefinition.TabId}' is already registered");
            }

            _registeredTabs[tabDefinition.TabId] = tabDefinition;

            // Insert in order
            var insertIndex = 0;
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Order > tabDefinition.Order)
                {
                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }

            WpfApplication.Current?.Dispatcher.Invoke(() =>
            {
                Tabs.Insert(insertIndex, tabDefinition);
            });
        }

        public void UnregisterTab(string tabId)
        {
            if (_registeredTabs.TryGetValue(tabId, out var tab))
            {
                _registeredTabs.Remove(tabId);
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    Tabs.Remove(tab);
                });
            }
        }

        public void RegisterToolbarButton(PluginToolbarButton button)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));
            if (string.IsNullOrEmpty(button.ButtonId))
                throw new ArgumentException("ButtonId is required", nameof(button));

            if (_registeredButtons.ContainsKey(button.ButtonId))
            {
                throw new InvalidOperationException($"Button with ID '{button.ButtonId}' is already registered");
            }

            _registeredButtons[button.ButtonId] = button;

            // Insert in order
            var insertIndex = 0;
            for (int i = 0; i < ToolbarButtons.Count; i++)
            {
                if (ToolbarButtons[i].Order > button.Order)
                {
                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }

            WpfApplication.Current?.Dispatcher.Invoke(() =>
            {
                ToolbarButtons.Insert(insertIndex, button);
            });
        }

        public void UnregisterToolbarButton(string buttonId)
        {
            if (_registeredButtons.TryGetValue(buttonId, out var button))
            {
                _registeredButtons.Remove(buttonId);
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    ToolbarButtons.Remove(button);
                });
            }
        }

        public void ShowToast(string message, ToastType type = ToastType.Info)
        {
            WpfApplication.Current?.Dispatcher.Invoke(() =>
            {
                ToastRequested?.Invoke(this, new ToastEventArgs(message, type));
            });
        }

        public bool? ShowDialog(string title, object content, DialogButtons buttons = DialogButtons.OkCancel)
        {
            bool? result = null;

            WpfApplication.Current?.Dispatcher.Invoke(() =>
            {
                var args = new DialogEventArgs(title, content, buttons);
                DialogRequested?.Invoke(this, args);
                result = args.Result;
            });

            return result;
        }

        public void RefreshUI()
        {
            WpfApplication.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var tab in Tabs)
                {
                    tab.ViewModel?.Refresh();
                }
            });
        }

        /// <summary>
        /// Updates the badge count for a tab
        /// </summary>
        public void UpdateTabBadge(string tabId, int count)
        {
            if (_registeredTabs.TryGetValue(tabId, out var tab))
            {
                WpfApplication.Current?.Dispatcher.Invoke(() =>
                {
                    tab.BadgeCount = count;
                });
            }
        }
    }

    public class ToastEventArgs : EventArgs
    {
        public string Message { get; }
        public ToastType Type { get; }

        public ToastEventArgs(string message, ToastType type)
        {
            Message = message;
            Type = type;
        }
    }

    public class DialogEventArgs : EventArgs
    {
        public string Title { get; }
        public object Content { get; }
        public DialogButtons Buttons { get; }
        public bool? Result { get; set; }

        public DialogEventArgs(string title, object content, DialogButtons buttons)
        {
            Title = title;
            Content = content;
            Buttons = buttons;
        }
    }
}
