using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class ChatTemplatesView : System.Windows.Controls.UserControl
    {
        public ChatTemplatesView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a template is clicked
        /// </summary>
        public event EventHandler<ChatMessageTemplate>? TemplateClicked;

        private void TemplateButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is ChatMessageTemplate template)
            {
                // Raise event for parent to handle
                TemplateClicked?.Invoke(this, template);
            }
        }

        private void UpdateEmptyState()
        {
            if (ViewModel?.ChatMessageTemplates != null && ViewModel.ChatMessageTemplates.Count > 0)
            {
                EmptyState.Visibility = Visibility.Collapsed;
                TemplatesControl.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyState.Visibility = Visibility.Visible;
                TemplatesControl.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            if (e.Property.Name == nameof(DataContext))
            {
                UpdateEmptyState();
            }
        }
    }
}
