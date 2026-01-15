using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace AIA.Models
{
    /// <summary>
    /// Represents a captured data asset (screenshot of screen or window, or clipboard content)
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
        
        // Clipboard-specific fields
        private string? _textContent;
        private List<string>? _filePaths;

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
                    OnPropertyChanged(nameof(IsClipboardAsset));
                    OnPropertyChanged(nameof(IsScreenshotAsset));
                }
            }
        }

        public string AssetTypeIcon => AssetType switch
        {
            DataAssetType.FullScreen => "DesktopMac20",
            DataAssetType.ActiveWindow => "WindowNew20",
            DataAssetType.ClipboardText => "ClipboardText32",
            DataAssetType.ClipboardImage => "Image20",
            DataAssetType.ClipboardFiles => "FolderOpen20",
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

        /// <summary>
        /// Text content for clipboard text assets
        /// </summary>
        public string? TextContent
        {
            get => _textContent;
            set
            {
                if (_textContent != value)
                {
                    _textContent = value;
                    OnPropertyChanged(nameof(TextContent));
                    OnPropertyChanged(nameof(PreviewText));
                }
            }
        }

        /// <summary>
        /// File paths for clipboard file assets
        /// </summary>
        public List<string>? FilePaths
        {
            get => _filePaths;
            set
            {
                if (_filePaths != value)
                {
                    _filePaths = value;
                    OnPropertyChanged(nameof(FilePaths));
                    OnPropertyChanged(nameof(PreviewText));
                }
            }
        }

        /// <summary>
        /// Preview text for display (truncated for long content)
        /// </summary>
        public string PreviewText
        {
            get
            {
                if (AssetType == DataAssetType.ClipboardText && !string.IsNullOrEmpty(TextContent))
                {
                    var preview = TextContent.Length > 100 
                        ? TextContent.Substring(0, 100) + "..." 
                        : TextContent;
                    return preview.Replace("\r\n", " ").Replace("\n", " ");
                }
                
                if (AssetType == DataAssetType.ClipboardFiles && FilePaths?.Count > 0)
                {
                    if (FilePaths.Count == 1)
                        return System.IO.Path.GetFileName(FilePaths[0]);
                    return $"{FilePaths.Count} files";
                }

                return Description;
            }
        }

        /// <summary>
        /// Whether this is a clipboard asset
        /// </summary>
        public bool IsClipboardAsset => AssetType is DataAssetType.ClipboardText 
            or DataAssetType.ClipboardImage 
            or DataAssetType.ClipboardFiles;

        /// <summary>
        /// Whether this is a screenshot asset
        /// </summary>
        public bool IsScreenshotAsset => AssetType is DataAssetType.FullScreen 
            or DataAssetType.ActiveWindow;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum DataAssetType
    {
        FullScreen,
        ActiveWindow,
        ClipboardText,
        ClipboardImage,
        ClipboardFiles
    }
}
