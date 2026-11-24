using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfBrushes = System.Windows.Media.Brushes;

namespace AIA.Dialogs
{
    /// <summary>
    /// Lightweight choice dialog used by bulk task operations and dependency selection.
    /// </summary>
    public static class ChoiceDialog
    {
        public static bool Show<T>(Window? owner, string title, string prompt, IEnumerable<T> choices, Func<T, string> display, out T selected)
        {
            var items = choices.Select(choice => new ChoiceItem<T>(choice, display(choice))).ToList();
            if (items.Count == 0)
            {
                selected = default!;
                return false;
            }

            var result = default(T);

            var comboBox = new WpfComboBox
            {
                ItemsSource = items,
                SelectedIndex = items.Count > 0 ? 0 : -1,
                Margin = new Thickness(0, 8, 0, 14)
            };

            var okButton = new WpfButton
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancelButton = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                MinWidth = 80
            };

            var buttons = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(comboBox);
            panel.Children.Add(buttons);

            var window = CreateWindow(title, owner, panel);

            okButton.Click += (_, _) =>
            {
                if (comboBox.SelectedItem is ChoiceItem<T> choice)
                {
                    result = choice.Value;
                    window.DialogResult = true;
                }
            };

            var confirmed = window.ShowDialog() == true;
            selected = confirmed ? result! : default!;
            return confirmed;
        }

        internal static Window CreateWindow(string title, Window? owner, UIElement content)
        {
            var window = new Window
            {
                Title = title,
                Owner = owner,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner == null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = WpfBrushes.White
            };

            window.Loaded += (_, _) => window.Activate();
            return window;
        }

        private sealed class ChoiceItem<T>
        {
            public ChoiceItem(T value, string label)
            {
                Value = value;
                Label = label;
            }

            public T Value { get; }
            public string Label { get; }

            public override string ToString() => Label;
        }
    }

    /// <summary>
    /// Lightweight text prompt used by tag entry.
    /// </summary>
    public static class TextInputDialog
    {
        public static string? Show(Window? owner, string title, string prompt, string initialValue = "")
        {
            string? result = null;
            var textBox = new WpfTextBox
            {
                Text = initialValue,
                MinWidth = 260,
                Margin = new Thickness(0, 8, 0, 14)
            };

            var okButton = new WpfButton
            {
                Content = "OK",
                IsDefault = true,
                MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var cancelButton = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                MinWidth = 80
            };

            var buttons = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(textBox);
            panel.Children.Add(buttons);

            var window = ChoiceDialog.CreateWindow(title, owner, panel);

            okButton.Click += (_, _) =>
            {
                result = textBox.Text.Trim();
                window.DialogResult = true;
            };

            window.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            window.ShowDialog();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
