using System.Windows;
using System.Windows.Controls;
using AIA.Dialogs;
using AIA.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace AIA.Views
{
    public partial class CalendarTabView : System.Windows.Controls.UserControl
    {
        public CalendarTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        private void BtnAddEvent(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            var dialog = new CalendarEventDialog();
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && dialog.ResultEvent != null)
            {
                ViewModel.AddCalendarEvent(dialog.ResultEvent);
            }
        }

        private void BtnEditSelected(object sender, RoutedEventArgs e)
        {
            if (LstEvents.SelectedItem is CalendarEventItem selected)
                EditEvent(selected);
        }

        private void BtnEditEvent(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: CalendarEventItem item })
                EditEvent(item);
        }

        private void EditEvent(CalendarEventItem item)
        {
            if (ViewModel == null) return;

            var dialog = new CalendarEventDialog(item);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && dialog.ResultEvent != null)
            {
                ViewModel.UpdateCalendarEvent(dialog.ResultEvent);
            }
        }

        private void BtnDeleteEvent(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement { Tag: CalendarEventItem item })
                return;

            var result = WpfMessageBox.Show(
                $"Delete '{item.Title}'?",
                "Delete event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                ViewModel.DeleteCalendarEvent(item);
        }
    }
}
