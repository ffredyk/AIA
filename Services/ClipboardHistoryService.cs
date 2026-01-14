using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIA.Models;

// Resolve ambiguous references
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace AIA.Services
{
    /// <summary>
    /// Service for tracking and managing clipboard history
    /// </summary>
    public class ClipboardHistoryService : IDisposable
    {
        private readonly DispatcherTimer _clipboardTimer;
        private readonly object _lockObject = new();
        private readonly Func<AppSettings> _getSettings;
        
        private string _lastClipboardSignature = string.Empty;
        private bool _isPaused;
        private bool _isDisposed;

        /// <summary>
        /// Collection of clipboard history items
        /// </summary>
        public ObservableCollection<DataAsset> ClipboardHistory { get; } = new();

        /// <summary>
        /// Event fired when a new clipboard item is captured
        /// </summary>
        public event EventHandler<DataAsset>? ClipboardCaptured;

        public ClipboardHistoryService(Func<AppSettings> getSettings)
        {
            _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            
            _clipboardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _clipboardTimer.Tick += OnClipboardTimerTick;
        }

        /// <summary>
        /// Starts clipboard monitoring
        /// </summary>
        public void Start()
        {
            if (_isDisposed) return;
            _clipboardTimer.Start();
        }

        /// <summary>
        /// Stops clipboard monitoring
        /// </summary>
        public void Stop()
        {
            _clipboardTimer.Stop();
        }

        /// <summary>
        /// Pauses clipboard monitoring (e.g., when overlay is visible)
        /// </summary>
        public void Pause()
        {
            _isPaused = true;
        }

        /// <summary>
        /// Resumes clipboard monitoring
        /// </summary>
        public void Resume()
        {
            _isPaused = false;
        }

        /// <summary>
        /// Clears all clipboard history
        /// </summary>
        public void ClearHistory()
        {
            lock (_lockObject)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    ClipboardHistory.Clear();
                });
            }
        }

        /// <summary>
        /// Copies a clipboard history item back to the clipboard
        /// </summary>
        public bool RestoreToClipboard(DataAsset item)
        {
            if (item == null) return false;

            try
            {
                // Pause monitoring to avoid re-capturing what we're restoring
                var wasPaused = _isPaused;
                _isPaused = true;

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    switch (item.AssetType)
                    {
                        case DataAssetType.ClipboardText when !string.IsNullOrEmpty(item.TextContent):
                            WpfClipboard.SetText(item.TextContent);
                            break;
                            
                        case DataAssetType.ClipboardImage when item.FullImage != null:
                            WpfClipboard.SetImage(item.FullImage);
                            break;
                            
                        case DataAssetType.ClipboardFiles when item.FilePaths?.Count > 0:
                            var fileDropList = new System.Collections.Specialized.StringCollection();
                            fileDropList.AddRange(item.FilePaths.ToArray());
                            WpfClipboard.SetFileDropList(fileDropList);
                            break;
                    }
                });

                // Update last signature to prevent re-capture
                _lastClipboardSignature = GetClipboardSignature();
                
                // Restore pause state after a short delay
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _isPaused = wasPaused;
                };
                timer.Start();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore clipboard: {ex.Message}");
                return false;
            }
        }

        private void OnClipboardTimerTick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            var settings = _getSettings();
            if (!settings.EnableClipboardHistory) return;

            try
            {
                var signature = GetClipboardSignature();
                if (signature == _lastClipboardSignature) return;

                _lastClipboardSignature = signature;

                CaptureClipboardContent(settings);
            }
            catch (Exception ex)
            {
                // Clipboard access can fail if another app has it locked
                System.Diagnostics.Debug.WriteLine($"Clipboard access error: {ex.Message}");
            }
        }

        private string GetClipboardSignature()
        {
            try
            {
                if (WpfClipboard.ContainsText())
                {
                    var text = WpfClipboard.GetText();
                    return $"TEXT:{text.GetHashCode()}";
                }
                
                if (WpfClipboard.ContainsImage())
                {
                    var image = WpfClipboard.GetImage();
                    if (image != null)
                    {
                        return $"IMAGE:{image.PixelWidth}x{image.PixelHeight}:{image.GetHashCode()}";
                    }
                }
                
                if (WpfClipboard.ContainsFileDropList())
                {
                    var files = WpfClipboard.GetFileDropList();
                    return $"FILES:{string.Join(",", files.Cast<string>()).GetHashCode()}";
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CaptureClipboardContent(AppSettings settings)
        {
            DataAsset? asset = null;

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Check for text
                    if (settings.TrackClipboardText && WpfClipboard.ContainsText())
                    {
                        var text = WpfClipboard.GetText();
                        
                        // Check size limit
                        var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(text);
                        if (sizeBytes > settings.MaxClipboardItemSizeKb * 1024)
                        {
                            System.Diagnostics.Debug.WriteLine($"Clipboard text too large ({sizeBytes} bytes), skipping");
                            return;
                        }

                        asset = new DataAsset
                        {
                            AssetType = DataAssetType.ClipboardText,
                            Name = GetTextPreviewName(text),
                            Description = text.Length > 50 ? text.Substring(0, 50) + "..." : text,
                            TextContent = text,
                            CapturedAt = DateTime.Now
                        };
                    }
                    // Check for image
                    else if (settings.TrackClipboardImages && WpfClipboard.ContainsImage())
                    {
                        var image = WpfClipboard.GetImage();
                        if (image != null)
                        {
                            // Check approximate size limit
                            var approxSizeBytes = image.PixelWidth * image.PixelHeight * 4; // RGBA
                            if (approxSizeBytes > settings.MaxClipboardItemSizeKb * 1024)
                            {
                                System.Diagnostics.Debug.WriteLine($"Clipboard image too large (~{approxSizeBytes} bytes), skipping");
                                return;
                            }

                            asset = new DataAsset
                            {
                                AssetType = DataAssetType.ClipboardImage,
                                Name = $"Image ({image.PixelWidth}x{image.PixelHeight})",
                                Description = $"{image.PixelWidth} × {image.PixelHeight} pixels",
                                FullImage = image,
                                Thumbnail = CreateThumbnail(image),
                                CapturedAt = DateTime.Now
                            };
                        }
                    }
                    // Check for files
                    else if (settings.TrackClipboardFiles && WpfClipboard.ContainsFileDropList())
                    {
                        var files = WpfClipboard.GetFileDropList();
                        if (files.Count > 0)
                        {
                            var fileList = files.Cast<string>().ToList();
                            
                            asset = new DataAsset
                            {
                                AssetType = DataAssetType.ClipboardFiles,
                                Name = fileList.Count == 1 
                                    ? Path.GetFileName(fileList[0]) 
                                    : $"{fileList.Count} files",
                                Description = fileList.Count == 1 
                                    ? fileList[0] 
                                    : string.Join(", ", fileList.Select(Path.GetFileName)),
                                FilePaths = fileList,
                                CapturedAt = DateTime.Now
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error capturing clipboard: {ex.Message}");
                }
            });

            if (asset != null)
            {
                AddToHistory(asset, settings);
            }
        }

        private void AddToHistory(DataAsset asset, AppSettings settings)
        {
            lock (_lockObject)
            {
                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    // Remove duplicate if exists (same content)
                    var duplicate = FindDuplicate(asset);
                    if (duplicate != null)
                    {
                        ClipboardHistory.Remove(duplicate);
                    }

                    // Add to beginning of list
                    ClipboardHistory.Insert(0, asset);

                    // Trim to max items
                    while (ClipboardHistory.Count > settings.MaxClipboardHistoryItems)
                    {
                        ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
                    }
                });
            }

            ClipboardCaptured?.Invoke(this, asset);
        }

        private DataAsset? FindDuplicate(DataAsset asset)
        {
            return asset.AssetType switch
            {
                DataAssetType.ClipboardText => ClipboardHistory.FirstOrDefault(h => 
                    h.AssetType == DataAssetType.ClipboardText && h.TextContent == asset.TextContent),
                    
                DataAssetType.ClipboardFiles => ClipboardHistory.FirstOrDefault(h => 
                    h.AssetType == DataAssetType.ClipboardFiles && 
                    h.FilePaths != null && asset.FilePaths != null &&
                    h.FilePaths.SequenceEqual(asset.FilePaths)),
                    
                // Images are harder to compare, skip duplicate detection for now
                _ => null
            };
        }

        private static string GetTextPreviewName(string text)
        {
            // Get first line or first N characters for name
            var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
            firstLine = firstLine.Trim();
            
            if (firstLine.Length > 40)
            {
                firstLine = firstLine.Substring(0, 40) + "...";
            }

            return string.IsNullOrWhiteSpace(firstLine) ? "Text" : firstLine;
        }

        private static BitmapSource? CreateThumbnail(BitmapSource source)
        {
            if (source == null) return null;

            try
            {
                // Create a thumbnail that fits within 150x100
                double scale = Math.Min(150.0 / source.PixelWidth, 100.0 / source.PixelHeight);
                scale = Math.Min(scale, 1.0); // Don't upscale

                if (Math.Abs(scale - 1.0) < 0.01)
                {
                    return source; // Already small enough
                }

                var thumbnail = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                
                // Freeze for cross-thread access
                if (thumbnail.CanFreeze)
                {
                    thumbnail.Freeze();
                }

                return thumbnail;
            }
            catch
            {
                return source;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
        }
    }
}
