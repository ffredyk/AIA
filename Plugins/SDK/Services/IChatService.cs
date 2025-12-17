using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIA.Plugins.SDK
{
    /// <summary>
    /// Service for managing chat sessions
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// Gets all chat sessions (read permission required)
        /// </summary>
        IReadOnlyList<IChatSession> GetAllSessions();

        /// <summary>
        /// Gets the currently selected chat session
        /// </summary>
        IChatSession? GetSelectedSession();

        /// <summary>
        /// Gets a chat session by ID
        /// </summary>
        IChatSession? GetSessionById(Guid id);

        /// <summary>
        /// Creates a new chat session (write permission required)
        /// </summary>
        IChatSession CreateSession(string? title = null);

        /// <summary>
        /// Deletes a chat session (write permission required)
        /// </summary>
        bool DeleteSession(Guid id);

        /// <summary>
        /// Adds a message to a chat session (write permission required)
        /// </summary>
        IChatMessage AddMessage(Guid sessionId, string content, ChatMessageRole role);

        /// <summary>
        /// Saves all chat sessions (write permission required)
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// Event fired when chat sessions change
        /// </summary>
        event EventHandler<ChatChangedEventArgs>? ChatChanged;
    }

    /// <summary>
    /// Chat session interface
    /// </summary>
    public interface IChatSession
    {
        Guid Id { get; }
        string ChatTitle { get; set; }
        string ChatSummary { get; set; }
        DateTime CreatedDate { get; }
        DateTime LastMessageDate { get; }
        IReadOnlyList<IChatMessage> Messages { get; }
    }

    /// <summary>
    /// Chat message interface
    /// </summary>
    public interface IChatMessage
    {
        Guid Id { get; }
        string Content { get; }
        ChatMessageRole Role { get; }
        DateTime Timestamp { get; }
    }

    public enum ChatMessageRole
    {
        User,
        Assistant,
        System
    }

    public class ChatChangedEventArgs : EventArgs
    {
        public ChatChangeType ChangeType { get; }
        public IChatSession? Session { get; }

        public ChatChangedEventArgs(ChatChangeType changeType, IChatSession? session = null)
        {
            ChangeType = changeType;
            Session = session;
        }
    }

    public enum ChatChangeType
    {
        SessionAdded,
        SessionUpdated,
        SessionDeleted,
        MessageAdded,
        Reloaded
    }
}
