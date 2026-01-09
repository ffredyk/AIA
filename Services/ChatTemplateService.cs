using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service for managing chat message templates
    /// </summary>
    public class ChatTemplateService
    {
        private static readonly string SettingsFolder;
        private static readonly string TemplatesFile;

        static ChatTemplateService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            SettingsFolder = Path.Combine(exeDirectory, "settings");
            TemplatesFile = Path.Combine(SettingsFolder, "chat-templates.json");
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);
        }

        /// <summary>
        /// Loads chat message templates from disk
        /// </summary>
        public static async Task<List<ChatMessageTemplate>> LoadTemplatesAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(TemplatesFile))
            {
                return CreateDefaultTemplates();
            }

            try
            {
                var json = await File.ReadAllTextAsync(TemplatesFile);
                var templates = JsonSerializer.Deserialize<List<ChatMessageTemplate>>(json);
                return templates ?? CreateDefaultTemplates();
            }
            catch
            {
                return CreateDefaultTemplates();
            }
        }

        /// <summary>
        /// Saves chat message templates to disk
        /// </summary>
        public static async Task SaveTemplatesAsync(List<ChatMessageTemplate> templates)
        {
            EnsureDirectoryExists();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(templates, options);
            await File.WriteAllTextAsync(TemplatesFile, json);
        }

        /// <summary>
        /// Creates default predefined templates
        /// </summary>
        private static List<ChatMessageTemplate> CreateDefaultTemplates()
        {
            return new List<ChatMessageTemplate>
            {
                new ChatMessageTemplate
                {
                    Title = "Today's Summary",
                    Message = "Summarize my tasks and reminders for today.",
                    Description = "Get overview of today's items",
                    Icon = "Calendar20",
                    Color = "#0078D4",
                    Order = 0,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "This Week",
                    Message = "Give me an overview of all my tasks and reminders for the current week.",
                    Description = "Weekly overview",
                    Icon = "CalendarWeekNumbers20",
                    Color = "#1EB75F",
                    Order = 1,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Urgent Items",
                    Message = "Show me all urgent and overdue tasks and reminders.",
                    Description = "Critical items requiring attention",
                    Icon = "Alert20",
                    Color = "#FF4444",
                    Order = 2,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Yesterday's Progress",
                    Message = "What tasks did I complete yesterday?",
                    Description = "Review completed work",
                    Icon = "CheckmarkCircle20",
                    Color = "#1EB75F",
                    Order = 3,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Tomorrow's Plan",
                    Message = "What do I have scheduled for tomorrow?",
                    Description = "Preview upcoming tasks",
                    Icon = "CalendarLtr20",
                    Color = "#9B59B6",
                    Order = 4,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Prioritize Tasks",
                    Message = "Help me prioritize my current tasks based on deadlines, importance, and dependencies.",
                    Description = "Smart task prioritization",
                    Icon = "ArrowSort20",
                    Color = "#FFA500",
                    Order = 5,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Weekly Report",
                    Message = "Generate a summary report of my productivity this week, including completed tasks, pending items, and suggestions for next week.",
                    Description = "Productivity insights",
                    Icon = "DocumentBulletList20",
                    Color = "#0078D4",
                    Order = 6,
                    IsEnabled = true,
                    IsPredefined = true
                },
                new ChatMessageTemplate
                {
                    Title = "Focus Suggestion",
                    Message = "Based on my current tasks and deadlines, what should I focus on right now?",
                    Description = "Get focus recommendations",
                    Icon = "Target20",
                    Color = "#E74C3C",
                    Order = 7,
                    IsEnabled = true,
                    IsPredefined = true
                }
            };
        }

        /// <summary>
        /// Loads template settings
        /// </summary>
        public static async Task<ChatTemplateSettings> LoadSettingsAsync()
        {
            var settingsFile = Path.Combine(SettingsFolder, "chat-template-settings.json");
            
            if (!File.Exists(settingsFile))
            {
                return new ChatTemplateSettings();
            }

            try
            {
                var json = await File.ReadAllTextAsync(settingsFile);
                var settings = JsonSerializer.Deserialize<ChatTemplateSettings>(json);
                return settings ?? new ChatTemplateSettings();
            }
            catch
            {
                return new ChatTemplateSettings();
            }
        }

        /// <summary>
        /// Saves template settings
        /// </summary>
        public static async Task SaveSettingsAsync(ChatTemplateSettings settings)
        {
            EnsureDirectoryExists();

            var settingsFile = Path.Combine(SettingsFolder, "chat-template-settings.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(settingsFile, json);
        }
    }
}
