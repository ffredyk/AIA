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
    /// Service for persisting tasks and reminders to disk
    /// </summary>
    public static class TaskReminderService
    {
        private static readonly string DataFolder;
        private static readonly string TasksFile;
        private static readonly string RemindersFile;

        static TaskReminderService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            DataFolder = Path.Combine(exeDirectory, "userdata");
            TasksFile = Path.Combine(DataFolder, "tasks.json");
            RemindersFile = Path.Combine(DataFolder, "reminders.json");
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
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        #region Tasks

        /// <summary>
        /// Loads tasks from disk
        /// </summary>
        public static async Task<List<TaskItem>> LoadTasksAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(TasksFile))
            {
                return new List<TaskItem>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(TasksFile);
                var taskDtos = JsonSerializer.Deserialize<List<TaskItemDto>>(json, GetJsonOptions());
                
                if (taskDtos == null)
                    return new List<TaskItem>();

                return taskDtos.Select(ConvertFromDto).ToList();
            }
            catch
            {
                return new List<TaskItem>();
            }
        }

        /// <summary>
        /// Saves tasks to disk
        /// </summary>
        public static async Task SaveTasksAsync(IEnumerable<TaskItem> tasks)
        {
            EnsureDirectoryExists();

            var taskDtos = tasks.Select(ConvertToDto).ToList();
            var json = JsonSerializer.Serialize(taskDtos, GetJsonOptions());
            await File.WriteAllTextAsync(TasksFile, json);
        }

        private static TaskItemDto ConvertToDto(TaskItem task)
        {
            return new TaskItemDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Notes = task.Notes,
                Status = task.Status,
                Priority = task.Priority,
                CreatedDate = task.CreatedDate,
                DueDate = task.DueDate,
                CompletedDate = task.CompletedDate,
                ParentTaskId = task.ParentTaskId,
                Subtasks = task.Subtasks.Select(ConvertToDto).ToList()
            };
        }

        private static TaskItem ConvertFromDto(TaskItemDto dto)
        {
            var task = new TaskItem
            {
                Id = dto.Id,
                Title = dto.Title,
                Description = dto.Description,
                Notes = dto.Notes,
                Status = dto.Status,
                Priority = dto.Priority,
                CreatedDate = dto.CreatedDate,
                DueDate = dto.DueDate,
                CompletedDate = dto.CompletedDate,
                ParentTaskId = dto.ParentTaskId
            };

            foreach (var subtaskDto in dto.Subtasks)
            {
                var subtask = ConvertFromDto(subtaskDto);
                subtask.ParentTaskId = task.Id;
                task.Subtasks.Add(subtask);
            }

            return task;
        }

        #endregion

        #region Reminders

        /// <summary>
        /// Loads reminders from disk
        /// </summary>
        public static async Task<List<ReminderItem>> LoadRemindersAsync()
        {
            EnsureDirectoryExists();

            if (!File.Exists(RemindersFile))
            {
                return new List<ReminderItem>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(RemindersFile);
                var reminderDtos = JsonSerializer.Deserialize<List<ReminderItemDto>>(json, GetJsonOptions());
                
                if (reminderDtos == null)
                    return new List<ReminderItem>();

                return reminderDtos.Select(ConvertFromDto).ToList();
            }
            catch
            {
                return new List<ReminderItem>();
            }
        }

        /// <summary>
        /// Saves reminders to disk
        /// </summary>
        public static async Task SaveRemindersAsync(IEnumerable<ReminderItem> reminders)
        {
            EnsureDirectoryExists();

            var reminderDtos = reminders.Select(ConvertToDto).ToList();
            var json = JsonSerializer.Serialize(reminderDtos, GetJsonOptions());
            await File.WriteAllTextAsync(RemindersFile, json);
        }

        private static ReminderItemDto ConvertToDto(ReminderItem reminder)
        {
            return new ReminderItemDto
            {
                Id = reminder.Id,
                Title = reminder.Title,
                DueDate = reminder.DueDate,
                Severity = reminder.Severity,
                CreatedDate = reminder.CreatedDate,
                IsCompleted = reminder.IsCompleted
            };
        }

        private static ReminderItem ConvertFromDto(ReminderItemDto dto)
        {
            return new ReminderItem
            {
                Id = dto.Id,
                Title = dto.Title,
                DueDate = dto.DueDate,
                Severity = dto.Severity,
                CreatedDate = dto.CreatedDate,
                IsCompleted = dto.IsCompleted
            };
        }

        #endregion

        #region DTOs for serialization

        private class TaskItemDto
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public Models.TaskStatus Status { get; set; }
            public TaskPriority Priority { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime? DueDate { get; set; }
            public DateTime? CompletedDate { get; set; }
            public Guid? ParentTaskId { get; set; }
            public List<TaskItemDto> Subtasks { get; set; } = new();
        }

        private class ReminderItemDto
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public DateTime DueDate { get; set; }
            public ReminderSeverity Severity { get; set; }
            public DateTime CreatedDate { get; set; }
            public bool IsCompleted { get; set; }
        }

        #endregion
    }
}
