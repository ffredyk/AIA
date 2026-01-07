using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AIA.Models;

namespace AIA.Dialogs
{
    public partial class TaskTemplateDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly OverlayViewModel _viewModel;
        public TaskTemplate? SelectedTemplate { get; private set; }

        public TaskTemplateDialog()
        {
            InitializeComponent();
            _viewModel = OverlayViewModel.Singleton;
            
            // Populate templates
            TemplatesListControl.ItemsSource = _viewModel.TaskTemplates;
        }

        private void Template_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is TaskTemplate template)
            {
                SelectedTemplate = template;
                DialogResult = true;
                Close();
            }
        }

        private void BtnManageTemplates(object sender, RoutedEventArgs e)
        {
            var managerDialog = new TemplateManagerDialog();
            managerDialog.Owner = this;
            managerDialog.ShowDialog();
            
            // Refresh templates list
            TemplatesListControl.ItemsSource = null;
            TemplatesListControl.ItemsSource = _viewModel.TaskTemplates;
        }

        private void BtnCancel(object sender, RoutedEventArgs e)
        {
            SelectedTemplate = null;
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Ensure proper cleanup when closing via X button
            if (DialogResult == null)
            {
                SelectedTemplate = null;
                DialogResult = false;
            }
            base.OnClosing(e);
        }
    }
}
