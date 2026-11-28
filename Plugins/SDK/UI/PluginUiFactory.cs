using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Reusable WPF template primitives for programmatically built plugin views.
    /// Plugins compose their tab and settings templates from these helpers instead of
    /// duplicating the same styling code in every plugin.
    /// </summary>
    public static class PluginUiFactory
    {
        /// <summary>
        /// Creates a frozen solid color brush from the supplied ARGB components.
        /// </summary>
        /// <param name="alpha">Alpha channel.</param>
        /// <param name="red">Red channel.</param>
        /// <param name="green">Green channel.</param>
        /// <param name="blue">Blue channel.</param>
        /// <returns>A frozen <see cref="SolidColorBrush"/>.</returns>
        public static Brush BrushFrom(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Creates a two-way binding that updates the source on every property change.
        /// </summary>
        /// <param name="path">Binding path on the data context.</param>
        /// <returns>A configured <see cref="Binding"/>.</returns>
        public static Binding TwoWay(string path)
        {
            return new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
        }

        /// <summary>
        /// Creates a row definition factory for use with a grid template.
        /// </summary>
        /// <param name="height">Row height.</param>
        /// <returns>Factory for a <see cref="RowDefinition"/>.</returns>
        public static FrameworkElementFactory CreateRow(GridLength height)
        {
            var row = new FrameworkElementFactory(typeof(RowDefinition));
            row.SetValue(RowDefinition.HeightProperty, height);
            return row;
        }

        /// <summary>
        /// Creates a column definition factory for use with a grid template.
        /// </summary>
        /// <param name="width">Column width.</param>
        /// <returns>Factory for a <see cref="ColumnDefinition"/>.</returns>
        public static FrameworkElementFactory CreateColumn(GridLength width)
        {
            var column = new FrameworkElementFactory(typeof(ColumnDefinition));
            column.SetValue(ColumnDefinition.WidthProperty, width);
            return column;
        }

        /// <summary>
        /// Creates a small section header text block.
        /// </summary>
        /// <param name="text">Header text.</param>
        /// <param name="fontSize">Header font size.</param>
        /// <returns>Factory for a <see cref="TextBlock"/>.</returns>
        public static FrameworkElementFactory CreateSectionHeader(string text, double fontSize = 10)
        {
            var header = new FrameworkElementFactory(typeof(TextBlock));
            header.SetValue(TextBlock.TextProperty, text);
            header.SetValue(TextBlock.ForegroundProperty, BrushFrom(0xBB, 0xFF, 0xFF, 0xFF));
            header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            header.SetValue(TextBlock.FontSizeProperty, fontSize);
            header.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 6));
            return header;
        }

        /// <summary>
        /// Creates a text block, either bound to a view model path or showing static text.
        /// </summary>
        /// <param name="bindingPath">Optional binding path; takes precedence over <paramref name="text"/>.</param>
        /// <param name="text">Static text used when no binding path is supplied.</param>
        /// <param name="fontSize">Font size.</param>
        /// <param name="foreground">Optional foreground brush; defaults to white.</param>
        /// <param name="fontWeight">Optional font weight.</param>
        /// <param name="wrap">Whether text wraps.</param>
        /// <param name="trim">Whether long text is trimmed with an ellipsis.</param>
        /// <param name="margin">Optional margin.</param>
        /// <returns>Factory for a <see cref="TextBlock"/>.</returns>
        public static FrameworkElementFactory CreateText(
            string? bindingPath = null,
            string? text = null,
            double fontSize = 12,
            Brush? foreground = null,
            FontWeight? fontWeight = null,
            bool wrap = false,
            bool trim = false,
            Thickness? margin = null)
        {
            var block = new FrameworkElementFactory(typeof(TextBlock));

            if (bindingPath != null)
            {
                block.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            }
            else
            {
                block.SetValue(TextBlock.TextProperty, text ?? string.Empty);
            }

            block.SetValue(TextBlock.FontSizeProperty, fontSize);
            block.SetValue(TextBlock.ForegroundProperty, foreground ?? Brushes.White);

            if (fontWeight.HasValue)
                block.SetValue(TextBlock.FontWeightProperty, fontWeight.Value);
            if (wrap)
                block.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            if (trim)
                block.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (margin.HasValue)
                block.SetValue(FrameworkElement.MarginProperty, margin.Value);

            return block;
        }

        /// <summary>
        /// Creates a themed text input bound two-way to a view model path.
        /// </summary>
        /// <param name="bindingPath">Binding path for the text value.</param>
        /// <param name="tooltip">Tooltip shown for the input.</param>
        /// <param name="acceptReturns">Whether the input accepts multiple lines of text.</param>
        /// <param name="wrap">Whether long lines wrap.</param>
        /// <param name="minHeight">Optional minimum height.</param>
        /// <param name="fontSize">Font size.</param>
        /// <returns>Factory for a <see cref="TextBox"/>.</returns>
        public static FrameworkElementFactory CreateInput(
            string bindingPath,
            string tooltip,
            bool acceptReturns = false,
            bool wrap = false,
            double? minHeight = null,
            double fontSize = 12)
        {
            var box = new FrameworkElementFactory(typeof(TextBox));
            box.SetBinding(TextBox.TextProperty, TwoWay(bindingPath));
            box.SetValue(Control.BackgroundProperty, BrushFrom(0x22, 0xFF, 0xFF, 0xFF));
            box.SetValue(Control.ForegroundProperty, Brushes.White);
            box.SetValue(Control.BorderBrushProperty, BrushFrom(0x33, 0xFF, 0xFF, 0xFF));
            box.SetValue(TextBox.CaretBrushProperty, Brushes.White);
            box.SetValue(Control.PaddingProperty, new Thickness(6, 4, 6, 4));
            box.SetValue(Control.FontSizeProperty, fontSize);
            box.SetValue(FrameworkElement.ToolTipProperty, tooltip);

            if (acceptReturns)
                box.SetValue(TextBox.AcceptsReturnProperty, true);
            if (wrap)
                box.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
            if (acceptReturns || wrap)
                box.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            if (minHeight.HasValue)
                box.SetValue(FrameworkElement.MinHeightProperty, minHeight.Value);

            return box;
        }

        /// <summary>
        /// Creates a flat, hand-cursor button that invokes a command on the view model.
        /// </summary>
        /// <param name="text">Button caption.</param>
        /// <param name="commandPath">Binding path of the command.</param>
        /// <param name="background">Button background brush.</param>
        /// <param name="foreground">Optional foreground brush; defaults to white.</param>
        /// <returns>Factory for a <see cref="Button"/>.</returns>
        public static FrameworkElementFactory CreateButton(
            string text,
            string commandPath,
            Brush background,
            Brush? foreground = null)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(ContentControl.ContentProperty, text);
            button.SetBinding(ButtonBase.CommandProperty, new Binding(commandPath));
            button.SetValue(Control.BackgroundProperty, background);
            button.SetValue(Control.ForegroundProperty, foreground ?? Brushes.White);
            button.SetValue(Control.PaddingProperty, new Thickness(12, 5, 12, 5));
            button.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            button.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            return button;
        }

        /// <summary>
        /// Creates a transparent, borderless list box bound to an items collection and selected item.
        /// </summary>
        /// <param name="itemsPath">Binding path of the items collection.</param>
        /// <param name="selectedItemPath">Binding path of the selected item.</param>
        /// <param name="itemTemplate">Template used for each item.</param>
        /// <returns>Factory for a <see cref="System.Windows.Controls.ListBox"/>.</returns>
        public static FrameworkElementFactory CreateListBox(
            string itemsPath,
            string selectedItemPath,
            DataTemplate itemTemplate)
        {
            var list = new FrameworkElementFactory(typeof(System.Windows.Controls.ListBox));
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
            list.SetBinding(
                System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                TwoWay(selectedItemPath));
            list.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            list.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            list.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
            return list;
        }

        /// <summary>
        /// Creates a scroll viewer that wraps the supplied child factory.
        /// </summary>
        /// <param name="child">Child factory to scroll.</param>
        /// <param name="vertical">Whether the vertical scrollbar is automatic.</param>
        /// <param name="horizontal">Whether the horizontal scrollbar is automatic.</param>
        /// <returns>Factory for a <see cref="ScrollViewer"/>.</returns>
        public static FrameworkElementFactory CreateScrollViewer(
            FrameworkElementFactory child,
            bool vertical = true,
            bool horizontal = false)
        {
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(
                ScrollViewer.VerticalScrollBarVisibilityProperty,
                vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            scroll.SetValue(
                ScrollViewer.HorizontalScrollBarVisibilityProperty,
                horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            scroll.AppendChild(child);
            return scroll;
        }

        /// <summary>
        /// Creates a list item template with a title and optional subtitle and meta lines.
        /// </summary>
        /// <param name="titlePath">Binding path of the item title.</param>
        /// <param name="subtitlePath">Optional binding path of the subtitle.</param>
        /// <param name="metaPath">Optional binding path of the meta line.</param>
        /// <param name="accentAlpha">Alpha channel of the item background.</param>
        /// <returns>A <see cref="DataTemplate"/> for list items.</returns>
        public static DataTemplate CreateListItemTemplate(
            string titlePath,
            string? subtitlePath = null,
            string? metaPath = null,
            byte accentAlpha = 0x12)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, BrushFrom(accentAlpha, 0xFF, 0xFF, 0xFF));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.AppendChild(CreateText(bindingPath: titlePath, fontSize: 12, fontWeight: FontWeights.SemiBold));

            if (!string.IsNullOrEmpty(subtitlePath))
            {
                panel.AppendChild(CreateText(
                    bindingPath: subtitlePath,
                    fontSize: 10,
                    foreground: BrushFrom(0x88, 0xFF, 0xFF, 0xFF),
                    trim: true));
            }

            if (!string.IsNullOrEmpty(metaPath))
            {
                panel.AppendChild(CreateText(
                    bindingPath: metaPath,
                    fontSize: 10,
                    foreground: BrushFrom(0x66, 0xFF, 0xFF, 0xFF),
                    trim: true));
            }

            border.AppendChild(panel);
            return new DataTemplate { VisualTree = border };
        }
    }
}
