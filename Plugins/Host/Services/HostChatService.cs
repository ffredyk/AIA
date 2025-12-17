using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Plugins.SDK;

namespace AIA.Plugins.Host.Services
{
    /// <summary>
    /// Host implementation of chat service that bridges to OverlayViewModel
    /// </summary>
    public class HostChatService : IChatService
    {
        private readonly Func<OverlayViewModel> _viewModelProvider;

        public event EventHandler<ChatChangedEventArgs>? ChatChanged;

        public HostChatService(Func<OverlayViewModel> viewModelProvider)
        {
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        private OverlayViewModel ViewModel => _viewModelProvider();

        public IReadOnlyList<IChatSession> GetAllSessions()
        {
            return ViewModel.ActiveChats.Select(c => new ChatSessionAdapter(c)).ToList();
        }

        public IChatSession? GetSelectedSession()
        {
            var session = ViewModel.SelectedChatSession;
            return session != null ? new ChatSessionAdapter(session) : null;
        }

        public IChatSession? GetSessionById(Guid id)
        {
            var session = ViewModel.ActiveChats.FirstOrDefault(c => c.Id == id);
            return session != null ? new ChatSessionAdapter(session) : null;
        }

        public IChatSession CreateSession(string? title = null)
        {
            var session = ViewModel.CreateNewChatSession(title);
            var adapter = new ChatSessionAdapter(session);
            ChatChanged?.Invoke(this, new ChatChangedEventArgs(ChatChangeType.SessionAdded, adapter));
            return adapter;
        }

        public bool DeleteSession(Guid id)
        {
            var session = ViewModel.ActiveChats.FirstOrDefault(c => c.Id == id);
            if (session == null) return false;

            ViewModel.DeleteChatSession(session);
            ChatChanged?.Invoke(this, new ChatChangedEventArgs(ChatChangeType.SessionDeleted));
            return true;
        }

        public IChatMessage AddMessage(Guid sessionId, string content, ChatMessageRole role)
        {
            var session = ViewModel.ActiveChats.FirstOrDefault(c => c.Id == sessionId);
            if (session == null)
                throw new ArgumentException($"Chat session with ID {sessionId} not found", nameof(sessionId));

            var message = new ChatMessage
            {
                Content = content,
                Role = role == ChatMessageRole.User ? "user" : "assistant"
            };

            session.Messages.Add(message);

            var adapter = new ChatMessageAdapter(message);
            ChatChanged?.Invoke(this, new ChatChangedEventArgs(ChatChangeType.MessageAdded, new ChatSessionAdapter(session)));

            return adapter;
        }

        public async Task SaveAsync()
        {
            await ViewModel.SaveChatsAsync();
        }
    }

    internal class ChatSessionAdapter : IChatSession
    {
        private readonly ChatSession _session;

        public ChatSessionAdapter(ChatSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Guid Id => _session.Id;

        public string ChatTitle
        {
            get => _session.ChatTitle;
            set => _session.ChatTitle = value;
        }

        public string ChatSummary
        {
            get => _session.ChatSummary;
            set => _session.ChatSummary = value;
        }

        public DateTime CreatedDate => _session.CreatedAt;
        public DateTime LastMessageDate => _session.Messages.Count > 0 
            ? _session.CreatedAt  // ChatSession doesn't track last message date, use created date as fallback
            : _session.CreatedAt;

        public IReadOnlyList<IChatMessage> Messages =>
            _session.Messages.Select(m => new ChatMessageAdapter(m)).ToList();
    }

    internal class ChatMessageAdapter : IChatMessage
    {
        private readonly ChatMessage _message;
        private readonly Guid _id;

        public ChatMessageAdapter(ChatMessage message)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _id = Guid.NewGuid(); // Generate an ID since ChatMessage doesn't have one
        }

        public Guid Id => _id;
        public string Content => _message.Content;
        public ChatMessageRole Role => _message.Role == "user" ? ChatMessageRole.User : ChatMessageRole.Assistant;
        public DateTime Timestamp => DateTime.Now; // ChatMessage doesn't track timestamp
    }
}
