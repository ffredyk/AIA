using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class OutlookTabView
    {
        public OutlookTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event EventHandler<string>? ToastRequested;

        private async void BtnRefreshOutlook(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            await ViewModel.RefreshFlaggedEmailsAsync();
        }

        private void OutlookEmailItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is OutlookEmail email)
            {
                ViewModel.SelectedOutlookEmail = email;
            }
        }

        private async void BtnMarkEmailComplete(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is OutlookEmail email)
            {
                await ViewModel.MarkEmailFlagCompleteAsync(email);
                ToastRequested?.Invoke(this, "Email flag marked as complete");
            }
            e.Handled = true;
        }

        private async void BtnMarkSelectedEmailComplete(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedOutlookEmail == null) return;

            await ViewModel.MarkEmailFlagCompleteAsync(ViewModel.SelectedOutlookEmail);
            ToastRequested?.Invoke(this, "Email flag marked as complete");
        }

        private async void BtnClearEmailFlag(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedOutlookEmail == null) return;

            await ViewModel.ClearEmailFlagAsync(ViewModel.SelectedOutlookEmail);
            ToastRequested?.Invoke(this, "Email flag cleared");
        }

        private async void BtnOpenInOutlook(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedOutlookEmail == null) return;
            await ViewModel.OpenEmailInOutlookAsync(ViewModel.SelectedOutlookEmail);
        }
    }
}
