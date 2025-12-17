using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for managing data assets (screenshots)
    /// </summary>
    public interface IDataAssetService
    {
        /// <summary>
        /// Gets current captured data assets (read permission required)
        /// </summary>
        IReadOnlyList<IDataAsset> GetCurrentAssets();

        /// <summary>
        /// Captures current screen and windows
        /// </summary>
        void CaptureCurrentAssets();

        /// <summary>
        /// Copies a data asset to clipboard
        /// </summary>
        bool CopyToClipboard(IDataAsset asset);

        /// <summary>
        /// Saves a data asset to a file
        /// </summary>
        string? SaveToFile(IDataAsset asset);

        /// <summary>
        /// Saves a data asset to a file with a save dialog
        /// </summary>
        string? SaveToFileWithDialog(IDataAsset asset);

        /// <summary>
        /// Saves a data asset to a data bank category (write permission required for both data assets and data banks)
        /// </summary>
        Task<bool> SaveToDataBankAsync(IDataAsset asset, Guid categoryId);

        /// <summary>
        /// Event fired when data assets change
        /// </summary>
        event EventHandler<DataAssetsChangedEventArgs>? DataAssetsChanged;
    }

    /// <summary>
    /// Data asset interface
    /// </summary>
    public interface IDataAsset
    {
        Guid Id { get; }
        string Name { get; }
        string Description { get; }
        BitmapSource? Thumbnail { get; }
        BitmapSource? FullImage { get; }
        DateTime CapturedAt { get; }
        DataAssetItemType AssetType { get; }
    }

    public enum DataAssetItemType
    {
        FullScreen,
        ActiveWindow
    }

    public class DataAssetsChangedEventArgs : EventArgs
    {
        public DataAssetChangeType ChangeType { get; }

        public DataAssetsChangedEventArgs(DataAssetChangeType changeType)
        {
            ChangeType = changeType;
        }
    }

    public enum DataAssetChangeType
    {
        Captured,
        Cleared
    }
}
