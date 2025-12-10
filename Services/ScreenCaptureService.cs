using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service for capturing screenshots of the screen and windows
    /// </summary>
    public class ScreenCaptureService
    {
        #region Native Methods

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint GW_HWNDNEXT = 2;
        private const int DWMWA_CLOAKED = 14;
        private const int SRCCOPY = 0x00CC0020;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        #endregion

        private readonly List<IntPtr> _recentActiveWindows = new();
        private readonly object _lockObject = new();
        private IntPtr _overlayWindowHandle = IntPtr.Zero;

        // Directory for saving screenshots
        private static readonly string ScreenshotsFolder;

        static ScreenCaptureService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            ScreenshotsFolder = Path.Combine(exeDirectory, "screenshots");
        }

        /// <summary>
        /// Sets the handle of the overlay window to exclude from captures
        /// </summary>
        public void SetOverlayWindowHandle(IntPtr handle)
        {
            _overlayWindowHandle = handle;
        }

        /// <summary>
        /// Tracks the currently active window (call this periodically when overlay is hidden)
        /// </summary>
        public void TrackActiveWindow()
        {
            var foregroundWindow = GetForegroundWindow();
            
            // Skip if it's our overlay or invalid
            if (foregroundWindow == IntPtr.Zero || foregroundWindow == _overlayWindowHandle)
                return;

            // Skip if the window is not visible or minimized
            if (!IsWindowVisible(foregroundWindow) || IsIconic(foregroundWindow))
                return;

            // Get window title to verify it's a real window
            var title = GetWindowTitle(foregroundWindow);
            if (string.IsNullOrWhiteSpace(title))
                return;

            lock (_lockObject)
            {
                // Remove if already in list (we'll add to front)
                _recentActiveWindows.RemoveAll(h => h == foregroundWindow);
                
                // Add to front
                _recentActiveWindows.Insert(0, foregroundWindow);
                
                // Keep only last 10 windows
                while (_recentActiveWindows.Count > 10)
                {
                    _recentActiveWindows.RemoveAt(_recentActiveWindows.Count - 1);
                }
            }
        }

        /// <summary>
        /// Captures the entire screen
        /// </summary>
        public DataAsset? CaptureFullScreen()
        {
            try
            {
                var screenBounds = GetPrimaryScreenBounds();
                using var bitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                
                graphics.CopyFromScreen(screenBounds.Left, screenBounds.Top, 0, 0, 
                    new System.Drawing.Size(screenBounds.Width, screenBounds.Height), CopyPixelOperation.SourceCopy);

                var bitmapSource = ConvertToBitmapSource(bitmap);
                var thumbnail = CreateThumbnail(bitmapSource, 120, 80);

                return new DataAsset
                {
                    Name = "Full Screen",
                    Description = $"Screen capture at {DateTime.Now:HH:mm:ss}",
                    FullImage = bitmapSource,
                    Thumbnail = thumbnail,
                    CapturedAt = DateTime.Now,
                    AssetType = DataAssetType.FullScreen
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Captures the last N active windows (excluding overlay)
        /// </summary>
        public List<DataAsset> CaptureRecentActiveWindows(int count = 3)
        {
            var assets = new List<DataAsset>();
            List<IntPtr> windowsToCapture;

            lock (_lockObject)
            {
                // Filter out windows that are no longer valid
                var validWindows = _recentActiveWindows
                    .Where(h => IsWindowValid(h) && h != _overlayWindowHandle)
                    .Take(count)
                    .ToList();
                
                windowsToCapture = validWindows;
            }

            foreach (var hwnd in windowsToCapture)
            {
                var asset = CaptureWindow(hwnd);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        /// <summary>
        /// Captures all data assets: full screen + last 3 active windows
        /// </summary>
        public List<DataAsset> CaptureAllDataAssets()
        {
            var assets = new List<DataAsset>();

            // Capture full screen
            var fullScreen = CaptureFullScreen();
            if (fullScreen != null)
            {
                assets.Add(fullScreen);
            }

            // Capture recent active windows
            var windowAssets = CaptureRecentActiveWindows(3);
            assets.AddRange(windowAssets);

            return assets;
        }

        #region Save Methods

        /// <summary>
        /// Copies the data asset image to clipboard
        /// </summary>
        public static bool CopyToClipboard(DataAsset asset)
        {
            if (asset.FullImage == null)
                return false;

            try
            {
                System.Windows.Clipboard.SetImage(asset.FullImage);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Saves the data asset to a file on disk
        /// </summary>
        public static string? SaveToFile(DataAsset asset, string? customPath = null)
        {
            if (asset.FullImage == null)
                return null;

            try
            {
                string filePath;
                
                if (!string.IsNullOrEmpty(customPath))
                {
                    filePath = customPath;
                }
                else
                {
                    // Ensure screenshots directory exists
                    if (!Directory.Exists(ScreenshotsFolder))
                        Directory.CreateDirectory(ScreenshotsFolder);

                    var fileName = $"{asset.AssetType}_{asset.CapturedAt:yyyyMMdd_HHmmss}_{asset.Id:N}.png";
                    filePath = Path.Combine(ScreenshotsFolder, fileName);
                }

                // Ensure directory exists for custom paths
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var fileStream = new FileStream(filePath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(asset.FullImage));
                encoder.Save(fileStream);

                return filePath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves the data asset with a file dialog
        /// </summary>
        public static string? SaveToFileWithDialog(DataAsset asset)
        {
            if (asset.FullImage == null)
                return null;

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Screenshot",
                FileName = $"{asset.AssetType}_{asset.CapturedAt:yyyyMMdd_HHmmss}",
                DefaultExt = ".png",
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp|All Files|*.*"
            };

            if (saveDialog.ShowDialog() != true)
                return null;

            try
            {
                using var fileStream = new FileStream(saveDialog.FileName, FileMode.Create);
                
                BitmapEncoder encoder = Path.GetExtension(saveDialog.FileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                
                encoder.Frames.Add(BitmapFrame.Create(asset.FullImage));
                encoder.Save(fileStream);

                return saveDialog.FileName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a DataBankEntry from a DataAsset for saving to data banks
        /// </summary>
        public static async Task<DataBankEntry?> CreateDataBankEntryAsync(DataAsset asset, Guid categoryId)
        {
            if (asset.FullImage == null)
                return null;

            try
            {
                // First save the image to the databanks/files folder
                var dataBankFilesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "databanks", "files");
                if (!Directory.Exists(dataBankFilesFolder))
                    Directory.CreateDirectory(dataBankFilesFolder);

                var fileName = $"{Guid.NewGuid()}_{asset.AssetType}_{asset.CapturedAt:yyyyMMdd_HHmmss}.png";
                var filePath = Path.Combine(dataBankFilesFolder, fileName);

                // Save the image
                await Task.Run(() =>
                {
                    using var fileStream = new FileStream(filePath, FileMode.Create);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(asset.FullImage));
                    encoder.Save(fileStream);
                });

                var fileInfo = new FileInfo(filePath);

                var entry = new DataBankEntry
                {
                    Title = asset.Name,
                    Content = $"Screenshot captured at {asset.CapturedAt:yyyy-MM-dd HH:mm:ss}\nType: {asset.AssetType}\nDescription: {asset.Description}",
                    EntryType = DataEntryType.Image,
                    FilePath = filePath,
                    OriginalFileName = fileName,
                    FileSize = fileInfo.Length,
                    CategoryId = categoryId,
                    Tags = asset.AssetType == DataAssetType.FullScreen ? "screenshot,fullscreen" : "screenshot,window"
                };

                entry.RefreshPreview();
                return entry;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        private DataAsset? CaptureWindow(IntPtr hwnd)
        {
            try
            {
                if (!GetWindowRect(hwnd, out RECT rect))
                    return null;

                var width = rect.Width;
                var height = rect.Height;

                if (width <= 0 || height <= 0)
                    return null;

                // Try PrintWindow first for better results
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                
                var hdc = graphics.GetHdc();
                bool success = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                graphics.ReleaseHdc(hdc);

                if (!success)
                {
                    // Fallback to screen copy
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                        new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
                }

                var bitmapSource = ConvertToBitmapSource(bitmap);
                var thumbnail = CreateThumbnail(bitmapSource, 120, 80);
                var windowTitle = GetWindowTitle(hwnd);

                return new DataAsset
                {
                    Name = TruncateTitle(windowTitle, 30),
                    Description = windowTitle,
                    FullImage = bitmapSource,
                    Thumbnail = thumbnail,
                    CapturedAt = DateTime.Now,
                    AssetType = DataAssetType.ActiveWindow,
                    WindowHandle = hwnd
                };
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static BitmapSource CreateThumbnail(BitmapSource source, int maxWidth, int maxHeight)
        {
            double scaleX = (double)maxWidth / source.PixelWidth;
            double scaleY = (double)maxHeight / source.PixelHeight;
            double scale = Math.Min(scaleX, scaleY);

            var scaledBitmap = new TransformedBitmap(source, 
                new System.Windows.Media.ScaleTransform(scale, scale));
            
            // Create a frozen copy
            var thumbnail = BitmapFrame.Create(scaledBitmap);
            thumbnail.Freeze();
            return thumbnail;
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length == 0)
                return string.Empty;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string TruncateTitle(string title, int maxLength)
        {
            if (string.IsNullOrEmpty(title))
                return "Unknown Window";

            if (title.Length <= maxLength)
                return title;

            return title[..(maxLength - 3)] + "...";
        }

        private bool IsWindowValid(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            if (!IsWindowVisible(hwnd))
                return false;

            if (IsIconic(hwnd))
                return false;

            // Check if window is cloaked (invisible in DWM)
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out bool isCloaked, sizeof(int)) == 0 && isCloaked)
                return false;

            return true;
        }

        private static RECT GetPrimaryScreenBounds()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            return new RECT
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Bottom
            };
        }
    }
}
