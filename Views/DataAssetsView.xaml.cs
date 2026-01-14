using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIA.Models;

namespace AIA.Views
{
    public partial class DataAssetsView
    {
        public DataAssetsView()
        {
            InitializeComponent();
        }

        private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

        /// <summary>
        /// Event raised when a toast notification should be shown
        /// </summary>
        public event EventHandler<string>? ToastRequested;

        private void BtnRefreshDataAssets(object sender, RoutedEventArgs e)
        {
            ViewModel?.CaptureCurrentDataAssets();
        }

        private void DataAssetItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is DataAsset asset)
            {
                ShowDataAssetPreview(asset);
            }
        }

        private void ShowDataAssetPreview(DataAsset asset)
        {
            if (asset.FullImage == null) return;

            var previewWindow = new Window
            {
                Title = asset.Name,
                Width = Math.Min(asset.FullImage.PixelWidth + 40, SystemParameters.PrimaryScreenWidth * 0.9),
                Height = Math.Min(asset.FullImage.PixelHeight + 80, SystemParameters.PrimaryScreenHeight * 0.9),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 30, 30, 30)),
                ResizeMode = ResizeMode.CanResize,
                Owner = Window.GetWindow(this)
            };

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var image = new System.Windows.Controls.Image
            {
                Source = asset.FullImage,
                Stretch = Stretch.None
            };

            scrollViewer.Content = image;
            previewWindow.Content = scrollViewer;
            previewWindow.ShowDialog();
        }

        private void BtnCopyAssetToClipboard(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            if (ViewModel == null) return;

            if (ViewModel.CopyDataAssetToClipboard(asset))
            {
                ToastRequested?.Invoke(this, "Copied to clipboard!");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to copy to clipboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAssetToDisk(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            if (ViewModel == null) return;

            var filePath = ViewModel.SaveDataAssetToFile(asset);
            if (!string.IsNullOrEmpty(filePath))
            {
                ToastRequested?.Invoke(this, $"Saved to {System.IO.Path.GetFileName(filePath)}");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAssetAs(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            if (ViewModel == null) return;

            var filePath = ViewModel.SaveDataAssetWithDialog(asset);
            if (!string.IsNullOrEmpty(filePath))
            {
                ToastRequested?.Invoke(this, $"Saved to {System.IO.Path.GetFileName(filePath)}");
            }
        }

        private async void BtnSaveAssetToDataBank(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            if (ViewModel == null || ViewModel.SelectedCategory == null)
            {
                System.Windows.MessageBox.Show("Please select a data bank category first.", "No Category Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var success = await ViewModel.SaveDataAssetToDataBankAsync(asset);
            if (success)
            {
                ToastRequested?.Invoke(this, $"Saved to '{ViewModel.SelectedCategory.Name}' data bank");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save to data bank.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Clipboard History

        private void ClipboardItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is DataAsset asset)
            {
                // For images, show preview; for text/files, restore to clipboard
                if (asset.AssetType == DataAssetType.ClipboardImage && asset.FullImage != null)
                {
                    ShowDataAssetPreview(asset);
                }
                else
                {
                    RestoreClipboardItem(asset);
                }
            }
        }

        private void BtnRestoreClipboardItem(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not DataAsset asset)
                return;

            RestoreClipboardItem(asset);
        }

        private void RestoreClipboardItem(DataAsset asset)
        {
            if (ViewModel == null) return;

            if (ViewModel.RestoreClipboardItem(asset))
            {
                ToastRequested?.Invoke(this, "Restored to clipboard!");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to restore to clipboard.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearClipboardHistory(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            ViewModel.ClearClipboardHistory();
            ToastRequested?.Invoke(this, "Clipboard history cleared");
        }

        #endregion
    }
}
