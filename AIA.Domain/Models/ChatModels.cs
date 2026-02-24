using System;
using System.Collections.Generic;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Represents a chat session
    /// </summary>
    public class ChatSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ChatTitle { get; set; } = "New Chat";
        public string ChatSummary { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public List<ChatMessage> Messages { get; set; } = new();
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
    }

    /// <summary>
    /// Represents a chat message
    /// </summary>
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<ChatMessageAttachment>? Attachments { get; set; }
    }

    /// <summary>
    /// Represents an attachment in a chat message (image, file, etc.)
    /// </summary>
    public class ChatMessageAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Type { get; set; } = "image"; // image, file, etc.
        public string? FilePath { get; set; }
        public string? Base64Data { get; set; }
        public string? MimeType { get; set; }
        public string? Description { get; set; }
    }
}
