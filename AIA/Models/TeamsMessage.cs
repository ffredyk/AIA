using System;
using System.ComponentModel;

namespace AIA.Models
{
    public class TeamsMessage : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _chatId = string.Empty;
        private string _chatName = string.Empty;
        private string _senderName = string.Empty;
        private string _senderEmail = string.Empty;
        private string _content = string.Empty;
        private string _contentPreview = string.Empty;
        private DateTime _receivedDate;
        private bool _isRead;
        private TeamsMessageType _messageType = TeamsMessageType.Chat;
        private string _teamName = string.Empty;
        private string _channelName = string.Empty;
        private int _mentionCount;
        private bool _isMention;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public string ChatId
        {
            get => _chatId;
            set { _chatId = value; OnPropertyChanged(nameof(ChatId)); }
        }

        public string ChatName
        {
            get => _chatName;
            set { _chatName = value; OnPropertyChanged(nameof(ChatName)); }
        }

        public string SenderName
        {
            get => _senderName;
            set { _senderName = value; OnPropertyChanged(nameof(SenderName)); OnPropertyChanged(nameof(SenderInitials)); }
        }

        public string SenderEmail
        {
            get => _senderEmail;
            set { _senderEmail = value; OnPropertyChanged(nameof(SenderEmail)); }
        }

        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(nameof(Content)); }
        }

        public string ContentPreview
        {
            get => _contentPreview;
            set { _contentPreview = value; OnPropertyChanged(nameof(ContentPreview)); }
        }

        public DateTime ReceivedDate
        {
            get => _receivedDate;
            set
            {
                _receivedDate = value;
                OnPropertyChanged(nameof(ReceivedDate));
                OnPropertyChanged(nameof(ReceivedDateText));
            }
        }

        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(nameof(IsRead)); }
        }

        public TeamsMessageType MessageType
        {
            get => _messageType;
            set { _messageType = value; OnPropertyChanged(nameof(MessageType)); OnPropertyChanged(nameof(IsChannelMessage)); }
        }

        public string TeamName
        {
            get => _teamName;
            set { _teamName = value; OnPropertyChanged(nameof(TeamName)); OnPropertyChanged(nameof(SourceText)); }
        }

        public string ChannelName
        {
            get => _channelName;
            set { _channelName = value; OnPropertyChanged(nameof(ChannelName)); OnPropertyChanged(nameof(SourceText)); }
        }

        public int MentionCount
        {
            get => _mentionCount;
            set { _mentionCount = value; OnPropertyChanged(nameof(MentionCount)); OnPropertyChanged(nameof(HasMentions)); }
        }

        public bool IsMention
        {
            get => _isMention;
            set { _isMention = value; OnPropertyChanged(nameof(IsMention)); }
        }

        // Computed properties
        public bool IsChannelMessage => MessageType == TeamsMessageType.Channel;
        public bool HasMentions => MentionCount > 0 || IsMention;

        public string ReceivedDateText
        {
            get
            {
                var diff = DateTime.Now - ReceivedDate;

                if (diff.TotalMinutes < 1)
                    return "Just now";
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes} min ago";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours}h ago";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}d ago";
                if (ReceivedDate.Year == DateTime.Now.Year)
                    return ReceivedDate.ToString("MMM dd");
                return ReceivedDate.ToString("MMM dd, yyyy");
            }
        }

        public string SourceText
        {
            get
            {
                if (MessageType == TeamsMessageType.Channel && !string.IsNullOrEmpty(TeamName))
                    return $"{TeamName} > {ChannelName}";
                return ChatName;
            }
        }

        public string SenderInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SenderName))
                    return "?";

                var parts = SenderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    return $"{parts[0][0]}{parts[1][0]}".ToUpper();
                if (parts.Length == 1 && parts[0].Length >= 1)
                    return parts[0][0].ToString().ToUpper();
                return "?";
            }
        }

        public void RefreshTimeDisplays()
        {
            OnPropertyChanged(nameof(ReceivedDateText));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum TeamsMessageType
    {
        Chat,
        Channel
    }
}
