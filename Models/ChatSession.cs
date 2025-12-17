using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIA.Models
{
    public class ChatSession : INotifyPropertyChanged
    {
        private string _chatTitle = string.Empty;
        private string _chatSummary = string.Empty;
        private bool _isRenaming;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid Id { get; set; } = Guid.NewGuid();
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
        
        public string ChatTitle 
        { 
            get => _chatTitle;
            set
            {
                if (_chatTitle != value)
                {
                    _chatTitle = value;
                    OnPropertyChanged(nameof(ChatTitle));
                }
            }
        }
        
        public string ChatSummary 
        { 
            get => _chatSummary;
            set
            {
                if (_chatSummary != value)
                {
                    _chatSummary = value;
                    OnPropertyChanged(nameof(ChatSummary));
                }
            }
        }

        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                if (_isRenaming != value)
                {
                    _isRenaming = value;
                    OnPropertyChanged(nameof(IsRenaming));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
