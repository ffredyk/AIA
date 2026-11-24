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
        private static string DataFolder => AppPaths.UserDataFolder;
        private static string ChatsFile => Path.Combine(DataFolder, "chats.json");
        private static string ChatImagesFolder => Path.Combine(DataFolder, "chat-images");

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
                var chatDtos = await JsonFileStore.ReadAsync<List<ChatSessionDto>>(ChatsFile, GetJsonOptions());

                if (chatDtos == null)
                    return new List<ChatSession>();

                return chatDtos.Select(ConvertFromDto).ToList();
            }
            catch (Exception ex)
            {
                throw new DataLoadException(ChatsFile, JsonFileStore.Quarantine(ChatsFile), ex);
            }
        }

        /// <summary>
        /// Saves chat sessions to disk
        /// </summary>
        public static async Task SaveChatsAsync(IEnumerable<ChatSession> chats)
        {
            EnsureDirectoryExists();

            var chatDtos = chats.Select(ConvertToDto).ToList();

            foreach (var dto in chatDtos)
            {
                foreach (var image in dto.Messages.SelectMany(m => m.Images))
                {
                    await EnsureImageFileAsync(image).ConfigureAwait(false);
                }
            }

            await JsonFileStore.WriteAsync(ChatsFile, chatDtos, GetJsonOptions()).ConfigureAwait(false);
        }

        private static async Task EnsureImageFileAsync(ChatImageDto dto)
        {
            if (dto.Image == null || string.IsNullOrEmpty(dto.FileName))
                return;

            var path = Path.Combine(ChatImagesFolder, dto.FileName);
            if (File.Exists(path))
                return;

            try
            {
                Directory.CreateDirectory(ChatImagesFolder);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dto.Image));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                await File.WriteAllBytesAsync(path, stream.ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to persist chat image '{dto.FileName}': {ex.Message}");
            }
        }

        private static System.Windows.Media.Imaging.BitmapSource? LoadImage(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            try
            {
                var path = Path.Combine(ChatImagesFolder, fileName);
                if (!File.Exists(path))
                    return null;

                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load chat image '{fileName}': {ex.Message}");
                return null;
            }
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
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Images = m.AttachedImages.Select(image => new ChatImageDto
                    {
                        Id = image.Id,
                        MimeType = image.MimeType,
                        CapturedAt = image.CapturedAt,
                        FileName = $"{image.Id:N}.png",
                        Image = image.FullImage
                    }).ToList()
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
                var message = new ChatMessage
                {
                    Role = messageDto.Role,
                    Content = messageDto.Content,
                    Timestamp = messageDto.Timestamp
                };

                foreach (var imageDto in messageDto.Images)
                {
                    var bitmap = LoadImage(imageDto.FileName);
                    if (bitmap == null)
                        continue;

                    var attachment = new ChatImageAttachment
                    {
                        Id = imageDto.Id == Guid.Empty ? Guid.NewGuid() : imageDto.Id,
                        MimeType = string.IsNullOrEmpty(imageDto.MimeType) ? "image/png" : imageDto.MimeType,
                        CapturedAt = imageDto.CapturedAt,
                        FullImage = bitmap
                    };
                    attachment.CreateThumbnail();
                    message.AttachedImages.Add(attachment);
                }

                chat.Messages.Add(message);
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
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public List<ChatImageDto> Images { get; set; } = new();
        }

        private class ChatImageDto
        {
            public Guid Id { get; set; }
            public string MimeType { get; set; } = "image/png";
            public DateTime CapturedAt { get; set; }
            public string FileName { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonIgnore]
            public System.Windows.Media.Imaging.BitmapSource? Image { get; set; }
        }

        #endregion
    }
}
