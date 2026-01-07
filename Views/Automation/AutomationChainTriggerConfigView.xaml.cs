using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AIA.Models.Automation;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AIA.Views.Automation
{
    /// <summary>
    /// Configuration view for Automation Chain trigger
    /// </summary>
    public partial class AutomationChainTriggerConfigView : WpfUserControl
    {
        public AutomationChainTriggerConfigView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ChainTriggerViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                UpdateSourceName(viewModel);
            }

            if (e.OldValue is ChainTriggerViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is ChainTriggerViewModel viewModel && e.PropertyName == nameof(ChainTriggerViewModel.SourceAutomationId))
            {
                Dispatcher.Invoke(() => UpdateSourceName(viewModel));
            }
        }

        private void UpdateSourceName(ChainTriggerViewModel viewModel)
        {
            var sourceAutomation = viewModel.AvailableAutomations
                .FirstOrDefault(a => a.Id == viewModel.SourceAutomationId);
            SourceNameText.Text = sourceAutomation?.Name ?? "Select Source";
        }
    }
}
