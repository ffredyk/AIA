using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace AIA.Models
{
    public enum DataEntryType
    {
        Text,
        TextFile,
        Pdf,
        Email,
        Image,
        Custom
    }

    public class DataBankEntry : INotifyPropertyChanged
    {
        private Guid _id;
        private string _title = string.Empty;
        private string _content = string.Empty;
        private DataEntryType _entryType = DataEntryType.Text;
        private string? _filePath;
        private string? _originalFileName;
        private long _fileSize;
        private DateTime _createdDate;
        private DateTime _modifiedDate;
        private Guid _categoryId;
        private string _tags = string.Empty;
        private BitmapImage? _previewImage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string Title
        {
            get => _title;
            set 
            { 
                _title = value; 
                OnPropertyChanged(nameof(Title));
                ModifiedDate = DateTime.Now;
            }
        }

        public string Content
        {
            get => _content;
            set 
            { 
                _content = value; 
                OnPropertyChanged(nameof(Content));
                OnPropertyChanged(nameof(ContentPreview));
                ModifiedDate = DateTime.Now;
            }
        }

        public DataEntryType EntryType
        {
            get => _entryType;
            set 
            { 
                _entryType = value; 
                OnPropertyChanged(nameof(EntryType));
                OnPropertyChanged(nameof(EntryTypeIcon));
                OnPropertyChanged(nameof(IsImageEntry));
                OnPropertyChanged(nameof(IsTextBasedEntry));
                OnPropertyChanged(nameof(IsPdfEntry));
            }
        }

        public string? FilePath
        {
            get => _filePath;
            set 
            { 
                _filePath = value; 
                OnPropertyChanged(nameof(FilePath));
                LoadPreviewImage();
            }
        }

        public string? OriginalFileName
        {
            get => _originalFileName;
            set { _originalFileName = value; OnPropertyChanged(nameof(OriginalFileName)); }
        }

        public long FileSize
        {
            get => _fileSize;
            set 
            { 
                _fileSize = value; 
                OnPropertyChanged(nameof(FileSize));
                OnPropertyChanged(nameof(FileSizeText));
            }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(nameof(CreatedDate)); }
        }

        public DateTime ModifiedDate
        {
            get => _modifiedDate;
            set { _modifiedDate = value; OnPropertyChanged(nameof(ModifiedDate)); }
        }

        public Guid CategoryId
        {
            get => _categoryId;
            set { _categoryId = value; OnPropertyChanged(nameof(CategoryId)); }
        }

        public string Tags
        {
            get => _tags;
            set { _tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        // Preview image for image entries - excluded from JSON serialization
        [JsonIgnore]
        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            private set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        // Computed properties - excluded from JSON serialization
        [JsonIgnore]
        public string ContentPreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content))
                    return "No content";
                
                var preview = Content.Length > 100 
                    ? Content.Substring(0, 100) + "..." 
                    : Content;
                return preview.Replace("\r\n", " ").Replace("\n", " ");
            }
        }

        [JsonIgnore]
        public string EntryTypeIcon => EntryType switch
        {
            DataEntryType.Text => "Document20",
            DataEntryType.TextFile => "DocumentText20",
            DataEntryType.Pdf => "DocumentPdf20",
            DataEntryType.Email => "Mail20",
            DataEntryType.Image => "Image20",
            DataEntryType.Custom => "DocumentBulletList20",
            _ => "Document20"
        };

        [JsonIgnore]
        public string FileSizeText
        {
            get
            {
                if (FileSize <= 0) return string.Empty;
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024.0 * 1024):F1} MB";
                return $"{FileSize / (1024.0 * 1024 * 1024):F2} GB";
            }
        }

        // Type checking properties for UI binding - excluded from JSON serialization
        [JsonIgnore]
        public bool IsImageEntry => EntryType == DataEntryType.Image;
        
        [JsonIgnore]
        public bool IsTextBasedEntry => EntryType == DataEntryType.Text || 
                                         EntryType == DataEntryType.TextFile || 
                                         EntryType == DataEntryType.Email ||
                                         EntryType == DataEntryType.Custom;
        
        [JsonIgnore]
        public bool IsPdfEntry => EntryType == DataEntryType.Pdf;

        public DataBankEntry()
        {
            Id = Guid.NewGuid();
            CreatedDate = DateTime.Now;
            ModifiedDate = DateTime.Now;
        }

        private void LoadPreviewImage()
        {
            if (EntryType != DataEntryType.Image || string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                PreviewImage = null;
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(FilePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 800; // Limit size for performance
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread-safe
                PreviewImage = bitmap;
            }
            catch
            {
                PreviewImage = null;
            }
        }

        public void RefreshPreview()
        {
            LoadPreviewImage();
            OnPropertyChanged(nameof(IsImageEntry));
            OnPropertyChanged(nameof(IsTextBasedEntry));
            OnPropertyChanged(nameof(IsPdfEntry));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
