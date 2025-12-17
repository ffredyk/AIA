using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AIA.Models;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of data asset service that bridges to OverlayViewModel
    /// </summary>
    public class HostDataAssetService : IDataAssetService
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public event EventHandler<DataAssetsChangedEventArgs>? DataAssetsChanged;

        public HostDataAssetService(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        private OverlayViewModel ViewModel => _viewModelProvider();

        public IReadOnlyList<IDataAsset> GetCurrentAssets()
        {
            return ViewModel.CurrentDataAssets.Select(a => new DataAssetAdapter(a)).ToList();
        }

        public void CaptureCurrentAssets()
        {
            ViewModel.CaptureCurrentDataAssets();
            DataAssetsChanged?.Invoke(this, new DataAssetsChangedEventArgs(DataAssetChangeType.Captured));
        }

        public bool CopyToClipboard(IDataAsset asset)
        {
            var actualAsset = ViewModel.CurrentDataAssets.FirstOrDefault(a => a.Id == asset.Id);
            if (actualAsset == null) return false;

            return ViewModel.CopyDataAssetToClipboard(actualAsset);
        }

        public string? SaveToFile(IDataAsset asset)
        {
            var actualAsset = ViewModel.CurrentDataAssets.FirstOrDefault(a => a.Id == asset.Id);
            if (actualAsset == null) return null;

            return ViewModel.SaveDataAssetToFile(actualAsset);
        }

        public string? SaveToFileWithDialog(IDataAsset asset)
        {
            var actualAsset = ViewModel.CurrentDataAssets.FirstOrDefault(a => a.Id == asset.Id);
            if (actualAsset == null) return null;

            return ViewModel.SaveDataAssetWithDialog(actualAsset);
        }

        public async Task<bool> SaveToDataBankAsync(IDataAsset asset, Guid categoryId)
        {
            var actualAsset = ViewModel.CurrentDataAssets.FirstOrDefault(a => a.Id == asset.Id);
            if (actualAsset == null) return false;

            var category = ViewModel.DataBankCategories.FirstOrDefault(c => c.Id == categoryId);
            if (category == null) return false;

            return await ViewModel.SaveDataAssetToDataBankAsync(actualAsset, category);
        }
    }

    internal class DataAssetAdapter : IDataAsset
    {
        private readonly DataAsset _asset;

        public DataAssetAdapter(DataAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        public Guid Id => _asset.Id;
        public string Name => _asset.Name;
        public string Description => _asset.Description;
        public BitmapSource? Thumbnail => _asset.Thumbnail;
        public BitmapSource? FullImage => _asset.FullImage;
        public DateTime CapturedAt => _asset.CapturedAt;
        public DataAssetItemType AssetType => (DataAssetItemType)(int)_asset.AssetType;
    }
}
