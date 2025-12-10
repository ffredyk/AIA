using System;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace AIA.Models
{
    /// <summary>
    /// Represents a captured data asset (screenshot of screen or window)
    /// </summary>
    public class DataAsset : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _description = string.Empty;
        private BitmapSource? _thumbnail;
        private BitmapSource? _fullImage;
        private DateTime _capturedAt;
        private DataAssetType _assetType;
        private IntPtr _windowHandle;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged(nameof(Thumbnail));
                }
            }
        }

        public BitmapSource? FullImage
        {
            get => _fullImage;
            set
            {
                if (_fullImage != value)
                {
                    _fullImage = value;
                    OnPropertyChanged(nameof(FullImage));
                }
            }
        }

        public DateTime CapturedAt
        {
            get => _capturedAt;
            set
            {
                if (_capturedAt != value)
                {
                    _capturedAt = value;
                    OnPropertyChanged(nameof(CapturedAt));
                    OnPropertyChanged(nameof(CapturedAtText));
                }
            }
        }

        public string CapturedAtText => CapturedAt.ToString("HH:mm:ss");

        public DataAssetType AssetType
        {
            get => _assetType;
            set
            {
                if (_assetType != value)
                {
                    _assetType = value;
                    OnPropertyChanged(nameof(AssetType));
                    OnPropertyChanged(nameof(AssetTypeIcon));
                }
            }
        }

        public string AssetTypeIcon => AssetType switch
        {
            DataAssetType.FullScreen => "DesktopMac20",
            DataAssetType.ActiveWindow => "WindowNew20",
            _ => "Image20"
        };

        public IntPtr WindowHandle
        {
            get => _windowHandle;
            set
            {
                if (_windowHandle != value)
                {
                    _windowHandle = value;
                    OnPropertyChanged(nameof(WindowHandle));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum DataAssetType
    {
        FullScreen,
        ActiveWindow
    }
}
