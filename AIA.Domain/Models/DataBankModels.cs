using System;
using System.Collections.Generic;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Represents a data bank category
    /// </summary>
    public class DataBankCategory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#0078D4";
        public int EntryCount { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
    }

    /// <summary>
    /// Represents a data bank entry
    /// </summary>
    public class DataBankEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DataEntryType EntryType { get; set; } = DataEntryType.Text;
        public string? FilePath { get; set; }
        public string? OriginalFileName { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public Guid CategoryId { get; set; }
        public string Tags { get; set; } = string.Empty;
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
        public bool FileUploadPending { get; set; }
    }

    public enum DataEntryType
    {
        Text,
        TextFile,
        Pdf,
        Email,
        Image,
        Custom
    }
}
