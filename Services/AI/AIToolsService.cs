using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AIA.Models;
using AIA.Models.AI;
using TaskStatus = AIA.Models.TaskStatus;

namespace AIA.Services.AI
{
    /// <summary>
    /// Provides MCP-like tools for AI to access application data
    /// </summary>
    public class AIToolsService
    {
        private readonly Dictionary<string, AITool> _tools = new();
        private readonly Func<OverlayViewModel> _getViewModel;

        public AIToolsService(Func<OverlayViewModel> getViewModel)
        {
            _getViewModel = getViewModel;
            RegisterAllTools();
        }

        public IEnumerable<AITool> GetAllTools() => _tools.Values;

        public AITool? GetTool(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

        public string ExecuteTool(string name, Dictionary<string, object> arguments)
        {
            if (!_tools.TryGetValue(name, out var tool))
            {
                return JsonSerializer.Serialize(new { error = $"Tool '{name}' not found" });
            }

            try
            {
                return tool.Handler?.Invoke(arguments) ?? "No handler defined";
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private void RegisterAllTools()
        {
            // Task Tools
            RegisterTool(new AITool
            {
                Name = "get_tasks",
                Description = "Get a list of tasks. Can filter by status or priority.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["status"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Filter by status: NotStarted, InProgress, OnHold, Completed, Cancelled",
                        Required = false,
                        Enum = new[] { "NotStarted", "InProgress", "OnHold", "Completed", "Cancelled" }
                    },
                    ["priority"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Filter by priority: Low, Medium, High, Critical",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Critical" }
                    },
                    ["include_subtasks"] = new AIToolParameter
                    {
                        Type = "boolean",
                        Description = "Whether to include subtasks in the response",
                        Required = false
                    }
                },
                Handler = GetTasksHandler
            });

            RegisterTool(new AITool
            {
                Name = "get_task_details",
                Description = "Get detailed information about a specific task by title or ID",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "The title of the task to find (partial match)",
                        Required = false
                    },
                    ["id"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "The ID of the task",
                        Required = false
                    }
                },
                Handler = GetTaskDetailsHandler
            });

