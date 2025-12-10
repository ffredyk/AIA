using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIA.Models
{
    public class ChatSession
    {
        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
        public string ChatTitle { get; set; }
        public string ChatSummary { get; set; }
    }
}
