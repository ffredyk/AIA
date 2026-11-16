using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    public class DataBankService
    {
        private static string DataBanksFolder => AppPaths.DataBanksFolder;
        private static string MetadataFile => Path.Combine(DataBanksFolder, "metadata.json");
        private static string FilesFolder => AppPaths.DataBankFilesFolder;

        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(DataBanksFolder))
                Directory.CreateDirectory(DataBanksFolder);
            
            if (!Directory.Exists(FilesFolder))
                Directory.CreateDirectory(FilesFolder);
        }

        public static async Task<DataBankMetadata> LoadMetadataAsync()
        {
            EnsureDirectoriesExist();

            if (!File.Exists(MetadataFile))
            {
                return new DataBankMetadata();
            }

            try
            {
                var metadata = await JsonFileStore.ReadAsync<DataBankMetadata>(MetadataFile, GetJsonOptions());
                return metadata ?? new DataBankMetadata();
            }
            catch (Exception ex)
            {
                var quarantined = JsonFileStore.Quarantine(MetadataFile);
                System.Diagnostics.Debug.WriteLine($"Failed to load data bank metadata ({ex.Message}); preserved at '{quarantined}'.");
                return new DataBankMetadata();
            }
        }

        public static async Task SaveMetadataAsync(DataBankMetadata metadata)
        {
            EnsureDirectoriesExist();
            await JsonFileStore.WriteAsync(MetadataFile, metadata, GetJsonOptions());
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions { WriteIndented = true };
        }

        public static async Task<string> ImportFileAsync(string sourcePath, DataEntryType entryType)
        {
            EnsureDirectoriesExist();

            var fileName = Path.GetFileName(sourcePath);
            var uniqueName = $"{Guid.NewGuid()}_{fileName}";
            var destPath = Path.Combine(FilesFolder, uniqueName);

            await Task.Run(() => File.Copy(sourcePath, destPath));

            return destPath;
        }

        public static async Task<string> ReadFileContentAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" => 
                    await File.ReadAllTextAsync(filePath),
                ".pdf" => await ExtractPdfTextAsync(filePath),
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" =>
                    GetImageMetadata(filePath),
                _ => $"[Binary file: {Path.GetFileName(filePath)}]"
            };
        }

        private static async Task<string> ExtractPdfTextAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
                    var builder = new System.Text.StringBuilder();

                    foreach (var page in document.GetPages())
                    {
                        builder.AppendLine(page.Text);
                        builder.AppendLine();
                    }

                    var text = builder.ToString().Trim();
                    return string.IsNullOrWhiteSpace(text)
                        ? "[PDF contains no extractable text]"
                        : text;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PDF extraction failed for '{filePath}': {ex.Message}");
                    return $"[PDF text extraction failed: {ex.Message}]";
                }
            });
        }

        private static string GetImageMetadata(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                using var stream = File.OpenRead(filePath);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None);
                
                var frame = decoder.Frames[0];
                return $"Image: {frame.PixelWidth}x{frame.PixelHeight} pixels\n" +
                       $"Format: {Path.GetExtension(filePath).ToUpperInvariant().TrimStart('.')}\n" +
                       $"Size: {FormatFileSize(fileInfo.Length)}";
            }
            catch
            {
                return $"[Image file: {Path.GetFileName(filePath)}]";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public static void DeleteFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                // Log error in production
            }
        }

        public static DataEntryType DetermineEntryType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".txt" or ".md" or ".log" => DataEntryType.TextFile,
                ".pdf" => DataEntryType.Pdf,
                ".eml" or ".msg" => DataEntryType.Email,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" => DataEntryType.Image,
                _ => DataEntryType.Custom
            };
        }

        public static long GetFileSize(string filePath)
        {
            if (!File.Exists(filePath))
                return 0;

            return new FileInfo(filePath).Length;
        }

        public static bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif";
        }
    }

    public class DataBankMetadata
    {
        public List<DataBankCategory> Categories { get; set; } = new();
        public List<DataBankEntry> Entries { get; set; } = new();
    }
}