            RegisterTool(new AITool
            {
                Name = "create_task",
                Description = "Create a new task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter { Type = "string", Description = "The task title", Required = true },
                    ["description"] = new AIToolParameter { Type = "string", Description = "Task description", Required = false },
                    ["priority"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Task priority",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Critical" }
                    },
                    ["due_date"] = new AIToolParameter { Type = "string", Description = "Due date in ISO format (YYYY-MM-DD)", Required = false }
                },
                Handler = CreateTaskHandler
            });

            RegisterTool(new AITool
            {
                Name = "update_task_status",
                Description = "Update the status of a task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter { Type = "string", Description = "The task title to find", Required = true },
                    ["status"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "The new status",
                        Required = true,
                        Enum = new[] { "NotStarted", "InProgress", "OnHold", "Completed", "Cancelled" }
                    }
                },
                Handler = UpdateTaskStatusHandler
            });

            // Reminder Tools
            RegisterTool(new AITool
            {
                Name = "get_reminders",
                Description = "Get a list of reminders. Can filter by completion status or severity.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["include_completed"] = new AIToolParameter
                    {
                        Type = "boolean",
                        Description = "Whether to include completed reminders",
                        Required = false
                    },
                    ["severity"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Filter by severity",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Urgent" }
                    }
                },
                Handler = GetRemindersHandler
            });

            RegisterTool(new AITool
            {
                Name = "create_reminder",
                Description = "Create a new reminder",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter { Type = "string", Description = "The reminder title", Required = true },
                    ["due_datetime"] = new AIToolParameter { Type = "string", Description = "Due date/time in ISO format", Required = true },
                    ["severity"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Reminder severity",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Urgent" }
                    }
                },
                Handler = CreateReminderHandler
            });

            RegisterTool(new AITool
            {
                Name = "snooze_reminder",
                Description = "Snooze a reminder by a specified number of minutes",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter { Type = "string", Description = "The reminder title to find", Required = true },
                    ["minutes"] = new AIToolParameter { Type = "integer", Description = "Minutes to snooze (default 15)", Required = false }
                },
                Handler = SnoozeReminderHandler
            });

            // Data Bank Tools
            RegisterTool(new AITool
            {
                Name = "get_databank_categories",
                Description = "Get all data bank categories",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = GetDataBankCategoriesHandler
            });

            RegisterTool(new AITool
            {
                Name = "get_databank_entries",
                Description = "Get entries from a data bank category",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["category_name"] = new AIToolParameter { Type = "string", Description = "The category name", Required = true },
                    ["search_query"] = new AIToolParameter { Type = "string", Description = "Optional search term to filter entries", Required = false }
                },
                Handler = GetDataBankEntriesHandler
            });

            RegisterTool(new AITool
            {
                Name = "get_entry_content",
                Description = "Get the full content of a data bank entry",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["entry_title"] = new AIToolParameter { Type = "string", Description = "The entry title to find", Required = true }
                },
                Handler = GetEntryContentHandler
            });

            RegisterTool(new AITool
            {
                Name = "create_databank_entry",
                Description = "Create a new entry in a data bank category",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["category_name"] = new AIToolParameter { Type = "string", Description = "The category name", Required = true },
                    ["title"] = new AIToolParameter { Type = "string", Description = "Entry title", Required = true },
                    ["content"] = new AIToolParameter { Type = "string", Description = "Entry content", Required = true },
                    ["tags"] = new AIToolParameter { Type = "string", Description = "Comma-separated tags", Required = false }
                },
                Handler = CreateDataBankEntryHandler
            });

            // Screen/Data Asset Tools
            RegisterTool(new AITool
            {
                Name = "get_current_screenshots",
                Description = "Get information about currently captured screenshots/data assets",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = GetCurrentScreenshotsHandler
            });

            // System Tools
            RegisterTool(new AITool
            {
                Name = "get_current_time",
                Description = "Get the current date and time",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = _ => JsonSerializer.Serialize(new { 
                    datetime = DateTime.Now.ToString("o"),
                    date = DateTime.Now.ToString("yyyy-MM-dd"),
                    time = DateTime.Now.ToString("HH:mm:ss"),
                    dayOfWeek = DateTime.Now.DayOfWeek.ToString()
                })
            });

            RegisterTool(new AITool
            {
                Name = "get_app_summary",
                Description = "Get a summary of the user's current tasks, reminders, and data",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = GetAppSummaryHandler
            });
        }

        private void RegisterTool(AITool tool)
        {
            _tools[tool.Name] = tool;
        }

        #region Task Handlers

        private string GetTasksHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var tasks = vm.Tasks.AsEnumerable();

            if (TryGetArg<string>(args, "status", out var status) && Enum.TryParse<Models.TaskStatus>(status, out var statusEnum))
            {
                tasks = tasks.Where(t => t.Status == statusEnum);
            }

            if (TryGetArg<string>(args, "priority", out var priority) && Enum.TryParse<TaskPriority>(priority, out var priorityEnum))
            {
                tasks = tasks.Where(t => t.Priority == priorityEnum);
            }

            var includeSubtasks = TryGetArg<bool>(args, "include_subtasks", out var inc) && inc;

            var result = tasks.Select(t => new
            {
                id = t.Id.ToString(),
                title = t.Title,
                status = t.Status.ToString(),
                priority = t.Priority.ToString(),
                dueDate = t.DueDate?.ToString("yyyy-MM-dd"),
                isOverdue = t.IsOverdue,
                subtasksCount = t.Subtasks.Count,
                completedSubtasks = t.CompletedSubtasksCount,
                subtasks = includeSubtasks ? t.Subtasks.Select(s => new
                {
                    id = s.Id.ToString(),
                    title = s.Title,
                    status = s.Status.ToString()
                }).ToList() : null
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, tasks = result });
        }

        private string GetTaskDetailsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? task = null;

            if (TryGetArg<string>(args, "id", out var id) && Guid.TryParse(id, out var guid))
            {
                task = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "title", out var title))
            {
                task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            }

            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found" });
            }

            return JsonSerializer.Serialize(new
            {
                id = task.Id.ToString(),
                title = task.Title,
                description = task.Description,
                notes = task.Notes,
                status = task.Status.ToString(),
                priority = task.Priority.ToString(),
                dueDate = task.DueDate?.ToString("yyyy-MM-dd"),
                createdDate = task.CreatedDate.ToString("yyyy-MM-dd HH:mm"),
                completedDate = task.CompletedDate?.ToString("yyyy-MM-dd HH:mm"),
                isOverdue = task.IsOverdue,
                subtasks = task.Subtasks.Select(s => new
                {
                    id = s.Id.ToString(),
                    title = s.Title,
                    status = s.Status.ToString(),
                    priority = s.Priority.ToString()
                }).ToList()
            });
        }

        private string CreateTaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title) || string.IsNullOrWhiteSpace(title))
            {
                return JsonSerializer.Serialize(new { error = "Title is required" });
            }

            var task = new TaskItem { Title = title };

            if (TryGetArg<string>(args, "description", out var desc))
                task.Description = desc;

            if (TryGetArg<string>(args, "priority", out var priority) && Enum.TryParse<TaskPriority>(priority, out var priorityEnum))
                task.Priority = priorityEnum;

            if (TryGetArg<string>(args, "due_date", out var dueDate) && DateTime.TryParse(dueDate, out var dueDateValue))
                task.DueDate = dueDateValue;

            vm.Tasks.Add(task);
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, taskId = task.Id.ToString(), message = $"Task '{title}' created successfully" });
        }

        private string UpdateTaskStatusHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title))
            {
                return JsonSerializer.Serialize(new { error = "Title is required" });
            }

            if (!TryGetArg<string>(args, "status", out var status) || !Enum.TryParse<TaskStatus>(status, out var statusEnum))
            {
                return JsonSerializer.Serialize(new { error = "Valid status is required" });
            }

            var task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found" });
            }

            task.Status = statusEnum;
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, message = $"Task '{task.Title}' status updated to {statusEnum}" });
        }

        #endregion

        #region Reminder Handlers

        private string GetRemindersHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var reminders = vm.Reminders.AsEnumerable();

            if (!TryGetArg<bool>(args, "include_completed", out var includeCompleted) || !includeCompleted)
            {
                reminders = reminders.Where(r => !r.IsCompleted);
            }

            if (TryGetArg<string>(args, "severity", out var severity) && Enum.TryParse<ReminderSeverity>(severity, out var severityEnum))
            {
                reminders = reminders.Where(r => r.Severity == severityEnum);
            }

            var result = reminders.OrderBy(r => r.DueDate).Select(r => new
            {
                id = r.Id.ToString(),
                title = r.Title,
                dueDate = r.DueDate.ToString("yyyy-MM-dd HH:mm"),
                severity = r.Severity.ToString(),
                isCompleted = r.IsCompleted,
                isOverdue = r.IsOverdue,
                timeLeft = r.TimeLeftText
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, reminders = result });
        }

        private string CreateReminderHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title) || string.IsNullOrWhiteSpace(title))
            {
                return JsonSerializer.Serialize(new { error = "Title is required" });
            }

            if (!TryGetArg<string>(args, "due_datetime", out var dueDateTimeStr) || !DateTime.TryParse(dueDateTimeStr, out var dueDateTime))
            {
                return JsonSerializer.Serialize(new { error = "Valid due_datetime is required" });
            }

            var reminder = new ReminderItem { Title = title, DueDate = dueDateTime };

            if (TryGetArg<string>(args, "severity", out var severity) && Enum.TryParse<ReminderSeverity>(severity, out var severityEnum))
                reminder.Severity = severityEnum;

            vm.Reminders.Add(reminder);
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, reminderId = reminder.Id.ToString(), message = $"Reminder '{title}' created for {dueDateTime:g}" });
        }

        private string SnoozeReminderHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title))
            {
                return JsonSerializer.Serialize(new { error = "Title is required" });
            }

            var reminder = vm.Reminders.FirstOrDefault(r => r.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            if (reminder == null)
            {
                return JsonSerializer.Serialize(new { error = "Reminder not found" });
            }

            int minutes = 15;
            if (TryGetArg<int>(args, "minutes", out var mins) && mins > 0)
            {
                minutes = mins;
            }

            vm.SnoozeReminder(reminder, minutes);

            return JsonSerializer.Serialize(new { success = true, message = $"Reminder '{reminder.Title}' snoozed by {minutes} minutes. New due time: {reminder.DueDate:g}" });
        }

        #endregion

        #region Data Bank Handlers

        private string GetDataBankCategoriesHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            var result = vm.DataBankCategories.Select(c => new
            {
                id = c.Id.ToString(),
                name = c.Name,
                entryCount = c.EntryCount,
                color = c.Color
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, categories = result });
        }

        private string GetDataBankEntriesHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "category_name", out var categoryName))
            {
                return JsonSerializer.Serialize(new { error = "category_name is required" });
            }

            var category = vm.DataBankCategories.FirstOrDefault(c => c.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                return JsonSerializer.Serialize(new { error = "Category not found" });
            }

            // Temporarily switch category to get entries
            var previousCategory = vm.SelectedCategory;
            vm.SelectedCategory = category;

            var entries = vm.CurrentCategoryEntries.AsEnumerable();

            if (TryGetArg<string>(args, "search_query", out var searchQuery))
            {
                entries = entries.Where(e => 
                    e.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    (e.Tags?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var result = entries.Select(e => new
            {
                id = e.Id.ToString(),
                title = e.Title,
                type = e.EntryType.ToString(),
                tags = e.Tags,
                preview = e.ContentPreview,
                hasFile = !string.IsNullOrEmpty(e.FilePath)
            }).ToList();

            vm.SelectedCategory = previousCategory;

            return JsonSerializer.Serialize(new { category = categoryName, count = result.Count, entries = result });
        }

        private string GetEntryContentHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "entry_title", out var title))
            {
                return JsonSerializer.Serialize(new { error = "entry_title is required" });
            }

            // Search across all categories
            DataBankEntry? entry = null;
            foreach (var category in vm.DataBankCategories)
            {
                var previousCategory = vm.SelectedCategory;
                vm.SelectedCategory = category;
                
                entry = vm.CurrentCategoryEntries.FirstOrDefault(e => 
                    e.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                
                if (entry != null)
                {
                    vm.SelectedCategory = previousCategory;
                    break;
                }
                vm.SelectedCategory = previousCategory;
            }

            if (entry == null)
            {
                return JsonSerializer.Serialize(new { error = "Entry not found" });
            }

            return JsonSerializer.Serialize(new
            {
                id = entry.Id.ToString(),
                title = entry.Title,
                type = entry.EntryType.ToString(),
                content = entry.Content,
                tags = entry.Tags,
                hasFile = !string.IsNullOrEmpty(entry.FilePath),
                originalFileName = entry.OriginalFileName,
                createdDate = entry.CreatedDate.ToString("yyyy-MM-dd HH:mm")
            });
        }

        private string CreateDataBankEntryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "category_name", out var categoryName))
            {
                return JsonSerializer.Serialize(new { error = "category_name is required" });
            }

            if (!TryGetArg<string>(args, "title", out var title))
            {
                return JsonSerializer.Serialize(new { error = "title is required" });
            }

            if (!TryGetArg<string>(args, "content", out var content))
            {
                return JsonSerializer.Serialize(new { error = "content is required" });
            }

            var category = vm.DataBankCategories.FirstOrDefault(c => c.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                return JsonSerializer.Serialize(new { error = "Category not found" });
            }

            var previousCategory = vm.SelectedCategory;
            vm.SelectedCategory = category;

            var entry = new DataBankEntry
            {
                Title = title,
                Content = content,
                EntryType = DataEntryType.Text,
                CategoryId = category.Id
            };

            if (TryGetArg<string>(args, "tags", out var tags))
            {
                entry.Tags = tags;
            }

            vm.CurrentCategoryEntries.Add(entry);
            category.EntryCount++;
            _ = vm.SaveDataBanksAsync();

            vm.SelectedCategory = previousCategory;

            return JsonSerializer.Serialize(new { success = true, entryId = entry.Id.ToString(), message = $"Entry '{title}' created in category '{category.Name}'" });
        }

        #endregion

        #region Screenshot/Asset Handlers

        private string GetCurrentScreenshotsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            var result = vm.CurrentDataAssets.Select(a => new
            {
                id = a.Id.ToString(),
                name = a.Name,
                description = a.Description,
                type = a.AssetType.ToString(),
                capturedAt = a.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                hasImage = a.FullImage != null
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, screenshots = result });
        }

        #endregion

        #region System Handlers

        private string GetAppSummaryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            var overdueTasks = vm.Tasks.Count(t => t.IsOverdue && t.Status != TaskStatus.Completed);
            var inProgressTasks = vm.Tasks.Count(t => t.Status == TaskStatus.InProgress);
            var upcomingReminders = vm.Reminders.Where(r => !r.IsCompleted && !r.IsOverdue).OrderBy(r => r.DueDate).Take(3);
            var overdueReminders = vm.Reminders.Count(r => r.IsOverdue && !r.IsCompleted);

            return JsonSerializer.Serialize(new
            {
                tasks = new
                {
                    total = vm.Tasks.Count,
                    overdue = overdueTasks,
                    inProgress = inProgressTasks,
                    completed = vm.Tasks.Count(t => t.Status == TaskStatus.Completed)
                },
                reminders = new
                {
                    total = vm.Reminders.Count,
                    active = vm.Reminders.Count(r => !r.IsCompleted),
                    overdue = overdueReminders,
                    upcoming = upcomingReminders.Select(r => new { title = r.Title, dueDate = r.DueDate.ToString("g") })
                },
                dataBank = new
                {
                    categories = vm.DataBankCategories.Count,
                    totalEntries = vm.DataBankCategories.Sum(c => c.EntryCount)
                },
                screenshots = new
                {
                    current = vm.CurrentDataAssets.Count
                }
            });
        }

        #endregion

        #region Helpers

        private static bool TryGetArg<T>(Dictionary<string, object> args, string key, out T value)
        {
            value = default!;
            
            if (!args.TryGetValue(key, out var obj))
                return false;

            try
            {
                if (obj is JsonElement jsonElement)
                {
                    if (typeof(T) == typeof(string))
                    {
                        value = (T)(object)jsonElement.GetString()!;
                        return true;
                    }
                    if (typeof(T) == typeof(bool))
                    {
                        value = (T)(object)jsonElement.GetBoolean();
                        return true;
                    }
                    if (typeof(T) == typeof(int))
                    {
                        value = (T)(object)jsonElement.GetInt32();
                        return true;
                    }
                }
                else if (obj is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
                else
                {
                    value = (T)Convert.ChangeType(obj, typeof(T));
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        #endregion
    }
}
