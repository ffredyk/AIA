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
        private static readonly string DataBanksFolder;
        private static readonly string MetadataFile;
        private static readonly string FilesFolder;

        static DataBankService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            DataBanksFolder = Path.Combine(exeDirectory, "databanks");
            MetadataFile = Path.Combine(DataBanksFolder, "metadata.json");
            FilesFolder = Path.Combine(DataBanksFolder, "files");
        }

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
                var json = await File.ReadAllTextAsync(MetadataFile);
                var metadata = JsonSerializer.Deserialize<DataBankMetadata>(json);
                return metadata ?? new DataBankMetadata();
            }
            catch
            {
                return new DataBankMetadata();
            }
        }

        public static async Task SaveMetadataAsync(DataBankMetadata metadata)
        {
            EnsureDirectoriesExist();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(metadata, options);
            await File.WriteAllTextAsync(MetadataFile, json);
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

        private static Task<string> ExtractPdfTextAsync(string filePath)
        {
            // Basic PDF text extraction placeholder
            // In production, you'd use a library like iTextSharp or PdfPig
            return Task.FromResult($"[PDF Content from: {Path.GetFileName(filePath)}]");
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
