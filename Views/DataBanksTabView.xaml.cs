using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIA.Models;

namespace AIA.Views
{
    public partial class DataBanksTabView
    {
        public DataBanksTabView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event EventHandler<string>? ToastRequested;

        #region Category Events

        private void BtnAddCategory(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.IsAddingNewCategory = true;
            NewCategoryNameInput?.Focus();
        }

        private async void BtnConfirmNewCategory(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            await ViewModel.AddNewCategoryAsync(ViewModel.NewCategoryName);
        }

        private void BtnCancelNewCategory(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.NewCategoryName = string.Empty;
            ViewModel.IsAddingNewCategory = false;
        }

        private void NewCategoryNameInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewCategory(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewCategory(sender, e);
                e.Handled = true;
            }
        }

        private void CategoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankCategory category)
            {
                ViewModel.SelectedCategory = category;
                ViewModel.SelectedDataEntry = null;
            }
        }

        private async void BtnDeleteCategory(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankCategory category)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to delete the category '{category.Name}' and all its entries?",
                    "Delete Category",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await ViewModel.DeleteCategoryAsync(category);
                }
            }
            e.Handled = true;
        }

        #endregion

        #region Entry Events

        private void BtnAddEntry(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.SelectedCategory == null) return;

            ViewModel.IsAddingNewEntry = true;
            NewEntryTitleInput?.Focus();
        }

        private async void BtnConfirmNewEntry(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            await ViewModel.AddNewEntryAsync(ViewModel.NewEntryTitle);
        }

        private void BtnCancelNewEntry(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            ViewModel.NewEntryTitle = string.Empty;
            ViewModel.IsAddingNewEntry = false;
        }

        private void NewEntryTitleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmNewEntry(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelNewEntry(sender, e);
                e.Handled = true;
            }
        }

        private async void BtnImportFile(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.SelectedCategory == null) return;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import File to Data Bank",
                Filter = "All Supported Files|*.txt;*.md;*.pdf;*.eml;*.msg;*.json;*.xml;*.csv;*.log;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|" +
                         "Text Files|*.txt;*.md;*.log|" +
                         "PDF Files|*.pdf|" +
                         "Email Files|*.eml;*.msg|" +
                         "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|" +
                         "Data Files|*.json;*.xml;*.csv|" +
                         "All Files|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var filePath in openFileDialog.FileNames)
                {
                    await ViewModel.ImportFileAsync(filePath);
                }
            }
        }

        private void DataEntryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (sender is FrameworkElement element && element.Tag is DataBankEntry entry)
            {
                entry.RefreshPreview();
                ViewModel.SelectedDataEntry = entry;
            }
        }

        private async void BtnDeleteEntry(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedDataEntry == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete '{ViewModel.SelectedDataEntry.Title}'?",
                "Delete Entry",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await ViewModel.DeleteEntryAsync(ViewModel.SelectedDataEntry);
            }
        }

        private async void DataEntryField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedDataEntry == null) return;
            await ViewModel.UpdateEntryAsync();
        }

        private async void DataEntryType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel?.SelectedDataEntry == null) return;
            ViewModel.SelectedDataEntry.RefreshPreview();
            await ViewModel.UpdateEntryAsync();
        }

        private void BtnOpenFile(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedDataEntry == null) return;

            var filePath = ViewModel.SelectedDataEntry.FilePath;
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                System.Windows.MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
