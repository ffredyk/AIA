using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIA.Models;
using Wpf.Ui.Controls;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace AIA
{
    public partial class ChatTemplateEditorWindow : Window
    {
        private ChatMessageTemplate? _template;
        private readonly bool _isNewTemplate;

        public ChatMessageTemplate? Template => _template;

        public ChatTemplateEditorWindow(ChatMessageTemplate? template = null)
        {
            InitializeComponent();

            _isNewTemplate = template == null;
            _template = template ?? new ChatMessageTemplate();

            if (_isNewTemplate)
            {
                TitleText.Text = Services.LocalizationService.Instance.GetString("ChatTemplate_NewTemplate");
            }

            LoadTemplate();
            UpdatePreview();
        }

        private void LoadTemplate()
        {
            if (_template == null) return;

            TxtTitle.Text = _template.Title;
            TxtDescription.Text = _template.Description;
            TxtMessage.Text = _template.Message;

            // Select icon
            foreach (ComboBoxItem item in CmbIcon.Items)
            {
                if (item.Tag?.ToString() == _template.Icon)
                {
                    CmbIcon.SelectedItem = item;
                    break;
                }
            }
            if (CmbIcon.SelectedItem == null && CmbIcon.Items.Count > 0)
            {
                CmbIcon.SelectedIndex = 0;
            }

            // Select color
            foreach (ComboBoxItem item in CmbColor.Items)
            {
                if (item.Tag?.ToString() == _template.Color)
                {
                    CmbColor.SelectedItem = item;
                    break;
                }
            }
            if (CmbColor.SelectedItem == null && CmbColor.Items.Count > 0)
            {
                CmbColor.SelectedIndex = 0;
            }

            // Wire up text change events
            TxtTitle.TextChanged += (s, e) => UpdatePreview();
            TxtDescription.TextChanged += (s, e) => UpdatePreview();
        }

        private void UpdatePreview()
        {
            PreviewTitle.Text = string.IsNullOrWhiteSpace(TxtTitle.Text) ? "Template Title" : TxtTitle.Text;
            PreviewDescription.Text = string.IsNullOrWhiteSpace(TxtDescription.Text) ? "Template description" : TxtDescription.Text;

            // Update icon preview
            if (CmbIcon.SelectedItem is ComboBoxItem iconItem && iconItem.Tag is string iconName)
            {
                if (Enum.TryParse<SymbolRegular>(iconName, out var symbol))
                {
                    IconPreview.Symbol = symbol;
                    PreviewIcon.Symbol = symbol;
                }
            }

            // Update color preview
            if (CmbColor.SelectedItem is ComboBoxItem colorItem && colorItem.Tag is string colorHex)
            {
                try
                {
                    var color = (WpfColor)WpfColorConverter.ConvertFromString(colorHex);
                    
                    ColorPreviewBorder.Background = new SolidColorBrush(color);
                    IconPreviewBorder.Background = new SolidColorBrush(WpfColor.FromArgb((byte)(0.2 * 255), color.R, color.G, color.B));
                    IconPreview.Foreground = new SolidColorBrush(color);
                    PreviewIconBorder.Background = new SolidColorBrush(WpfColor.FromArgb((byte)(0.2 * 255), color.R, color.G, color.B));
                    PreviewIcon.Foreground = new SolidColorBrush(color);
                }
                catch { }
            }
        }

        private void CmbIcon_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void CmbColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTitle.Text))
            {
                System.Windows.MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("ChatTemplate_TitleRequired"),
                    Services.LocalizationService.Instance.GetString("Common_Validation"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                TxtTitle.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtMessage.Text))
            {
                System.Windows.MessageBox.Show(
                    Services.LocalizationService.Instance.GetString("ChatTemplate_MessageRequired"),
                    Services.LocalizationService.Instance.GetString("Common_Validation"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                TxtMessage.Focus();
                return;
            }

            if (_template == null) return;

            _template.Title = TxtTitle.Text.Trim();
            _template.Description = TxtDescription.Text.Trim();
            _template.Message = TxtMessage.Text.Trim();

            if (CmbIcon.SelectedItem is ComboBoxItem iconItem && iconItem.Tag is string iconName)
            {
                _template.Icon = iconName;
            }

            if (CmbColor.SelectedItem is ComboBoxItem colorItem && colorItem.Tag is string colorHex)
            {
                _template.Color = colorHex;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
