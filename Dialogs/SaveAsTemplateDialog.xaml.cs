using System.Windows;
using AIA.Models;

namespace AIA.Dialogs
{
    public partial class SaveAsTemplateDialog : Wpf.Ui.Controls.FluentWindow
    {
        public string TemplateName { get; private set; } = string.Empty;
        public string TemplateDescription { get; private set; } = string.Empty;
        public bool DialogResult { get; private set; }

        public SaveAsTemplateDialog()
        {
            InitializeComponent();
            TxtTemplateName.Focus();
        }

        private void BtnCreate(object sender, RoutedEventArgs e)
        {
            var templateName = TxtTemplateName.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(templateName))
            {
                TxtTemplateName.Focus();
                return;
            }

            TemplateName = templateName;
            TemplateDescription = TxtTemplateDescription.Text?.Trim() ?? string.Empty;
            DialogResult = true;
            Close();
        }

        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
