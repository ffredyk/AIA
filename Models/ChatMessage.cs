using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AIA.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _role;
        private string _content;
        private ObservableCollection<ChatImageAttachment> _attachedImages = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public string Role
        {
            get => _role;
            set
            {
                if (_role != value)
                {
                    _role = value;
                    OnPropertyChanged(nameof(Role));
                }
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged(nameof(Content));
                }
            }
        }

        /// <summary>
        /// Images attached to this message
        /// </summary>
        public ObservableCollection<ChatImageAttachment> AttachedImages
        {
            get => _attachedImages;
            set
            {
                if (_attachedImages != value)
                {
                    _attachedImages = value;
                    OnPropertyChanged(nameof(AttachedImages));
                    OnPropertyChanged(nameof(HasAttachedImages));
                    OnPropertyChanged(nameof(AttachedImagesCount));
                }
            }
        }

        /// <summary>
        /// Whether this message has attached images
        /// </summary>
        public bool HasAttachedImages => AttachedImages?.Count > 0;

        /// <summary>
        /// Number of attached images
        /// </summary>
        public int AttachedImagesCount => AttachedImages?.Count ?? 0;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents an image attached to a chat message
    /// </summary>
    public class ChatImageAttachment : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private BitmapSource? _thumbnail;
        private BitmapSource? _fullImage;
        private string _mimeType = "image/png";
        private DateTime _capturedAt = DateTime.Now;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); }
        }

        public BitmapSource? FullImage
        {
            get => _fullImage;
            set
            {
                _fullImage = value;
                OnPropertyChanged(nameof(FullImage));
                OnPropertyChanged(nameof(ImageWidth));
                OnPropertyChanged(nameof(ImageHeight));
                OnPropertyChanged(nameof(ImageSizeText));
            }
        }

        public string MimeType
        {
            get => _mimeType;
            set { _mimeType = value; OnPropertyChanged(nameof(MimeType)); }
        }

        public DateTime CapturedAt
        {
            get => _capturedAt;
            set { _capturedAt = value; OnPropertyChanged(nameof(CapturedAt)); }
        }

        public int ImageWidth => FullImage?.PixelWidth ?? 0;
        public int ImageHeight => FullImage?.PixelHeight ?? 0;
        public string ImageSizeText => $"{ImageWidth}×{ImageHeight}";

        /// <summary>
        /// Creates a thumbnail from the full image
        /// </summary>
        public void CreateThumbnail(int maxSize = 80)
        {
            if (FullImage == null) return;

            try
            {
                double scale = Math.Min(
                    (double)maxSize / FullImage.PixelWidth,
                    (double)maxSize / FullImage.PixelHeight);

                if (scale >= 1)
                {
                    Thumbnail = FullImage;
                    return;
                }

                var scaledWidth = (int)(FullImage.PixelWidth * scale);
                var scaledHeight = (int)(FullImage.PixelHeight * scale);

                var transform = new System.Windows.Media.ScaleTransform(scale, scale);
                var scaledBitmap = new TransformedBitmap(FullImage, transform);

                // Freeze for thread safety
                if (scaledBitmap.CanFreeze)
                    scaledBitmap.Freeze();

                Thumbnail = scaledBitmap;
            }
            catch
            {
                Thumbnail = FullImage;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
