using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service for persisting chat sessions to disk
    /// </summary>
    public static class ChatSessionService
    {
        private static readonly string DataFolder;
        private static readonly string ChatsFile;

        static ChatSessionService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            DataFolder = Path.Combine(exeDirectory, "userdata");
            ChatsFile = Path.Combine(DataFolder, "chats.json");
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true
            };
        }

        /// <summary>
        /// Loads chat sessions from disk
        /// </summary>
        public static async Task<List<ChatSession>> LoadChatsAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(ChatsFile))
            {
                return new List<ChatSession>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(ChatsFile);
                var chatDtos = JsonSerializer.Deserialize<List<ChatSessionDto>>(json, GetJsonOptions());

                if (chatDtos == null)
                    return new List<ChatSession>();

                return chatDtos.Select(ConvertFromDto).ToList();
            }
            catch
            {
                return new List<ChatSession>();
            }
        }

        /// <summary>
        /// Saves chat sessions to disk
        /// </summary>
        public static async Task SaveChatsAsync(IEnumerable<ChatSession> chats)
        {
            EnsureDirectoryExists();

            var chatDtos = chats.Select(ConvertToDto).ToList();
            var json = JsonSerializer.Serialize(chatDtos, GetJsonOptions());
            await File.WriteAllTextAsync(ChatsFile, json);
        }

        private static ChatSessionDto ConvertToDto(ChatSession chat)
        {
            return new ChatSessionDto
            {
                Id = chat.Id,
                ChatTitle = chat.ChatTitle,
                ChatSummary = chat.ChatSummary,
                CreatedAt = chat.CreatedAt,
                Messages = chat.Messages.Select(m => new ChatMessageDto
                {
                    Role = m.Role,
                    Content = m.Content
                }).ToList()
            };
        }

        private static ChatSession ConvertFromDto(ChatSessionDto dto)
        {
            var chat = new ChatSession
            {
                Id = dto.Id,
                ChatTitle = dto.ChatTitle,
                ChatSummary = dto.ChatSummary,
                CreatedAt = dto.CreatedAt
            };

            foreach (var messageDto in dto.Messages)
            {
                chat.Messages.Add(new ChatMessage
                {
                    Role = messageDto.Role,
                    Content = messageDto.Content
                });
            }

            return chat;
        }

        #region DTOs for serialization

        private class ChatSessionDto
        {
            public Guid Id { get; set; }
            public string ChatTitle { get; set; } = string.Empty;
            public string ChatSummary { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public List<ChatMessageDto> Messages { get; set; } = new();
        }

        private class ChatMessageDto
        {
            public string Role { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        #endregion
    }
}
