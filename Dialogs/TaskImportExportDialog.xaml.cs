using System;
using System.IO;
using System.Windows;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using WpfClipboard = System.Windows.Clipboard;
using AIA.Models;

namespace AIA.Dialogs
{
    public partial class TaskImportExportDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly OverlayViewModel _viewModel;

        public TaskImportExportDialog()
        {
            InitializeComponent();
            _viewModel = OverlayViewModel.Singleton;
        }

        #region Export

        private async void BtnExportToFile(object sender, RoutedEventArgs e)
        {
            var saveDialog = new WpfSaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"tasks_export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var includeArchived = ChkIncludeArchived.IsChecked ?? false;
                var success = await _viewModel.ExportTasksAsync(saveDialog.FileName, includeArchived);
                
                if (success)
                {
                    WpfMessageBox.Show($"Tasks exported successfully to:\n{saveDialog.FileName}", 
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WpfMessageBox.Show("Failed to export tasks. Please try again.", 
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnCopyToClipboard(object sender, RoutedEventArgs e)
        {
            try
            {
                var tempFile = Path.GetTempFileName();
                var includeArchived = ChkIncludeArchived.IsChecked ?? false;
                var success = await _viewModel.ExportTasksAsync(tempFile, includeArchived);
                
                if (success)
                {
                    var json = await File.ReadAllTextAsync(tempFile);
                    WpfClipboard.SetText(json);
                    File.Delete(tempFile);
                    
                    WpfMessageBox.Show("Tasks copied to clipboard successfully.", 
                        "Copy Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WpfMessageBox.Show("Failed to copy tasks to clipboard.", 
                        "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error copying to clipboard: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Import

        private async void BtnImportFromFile(object sender, RoutedEventArgs e)
        {
            var openDialog = new WpfOpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (openDialog.ShowDialog() == true)
            {
                var replaceExisting = ChkReplaceExisting.IsChecked ?? false;
                
                if (replaceExisting)
                {
                    var result = WpfMessageBox.Show(
                        "This will delete all existing tasks and replace them with the imported data. Are you sure?", 
                        "Confirm Replace", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Warning);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                var count = await _viewModel.ImportTasksAsync(openDialog.FileName, replaceExisting);
                
                if (count > 0)
                {
                    WpfMessageBox.Show($"Successfully imported {count} task(s).", 
                        "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
                else
                {
                    WpfMessageBox.Show("No tasks were imported. Please check the file format.", 
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async void BtnPasteFromClipboard(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!WpfClipboard.ContainsText())
                {
                    WpfMessageBox.Show("Clipboard does not contain text data.", 
                        "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var json = WpfClipboard.GetText();
                var tempFile = Path.GetTempFileName();
                
                await File.WriteAllTextAsync(tempFile, json);
                
                var replaceExisting = ChkReplaceExisting.IsChecked ?? false;
                
                if (replaceExisting)
                {
                    var result = WpfMessageBox.Show(
                        "This will delete all existing tasks and replace them with the imported data. Are you sure?", 
                        "Confirm Replace", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Warning);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        File.Delete(tempFile);
                        return;
                    }
                }

                var count = await _viewModel.ImportTasksAsync(tempFile, replaceExisting);
                File.Delete(tempFile);
                
                if (count > 0)
                {
                    WpfMessageBox.Show($"Successfully imported {count} task(s) from clipboard.", 
                        "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
                else
                {
                    WpfMessageBox.Show("No tasks were imported. Please check the clipboard data format.", 
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Error importing from clipboard: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
