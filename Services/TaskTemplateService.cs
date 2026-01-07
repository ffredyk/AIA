using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Models;

namespace AIA.Services
{
    /// <summary>
    /// Service for managing task templates
    /// </summary>
    public class TaskTemplateService
    {
        private static readonly string TemplatesFile;

        static TaskTemplateService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            TemplatesFile = Path.Combine(exeDirectory, "task-templates.json");
        }

        /// <summary>
        /// Loads all task templates from disk
        /// </summary>
        public static async Task<List<TaskTemplate>> LoadTemplatesAsync()
        {
            if (!File.Exists(TemplatesFile))
            {
                return CreateDefaultTemplates();
            }

            try
            {
                var json = await File.ReadAllTextAsync(TemplatesFile);
                var templates = JsonSerializer.Deserialize<List<TaskTemplate>>(json);
                return templates ?? new List<TaskTemplate>();
            }
            catch
            {
                return new List<TaskTemplate>();
            }
        }

        /// <summary>
        /// Saves all task templates to disk
        /// </summary>
        public static async Task SaveTemplatesAsync(List<TaskTemplate> templates)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(templates, options);
            await File.WriteAllTextAsync(TemplatesFile, json);
        }

        /// <summary>
        /// Creates a new template from a task
        /// </summary>
        public static async Task<TaskTemplate> CreateTemplateFromTaskAsync(TaskItem task, string name, string description = "")
        {
            var templates = await LoadTemplatesAsync();
            var template = TaskTemplate.FromTask(task, name, description);
            templates.Add(template);
            await SaveTemplatesAsync(templates);
            return template;
        }

        /// <summary>
        /// Deletes a template
        /// </summary>
        public static async Task<bool> DeleteTemplateAsync(Guid templateId)
        {
            var templates = await LoadTemplatesAsync();
            var template = templates.FirstOrDefault(t => t.Id == templateId);
            
            if (template == null)
                return false;

            templates.Remove(template);
            await SaveTemplatesAsync(templates);
            return true;
        }

        /// <summary>
        /// Updates an existing template
        /// </summary>
        public static async Task<bool> UpdateTemplateAsync(TaskTemplate template)
        {
            var templates = await LoadTemplatesAsync();
            var existingIndex = templates.FindIndex(t => t.Id == template.Id);
            
            if (existingIndex < 0)
                return false;

            templates[existingIndex] = template;
            await SaveTemplatesAsync(templates);
            return true;
        }

        /// <summary>
        /// Creates default templates for common scenarios
        /// </summary>
        private static List<TaskTemplate> CreateDefaultTemplates()
        {
            return new List<TaskTemplate>
            {
                new TaskTemplate
                {
                    Name = "Bug Fix",
                    Description = "Template for fixing software bugs",
                    Title = "Fix: [Bug Description]",
                    TaskDescription = "Investigate and fix the reported bug",
                    DefaultPriority = TaskPriority.High,
                    RelativeDueDateDays = 3,
                    Tags = new List<string> { "bug", "development" },
                    Subtasks = new ObservableCollection<SubtaskTemplate>
                    {
                        new SubtaskTemplate { Title = "Reproduce the bug", Priority = TaskPriority.High, RelativeDueDateDays = 0 },
                        new SubtaskTemplate { Title = "Identify root cause", Priority = TaskPriority.High, RelativeDueDateDays = 1 },
                        new SubtaskTemplate { Title = "Implement fix", Priority = TaskPriority.High, RelativeDueDateDays = 2 },
                        new SubtaskTemplate { Title = "Test the fix", Priority = TaskPriority.Medium, RelativeDueDateDays = 3 },
                        new SubtaskTemplate { Title = "Update documentation", Priority = TaskPriority.Low, RelativeDueDateDays = 3 }
                    }
                },
                new TaskTemplate
                {
                    Name = "Feature Development",
                    Description = "Template for implementing new features",
                    Title = "Feature: [Feature Name]",
                    TaskDescription = "Develop and implement a new feature",
                    DefaultPriority = TaskPriority.Medium,
                    RelativeDueDateDays = 14,
                    Tags = new List<string> { "feature", "development" },
                    Subtasks = new ObservableCollection<SubtaskTemplate>
                    {
                        new SubtaskTemplate { Title = "Design specification", Priority = TaskPriority.High, RelativeDueDateDays = 2 },
                        new SubtaskTemplate { Title = "UI/UX design", Priority = TaskPriority.Medium, RelativeDueDateDays = 4 },
                        new SubtaskTemplate { Title = "Backend implementation", Priority = TaskPriority.High, RelativeDueDateDays = 8 },
                        new SubtaskTemplate { Title = "Frontend implementation", Priority = TaskPriority.High, RelativeDueDateDays = 10 },
                        new SubtaskTemplate { Title = "Testing", Priority = TaskPriority.High, RelativeDueDateDays = 12 },
                        new SubtaskTemplate { Title = "Documentation", Priority = TaskPriority.Medium, RelativeDueDateDays = 14 }
                    }
                },
                new TaskTemplate
                {
                    Name = "Code Review",
                    Description = "Template for conducting code reviews",
                    Title = "Review: [Pull Request/Branch Name]",
                    TaskDescription = "Review code changes and provide feedback",
                    DefaultPriority = TaskPriority.Medium,
                    RelativeDueDateDays = 1,
                    Tags = new List<string> { "review", "quality" },
                    Subtasks = new ObservableCollection<SubtaskTemplate>
                    {
                        new SubtaskTemplate { Title = "Check code style and conventions", Priority = TaskPriority.Medium },
                        new SubtaskTemplate { Title = "Verify functionality", Priority = TaskPriority.High },
                        new SubtaskTemplate { Title = "Review tests", Priority = TaskPriority.Medium },
                        new SubtaskTemplate { Title = "Check documentation", Priority = TaskPriority.Low }
                    }
                },
                new TaskTemplate
                {
                    Name = "Meeting Preparation",
                    Description = "Template for preparing for important meetings",
                    Title = "Prepare for: [Meeting Name]",
                    TaskDescription = "Gather materials and prepare for upcoming meeting",
                    DefaultPriority = TaskPriority.Medium,
                    RelativeDueDateDays = 2,
                    Tags = new List<string> { "meeting", "planning" },
                    Subtasks = new ObservableCollection<SubtaskTemplate>
                    {
                        new SubtaskTemplate { Title = "Review previous meeting notes", Priority = TaskPriority.Low },
                        new SubtaskTemplate { Title = "Prepare presentation/materials", Priority = TaskPriority.High, RelativeDueDateDays = 1 },
                        new SubtaskTemplate { Title = "Send agenda to participants", Priority = TaskPriority.Medium, RelativeDueDateDays = 1 },
                        new SubtaskTemplate { Title = "Prepare discussion points", Priority = TaskPriority.Medium, RelativeDueDateDays = 2 }
                    }
                },
                new TaskTemplate
                {
                    Name = "Weekly Planning",
                    Description = "Template for weekly work planning",
                    Title = "Week [Week Number] Planning",
                    TaskDescription = "Plan and organize tasks for the upcoming week",
                    DefaultPriority = TaskPriority.Medium,
                    RelativeDueDateDays = 7,
                    Tags = new List<string> { "planning", "weekly" },
                    Subtasks = new ObservableCollection<SubtaskTemplate>
                    {
                        new SubtaskTemplate { Title = "Review previous week accomplishments", Priority = TaskPriority.Low },
                        new SubtaskTemplate { Title = "Identify top priorities", Priority = TaskPriority.High },
                        new SubtaskTemplate { Title = "Schedule important tasks", Priority = TaskPriority.High },
                        new SubtaskTemplate { Title = "Block calendar time", Priority = TaskPriority.Medium },
                        new SubtaskTemplate { Title = "Communicate plan to team", Priority = TaskPriority.Medium }
                    }
                }
            };
        }

        /// <summary>
        /// Exports templates to a JSON file
        /// </summary>
        public static async Task<bool> ExportTemplatesAsync(string filePath, List<TaskTemplate> templates)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(templates, options);
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Imports templates from a JSON file
        /// </summary>
        public static async Task<List<TaskTemplate>?> ImportTemplatesAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var json = await File.ReadAllTextAsync(filePath);
                var templates = JsonSerializer.Deserialize<List<TaskTemplate>>(json);
                return templates;
            }
            catch
            {
                return null;
            }
        }
    }
}
