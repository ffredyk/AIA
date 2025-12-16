using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIA.Models.AI;
using AIA.Services.AI;

namespace AIA
{
    public partial class OrchestrationWindow : Window, INotifyPropertyChanged
    {
        private readonly AIOrchestrationService _orchestrationService;
        private AIProvider? _selectedProvider;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<AIProvider> Providers => _orchestrationService.Providers;
        public AIOrchestrationSettings Settings => _orchestrationService.Settings;

        public AIProvider? SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (_selectedProvider != value)
                {
                    _selectedProvider = value;
                    OnPropertyChanged(nameof(SelectedProvider));
                    UpdateProviderUI();
                }
            }
        }

        public OrchestrationWindow(AIOrchestrationService orchestrationService)
        {
            _orchestrationService = orchestrationService;
            
            InitializeComponent();
            DataContext = this;

            // Populate tools list
            ToolsListControl.ItemsSource = _orchestrationService.GetAvailableTools().ToList();

            // Select first provider if available
            if (Providers.Count > 0)
            {
                SelectedProvider = Providers[0];
            }

            UpdateStatus();
        }

        private void UpdateProviderUI()
        {
            if (SelectedProvider == null) return;

            // Update API key field
            ApiKeyInput.Password = SelectedProvider.ApiKey;

            // Update model suggestions
            UpdateModelSuggestions();

            // Hide test result
            TestResultBorder.Visibility = Visibility.Collapsed;
        }

        private void UpdateModelSuggestions()
        {
            if (SelectedProvider == null) return;

            var recommendations = AIOrchestrationService.GetRecommendedModels();
            if (recommendations.TryGetValue(SelectedProvider.ProviderType, out var models))
            {
                ModelSuggestionsCombo.ItemsSource = models;
                ModelSuggestionsCombo.SelectedIndex = -1;
            }
            else
            {
                ModelSuggestionsCombo.ItemsSource = null;
            }
        }

        private void UpdateStatus()
        {
            var enabledCount = Providers.Count(p => p.IsEnabled);
            var defaultProvider = Providers.FirstOrDefault(p => p.IsDefault);
            
            StatusText.Text = $"{Providers.Count} provider(s) configured, {enabledCount} enabled" +
                (defaultProvider != null ? $" | Default: {defaultProvider.Name}" : "");
        }

        #region Event Handlers

        private void ProviderItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AIProvider provider)
            {
                SelectedProvider = provider;
            }
        }

        private void BtnAddProvider(object sender, RoutedEventArgs e)
        {
            var newProvider = new AIProvider
            {
                Name = "New Provider",
                ProviderType = AIProviderType.OpenAI,
                ModelId = "gpt-4o",
                Priority = 50,
                Strengths = new[] { "conversation" }
            };

            _orchestrationService.AddProvider(newProvider);
            SelectedProvider = newProvider;
            UpdateStatus();
        }

        private async void BtnTestProvider(object sender, RoutedEventArgs e)
        {
            if (SelectedProvider == null) return;

            TestResultBorder.Visibility = Visibility.Visible;
            TestResultBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 128, 128, 128));
            TestResultText.Foreground = new SolidColorBrush(Colors.White);
            TestResultText.Text = "Testing connection...";

            var (success, message) = await _orchestrationService.TestProviderAsync(SelectedProvider);

            if (success)
            {
                TestResultBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 30, 183, 95));
                TestResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 183, 95));
            }
            else
            {
                TestResultBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 68, 68));
                TestResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 102, 102));
            }

            TestResultText.Text = message;
        }

        private void BtnSetDefault(object sender, RoutedEventArgs e)
        {
            if (SelectedProvider == null) return;

            _orchestrationService.SetDefaultProvider(SelectedProvider);
            UpdateStatus();
        }

        private void BtnDeleteProvider(object sender, RoutedEventArgs e)
        {
            if (SelectedProvider == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{SelectedProvider.Name}'?",
                "Delete Provider",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var provider = SelectedProvider;
                SelectedProvider = Providers.FirstOrDefault(p => p != provider);
                _orchestrationService.RemoveProvider(provider);
                UpdateStatus();
            }
        }

        private void ApiKeyInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (SelectedProvider != null)
            {
                SelectedProvider.ApiKey = ApiKeyInput.Password;
            }
        }

        private void ModelSuggestion_Selected(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedProvider != null && ModelSuggestionsCombo.SelectedItem is string model)
            {
                SelectedProvider.ModelId = model;
            }
        }

        private async void BtnSave(object sender, RoutedEventArgs e)
        {
            await _orchestrationService.SaveConfigurationAsync();
            StatusText.Text = "Configuration saved!";
        }

        private void BtnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Auto-save on close
            _ = _orchestrationService.SaveConfigurationAsync();
            base.OnClosing(e);
        }
    }
}
