using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AIA.Models;
using AIA.Models.AI;
using AIA.Models.Automation;
using AIA.Services.Automation;
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

        // Current automation context (set during automation execution)
        private AutomationContext? _currentAutomationContext;

        public AIToolsService(Func<OverlayViewModel> getViewModel)
        {
            _getViewModel = getViewModel;
            RegisterAllTools();
        }

        public IEnumerable<AITool> GetAllTools() => _tools.Values;

        public AITool? GetTool(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

        /// <summary>
        /// Sets the current automation context for variable resolution
        /// </summary>
        public void SetAutomationContext(AutomationContext? context)
        {
            _currentAutomationContext = context;
        }

        /// <summary>
        /// Registers a custom tool (e.g., from a plugin)
        /// </summary>
        public void RegisterCustomTool(AITool tool)
        {
            if (string.IsNullOrEmpty(tool.Name))
                throw new ArgumentException("Tool name is required", nameof(tool));
            
            _tools[tool.Name] = tool;
        }

        /// <summary>
        /// Unregisters a custom tool
        /// </summary>
        public void UnregisterCustomTool(string name)
        {
            _tools.Remove(name);
        }

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
                Name = "add_subtask",
                Description = "Add a subtask to an existing task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["parent_task_title"] = new AIToolParameter { Type = "string", Description = "The title of the parent task (partial match)", Required = false },
                    ["parent_task_id"] = new AIToolParameter { Type = "string", Description = "The ID of the parent task", Required = false },
                    ["title"] = new AIToolParameter { Type = "string", Description = "The subtask title", Required = true },
                    ["description"] = new AIToolParameter { Type = "string", Description = "Subtask description", Required = false },
                    ["priority"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Subtask priority",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Critical" }
                    },
                    ["due_date"] = new AIToolParameter { Type = "string", Description = "Due date in ISO format (YYYY-MM-DD)", Required = false }
                },
                Handler = AddSubtaskHandler
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

            // === NEW EDIT TOOLS ===

            RegisterTool(new AITool
            {
                Name = "update_task",
                Description = "Update multiple properties of an existing task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["task_id"] = new AIToolParameter { Type = "string", Description = "The task ID", Required = false },
                    ["task_title"] = new AIToolParameter { Type = "string", Description = "The task title to find (partial match)", Required = false },
                    ["new_title"] = new AIToolParameter { Type = "string", Description = "New task title", Required = false },
                    ["description"] = new AIToolParameter { Type = "string", Description = "New description", Required = false },
                    ["notes"] = new AIToolParameter { Type = "string", Description = "New notes", Required = false },
                    ["status"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "New status",
                        Required = false,
                        Enum = new[] { "NotStarted", "InProgress", "OnHold", "Completed", "Cancelled" }
                    },
                    ["priority"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "New priority",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Critical" }
                    },
                    ["due_date"] = new AIToolParameter { Type = "string", Description = "New due date in ISO format (YYYY-MM-DD), or 'clear' to remove", Required = false }
                },
                Handler = UpdateTaskHandler
            });

            RegisterTool(new AITool
            {
                Name = "delete_task",
                Description = "Delete a task by ID or title",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["task_id"] = new AIToolParameter { Type = "string", Description = "The task ID", Required = false },
                    ["task_title"] = new AIToolParameter { Type = "string", Description = "The task title to find (partial match)", Required = false }
                },
                Handler = DeleteTaskHandler
            });

            RegisterTool(new AITool
            {
                Name = "update_subtask",
                Description = "Update a subtask within a parent task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["subtask_id"] = new AIToolParameter { Type = "string", Description = "The subtask ID", Required = false },
                    ["subtask_title"] = new AIToolParameter { Type = "string", Description = "The subtask title to find (partial match)", Required = false },
                    ["parent_task_id"] = new AIToolParameter { Type = "string", Description = "Parent task ID to narrow search", Required = false },
                    ["new_title"] = new AIToolParameter { Type = "string", Description = "New subtask title", Required = false },
                    ["description"] = new AIToolParameter { Type = "string", Description = "New description", Required = false },
                    ["status"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "New status",
                        Required = false,
                        Enum = new[] { "NotStarted", "InProgress", "OnHold", "Completed", "Cancelled" }
                    },
                    ["priority"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "New priority",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Critical" }
                    },
                    ["due_date"] = new AIToolParameter { Type = "string", Description = "New due date in ISO format (YYYY-MM-DD), or 'clear' to remove", Required = false }
                },
                Handler = UpdateSubtaskHandler
            });

            RegisterTool(new AITool
            {
                Name = "delete_subtask",
                Description = "Delete a subtask from a parent task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["subtask_id"] = new AIToolParameter { Type = "string", Description = "The subtask ID", Required = false },
                    ["subtask_title"] = new AIToolParameter { Type = "string", Description = "The subtask title to find (partial match)", Required = false },
                    ["parent_task_id"] = new AIToolParameter { Type = "string", Description = "Parent task ID to narrow search", Required = false }
                },
                Handler = DeleteSubtaskHandler
            });

            RegisterTool(new AITool
            {
                Name = "update_reminder",
                Description = "Update an existing reminder",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["reminder_id"] = new AIToolParameter { Type = "string", Description = "The reminder ID", Required = false },
                    ["reminder_title"] = new AIToolParameter { Type = "string", Description = "The reminder title to find (partial match)", Required = false },
                    ["new_title"] = new AIToolParameter { Type = "string", Description = "New reminder title", Required = false },
                    ["due_datetime"] = new AIToolParameter { Type = "string", Description = "New due date/time in ISO format", Required = false },
                    ["severity"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "New severity",
                        Required = false,
                        Enum = new[] { "Low", "Medium", "High", "Urgent" }
                    },
                    ["completed"] = new AIToolParameter { Type = "boolean", Description = "Mark as completed/incomplete", Required = false }
                },
                Handler = UpdateReminderHandler
            });

            RegisterTool(new AITool
            {
                Name = "delete_reminder",
                Description = "Delete a reminder by ID or title",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["reminder_id"] = new AIToolParameter { Type = "string", Description = "The reminder ID", Required = false },
                    ["reminder_title"] = new AIToolParameter { Type = "string", Description = "The reminder title to find (partial match)", Required = false }
                },
                Handler = DeleteReminderHandler
            });

            RegisterTool(new AITool
            {
                Name = "update_databank_entry",
                Description = "Update an existing data bank entry",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["entry_id"] = new AIToolParameter { Type = "string", Description = "The entry ID", Required = false },
                    ["entry_title"] = new AIToolParameter { Type = "string", Description = "The entry title to find (partial match)", Required = false },
                    ["category_name"] = new AIToolParameter { Type = "string", Description = "Category name to narrow search", Required = false },
                    ["new_title"] = new AIToolParameter { Type = "string", Description = "New entry title", Required = false },
                    ["content"] = new AIToolParameter { Type = "string", Description = "New content", Required = false },
                    ["tags"] = new AIToolParameter { Type = "string", Description = "New tags (comma-separated)", Required = false }
                },
                Handler = UpdateDataBankEntryHandler
            });

            RegisterTool(new AITool
            {
                Name = "delete_databank_entry",
                Description = "Delete a data bank entry by ID or title",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["entry_id"] = new AIToolParameter { Type = "string", Description = "The entry ID", Required = false },
                    ["entry_title"] = new AIToolParameter { Type = "string", Description = "The entry title to find (partial match)", Required = false },
                    ["category_name"] = new AIToolParameter { Type = "string", Description = "Category name to narrow search", Required = false }
                },
                Handler = DeleteDataBankEntryHandler
            });

            RegisterTool(new AITool
            {
                Name = "add_task_tags",
                Description = "Add tags to a task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["task_id"] = new AIToolParameter { Type = "string", Description = "The task ID", Required = false },
                    ["task_title"] = new AIToolParameter { Type = "string", Description = "The task title to find (partial match)", Required = false },
                    ["tags"] = new AIToolParameter { Type = "string", Description = "Comma-separated tags to add", Required = true }
                },
                Handler = AddTaskTagsHandler
            });

            RegisterTool(new AITool
            {
                Name = "remove_task_tags",
                Description = "Remove tags from a task",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["task_id"] = new AIToolParameter { Type = "string", Description = "The task ID", Required = false },
                    ["task_title"] = new AIToolParameter { Type = "string", Description = "The task title to find (partial match)", Required = false },
                    ["tags"] = new AIToolParameter { Type = "string", Description = "Comma-separated tags to remove", Required = true }
                },
                Handler = RemoveTaskTagsHandler
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

            RegisterTool(new AITool
            {
                Name = "list_available_screenshots",
                Description = "**IMPORTANT: You CAN see the user's screen!** This tool lists all available screenshots/data assets with metadata. The user has likely already captured screenshots that you can analyze. Always check this first when the user asks about what's on their screen, what they're looking at, or to help with visual tasks. Use this to discover what screenshots are available, then use get_screenshot_base64 to retrieve and analyze them.",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = ListAvailableScreenshotsHandler
            });

            RegisterTool(new AITool
            {
                Name = "get_screenshot_base64",
                Description = "Get a screenshot in base64-encoded PNG format for visual analysis. Use the screenshot ID from list_available_screenshots. This allows you to see and analyze what's on the user's screen - you can identify UI elements, read text, describe images, help with visual problems, etc. Always use this when the user asks about their screen or shows you something visually.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["screenshot_id"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "The ID of the screenshot to retrieve (from list_available_screenshots)",
                        Required = true
                    }
                },
                Handler = GetScreenshotBase64Handler
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

            // === NEW AUTOMATION TOOLS ===

            // Notification Tools
            RegisterTool(new AITool
            {
                Name = "show_notification",
                Description = "Show a notification to the user. Use for important messages, alerts, or status updates.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["title"] = new AIToolParameter { Type = "string", Description = "Notification title", Required = false },
                    ["message"] = new AIToolParameter { Type = "string", Description = "Notification message", Required = true },
                    ["type"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Notification type for styling",
                        Required = false,
                        Enum = new[] { "info", "success", "warning", "error" }
                    },
                    ["duration"] = new AIToolParameter { Type = "integer", Description = "Duration in seconds (default 5)", Required = false }
                },
                Handler = ShowNotificationHandler
            });

            // Clipboard Tools
            RegisterTool(new AITool
            {
                Name = "copy_to_clipboard",
                Description = "Copy text content to the system clipboard",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["content"] = new AIToolParameter { Type = "string", Description = "Text content to copy to clipboard", Required = true }
                },
                Handler = CopyToClipboardHandler
            });

            RegisterTool(new AITool
            {
                Name = "get_clipboard_text",
                Description = "Get the current text content from the system clipboard",
                Parameters = new Dictionary<string, AIToolParameter>(),
                Handler = GetClipboardTextHandler
            });

            // Clipboard History Tool
            RegisterTool(new AITool
            {
                Name = "get_clipboard_history",
                Description = "Get the clipboard history - a list of recently copied items including text, images, and file paths. Returns up to the configured maximum number of items.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["limit"] = new AIToolParameter
                    {
                        Type = "integer",
                        Description = "Maximum number of items to return (default 10)",
                        Required = false
                    },
                    ["type"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "Filter by content type",
                        Required = false,
                        Enum = new[] { "text", "image", "files", "all" }
                    }
                },
                Handler = GetClipboardHistoryHandler
            });

            // New tool to retrieve clipboard image as base64 (similar to screenshot retrieval)
            RegisterTool(new AITool
            {
                Name = "get_clipboard_image_base64",
                Description = "**IMPORTANT: You CAN see clipboard images!** Get a clipboard image in base64-encoded PNG format for visual analysis. Use the clipboard item ID from get_clipboard_history. This allows you to see and analyze what was copied to the clipboard - you can identify content, read text from images, describe what was copied, etc. Always use this when the user asks about images in their clipboard history.",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["clipboard_id"] = new AIToolParameter
                    {
                        Type = "string",
                        Description = "The ID of the clipboard item to retrieve (from get_clipboard_history)",
                        Required = true
                    }
                },
                Handler = GetClipboardImageBase64Handler
            });

            // File Tools
            RegisterTool(new AITool
            {
                Name = "save_to_file",
                Description = "Save text content to a file",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["content"] = new AIToolParameter { Type = "string", Description = "Content to save", Required = true },
                    ["filename"] = new AIToolParameter { Type = "string", Description = "File name (saved to Documents folder if no path specified)", Required = true },
                    ["folder"] = new AIToolParameter { Type = "string", Description = "Optional folder path (defaults to Documents)", Required = false },
                    ["append"] = new AIToolParameter { Type = "boolean", Description = "Append to existing file instead of overwriting", Required = false }
                },
                Handler = SaveToFileHandler
            });

            RegisterTool(new AITool
            {
                Name = "read_file",
                Description = "Read text content from a file",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["filepath"] = new AIToolParameter { Type = "string", Description = "Full path to the file", Required = true }
                },
                Handler = ReadFileHandler
            });

            // Automation Tools
            RegisterTool(new AITool
            {
                Name = "list_automations",
                Description = "Get a list of available automations",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["include_disabled"] = new AIToolParameter { Type = "boolean", Description = "Include disabled automations", Required = false }
                },
                Handler = ListAutomationsHandler
            });

            RegisterTool(new AITool
            {
                Name = "trigger_automation",
                Description = "Trigger an automation to run",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["name"] = new AIToolParameter { Type = "string", Description = "Name of the automation to trigger", Required = false },
                    ["id"] = new AIToolParameter { Type = "string", Description = "ID of the automation to trigger", Required = false }
                },
                Handler = TriggerAutomationHandler
            });

            // Context Variables Tool (for automation context)
            RegisterTool(new AITool
            {
                Name = "get_context_variable",
                Description = "Get a variable from the current automation context",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["name"] = new AIToolParameter { Type = "string", Description = "Variable name", Required = true }
                },
                Handler = GetContextVariableHandler
            });

            RegisterTool(new AITool
            {
                Name = "set_context_variable",
                Description = "Set a variable in the current automation context",
                Parameters = new Dictionary<string, AIToolParameter>
                {
                    ["name"] = new AIToolParameter { Type = "string", Description = "Variable name", Required = true },
                    ["value"] = new AIToolParameter { Type = "string", Description = "Variable value", Required = true }
                },
                Handler = SetContextVariableHandler
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

            // Dispatch to UI thread for collection modification
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.Tasks.Add(task);
                vm.ApplyTaskFilter();
            });
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, taskId = task.Id.ToString(), message = $"Task '{title}' created successfully" });
        }

        private string AddSubtaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title) || string.IsNullOrWhiteSpace(title))
            {
                return JsonSerializer.Serialize(new { error = "Subtask title is required" });
            }

            // Find parent task by ID or title
            TaskItem? parentTask = null;

            if (TryGetArg<string>(args, "parent_task_id", out var id) && Guid.TryParse(id, out var guid))
            {
                parentTask = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "parent_task_title", out var parentTitle))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    parentTask = vm.Tasks.FirstOrDefault(t => t.Title.Contains(parentTitle, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (parentTask == null)
            {
                return JsonSerializer.Serialize(new { error = "Parent task not found. Provide either 'parent_task_id' or 'parent_task_title' parameter." });
            }

            // Create subtask
            var subtask = new TaskItem 
            { 
                Title = title,
                Status = TaskStatus.NotStarted,
                Priority = TaskPriority.Medium
            };

            if (TryGetArg<string>(args, "description", out var desc))
                subtask.Description = desc;

            if (TryGetArg<string>(args, "priority", out var priority) && Enum.TryParse<TaskPriority>(priority, out var priorityEnum))
                subtask.Priority = priorityEnum;

            if (TryGetArg<string>(args, "due_date", out var dueDate) && DateTime.TryParse(dueDate, out var dueDateValue))
                subtask.DueDate = dueDateValue;

            // Add subtask to parent
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                parentTask.AddSubtask(subtask);
            });
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                subtaskId = subtask.Id.ToString(), 
                parentTaskId = parentTask.Id.ToString(),
                message = $"Subtask '{title}' added to task '{parentTask.Title}'" 
            });
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

            TaskItem? task = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            });
            
            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found" });
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                task.Status = statusEnum;
            });
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, message = $"Task '{task.Title}' status updated to {statusEnum}" });
        }

        private string UpdateTaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? task = null;

            // Find task by ID or title
            if (TryGetArg<string>(args, "task_id", out var id) && Guid.TryParse(id, out var guid))
            {
                task = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "task_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found. Provide either 'task_id' or 'task_title' parameter." });
            }

            var changes = new List<string>();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (TryGetArg<string>(args, "new_title", out var newTitle) && !string.IsNullOrWhiteSpace(newTitle))
                {
                    task.Title = newTitle;
                    changes.Add("title");
                }

                if (TryGetArg<string>(args, "description", out var desc))
                {
                    task.Description = desc;
                    changes.Add("description");
                }

                if (TryGetArg<string>(args, "notes", out var notes))
                {
                    task.Notes = notes;
                    changes.Add("notes");
                }

                if (TryGetArg<string>(args, "status", out var status) && Enum.TryParse<TaskStatus>(status, out var statusEnum))
                {
                    task.Status = statusEnum;
                    changes.Add("status");
                }

                if (TryGetArg<string>(args, "priority", out var priority) && Enum.TryParse<TaskPriority>(priority, out var priorityEnum))
                {
                    task.Priority = priorityEnum;
                    changes.Add("priority");
                }

                if (TryGetArg<string>(args, "due_date", out var dueDate))
                {
                    if (dueDate.ToLowerInvariant() == "clear")
                    {
                        task.DueDate = null;
                        changes.Add("due_date (cleared)");
                    }
                    else if (DateTime.TryParse(dueDate, out var dueDateValue))
                    {
                        task.DueDate = dueDateValue;
                        changes.Add("due_date");
                    }
                }
            });

            if (changes.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "No valid updates provided" });
            }

            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                taskId = task.Id.ToString(),
                message = $"Task '{task.Title}' updated: {string.Join(", ", changes)}" 
            });
        }

        private string DeleteTaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? task = null;

            if (TryGetArg<string>(args, "task_id", out var id) && Guid.TryParse(id, out var guid))
            {
                task = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "task_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found. Provide either 'task_id' or 'task_title' parameter." });
            }

            var taskTitle = task.Title;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.DeleteTask(task);
            });

            return JsonSerializer.Serialize(new { success = true, message = $"Task '{taskTitle}' deleted successfully" });
        }

        private string UpdateSubtaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? subtask = null;

            // Find subtask by ID or title
            if (TryGetArg<string>(args, "subtask_id", out var id) && Guid.TryParse(id, out var guid))
            {
                subtask = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "subtask_title", out var title))
            {
                // If parent task ID is provided, search within that parent only
                if (TryGetArg<string>(args, "parent_task_id", out var parentId) && Guid.TryParse(parentId, out var parentGuid))
                {
                    var parentTask = vm.FindTaskById(parentGuid);
                    if (parentTask != null)
                    {
                        subtask = parentTask.Subtasks.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    // Search all tasks for subtasks
                    foreach (var task in vm.Tasks)
                    {
                        subtask = task.Subtasks.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                        if (subtask != null) break;
                    }
                }
            }

            if (subtask == null || !subtask.IsSubtask)
            {
                return JsonSerializer.Serialize(new { error = "Subtask not found. Provide either 'subtask_id' or 'subtask_title' parameter." });
            }

            var changes = new List<string>();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (TryGetArg<string>(args, "new_title", out var newTitle) && !string.IsNullOrWhiteSpace(newTitle))
                {
                    subtask.Title = newTitle;
                    changes.Add("title");
                }

                if (TryGetArg<string>(args, "description", out var desc))
                {
                    subtask.Description = desc;
                    changes.Add("description");
                }

                if (TryGetArg<string>(args, "status", out var status) && Enum.TryParse<TaskStatus>(status, out var statusEnum))
                {
                    subtask.Status = statusEnum;
                    changes.Add("status");
                }

                if (TryGetArg<string>(args, "priority", out var priority) && Enum.TryParse<TaskPriority>(priority, out var priorityEnum))
                {
                    subtask.Priority = priorityEnum;
                    changes.Add("priority");
                }

                if (TryGetArg<string>(args, "due_date", out var dueDate))
                {
                    if (dueDate.ToLowerInvariant() == "clear")
                    {
                        subtask.DueDate = null;
                        changes.Add("due_date (cleared)");
                    }
                    else if (DateTime.TryParse(dueDate, out var dueDateValue))
                    {
                        subtask.DueDate = dueDateValue;
                        changes.Add("due_date");
                    }
                }
            });

            if (changes.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "No valid updates provided" });
            }

            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                subtaskId = subtask.Id.ToString(),
                parentTaskId = subtask.ParentTaskId?.ToString(),
                message = $"Subtask '{subtask.Title}' updated: {string.Join(", ", changes)}" 
            });
        }

        private string DeleteSubtaskHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? subtask = null;
            TaskItem? parentTask = null;

            // Find subtask by ID or title
            if (TryGetArg<string>(args, "subtask_id", out var id) && Guid.TryParse(id, out var guid))
            {
                subtask = vm.FindTaskById(guid);
                if (subtask?.ParentTaskId.HasValue == true)
                {
                    parentTask = vm.FindTaskById(subtask.ParentTaskId.Value);
                }
            }
            else if (TryGetArg<string>(args, "subtask_title", out var title))
            {
                // If parent task ID is provided, search within that parent only
                if (TryGetArg<string>(args, "parent_task_id", out var parentId) && Guid.TryParse(parentId, out var parentGuid))
                {
                    parentTask = vm.FindTaskById(parentGuid);
                    if (parentTask != null)
                    {
                        subtask = parentTask.Subtasks.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    // Search all tasks for subtasks
                    foreach (var task in vm.Tasks)
                    {
                        subtask = task.Subtasks.FirstOrDefault(s => s.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                        if (subtask != null)
                        {
                            parentTask = task;
                            break;
                        }
                    }
                }
            }

            if (subtask == null || parentTask == null)
            {
                return JsonSerializer.Serialize(new { error = "Subtask not found. Provide either 'subtask_id' or 'subtask_title' parameter." });
            }

            var subtaskTitle = subtask.Title;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                parentTask.Subtasks.Remove(subtask);
                parentTask.RefreshSubtaskProgress();
            });
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, message = $"Subtask '{subtaskTitle}' deleted successfully" });
        }

        private string UpdateReminderHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            ReminderItem? reminder = null;

            // Find reminder by ID or title
            if (TryGetArg<string>(args, "reminder_id", out var id) && Guid.TryParse(id, out var guid))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    reminder = vm.Reminders.FirstOrDefault(r => r.Id == guid);
                });
            }
            else if (TryGetArg<string>(args, "reminder_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    reminder = vm.Reminders.FirstOrDefault(r => r.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (reminder == null)
            {
                return JsonSerializer.Serialize(new { error = "Reminder not found. Provide either 'reminder_id' or 'reminder_title' parameter." });
            }

            var changes = new List<string>();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (TryGetArg<string>(args, "new_title", out var newTitle) && !string.IsNullOrWhiteSpace(newTitle))
                {
                    reminder.Title = newTitle;
                    changes.Add("title");
                }

                if (TryGetArg<string>(args, "due_datetime", out var dueDateTime) && DateTime.TryParse(dueDateTime, out var dueDateTimeValue))
                {
                    reminder.DueDate = dueDateTimeValue;
                    changes.Add("due_datetime");
                }

                if (TryGetArg<string>(args, "severity", out var severity) && Enum.TryParse<ReminderSeverity>(severity, out var severityEnum))
                {
                    reminder.Severity = severityEnum;
                    changes.Add("severity");
                }

                if (TryGetArg<bool>(args, "completed", out var completed))
                {
                    reminder.IsCompleted = completed;
                    changes.Add(completed ? "marked completed" : "marked incomplete");
                }
            });

            if (changes.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "No valid updates provided" });
            }

            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                reminderId = reminder.Id.ToString(),
                message = $"Reminder '{reminder.Title}' updated: {string.Join(", ", changes)}" 
            });
        }

        private string DeleteReminderHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            ReminderItem? reminder = null;

            if (TryGetArg<string>(args, "reminder_id", out var id) && Guid.TryParse(id, out var guid))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    reminder = vm.Reminders.FirstOrDefault(r => r.Id == guid);
                });
            }
            else if (TryGetArg<string>(args, "reminder_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    reminder = vm.Reminders.FirstOrDefault(r => r.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (reminder == null)
            {
                return JsonSerializer.Serialize(new { error = "Reminder not found. Provide either 'reminder_id' or 'reminder_title' parameter." });
            }

            var reminderTitle = reminder.Title;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.DeleteReminder(reminder);
            });

            return JsonSerializer.Serialize(new { success = true, message = $"Reminder '{reminderTitle}' deleted successfully" });
        }

        private string UpdateDataBankEntryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            DataBankEntry? entry = null;

            // Find entry by ID or title
            if (TryGetArg<string>(args, "entry_id", out var id) && Guid.TryParse(id, out var guid))
            {
                // Search across all categories
                foreach (var category in vm.DataBankCategories)
                {
                    var previousCategory = vm.SelectedCategory;
                    vm.SelectedCategory = category;
                    
                    entry = vm.CurrentCategoryEntries.FirstOrDefault(e => e.Id == guid);
                    
                    if (entry != null)
                    {
                        vm.SelectedCategory = previousCategory;
                        break;
                    }
                    vm.SelectedCategory = previousCategory;
                }
            }
            else if (TryGetArg<string>(args, "entry_title", out var title))
            {
                var searchCategories = vm.DataBankCategories.AsEnumerable();
                
                // Narrow search by category if provided
                if (TryGetArg<string>(args, "category_name", out var categoryName))
                {
                    searchCategories = searchCategories.Where(c => c.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var category in searchCategories)
                {
                    var previousCategory = vm.SelectedCategory;
                    vm.SelectedCategory = category;
                    
                    entry = vm.CurrentCategoryEntries.FirstOrDefault(e => e.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                    
                    if (entry != null)
                    {
                        vm.SelectedCategory = previousCategory;
                        break;
                    }
                    vm.SelectedCategory = previousCategory;
                }
            }

            if (entry == null)
            {
                return JsonSerializer.Serialize(new { error = "Entry not found. Provide either 'entry_id' or 'entry_title' parameter." });
            }

            var changes = new List<string>();

            if (TryGetArg<string>(args, "new_title", out var newTitle) && !string.IsNullOrWhiteSpace(newTitle))
            {
                entry.Title = newTitle;
                changes.Add("title");
            }

            if (TryGetArg<string>(args, "content", out var content))
            {
                entry.Content = content;
                changes.Add("content");
            }

            if (TryGetArg<string>(args, "tags", out var tags))
            {
                entry.Tags = tags;
                changes.Add("tags");
            }

            if (changes.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = "No valid updates provided" });
            }

            _ = vm.SaveDataBanksAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                entryId = entry.Id.ToString(),
                message = $"Entry '{entry.Title}' updated: {string.Join(", ", changes)}" 
            });
        }

        private string DeleteDataBankEntryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            DataBankEntry? entry = null;
            DataBankCategory? entryCategory = null;

            // Find entry by ID or title
            if (TryGetArg<string>(args, "entry_id", out var id) && Guid.TryParse(id, out var guid))
            {
                foreach (var category in vm.DataBankCategories)
                {
                    var previousCategory = vm.SelectedCategory;
                    vm.SelectedCategory = category;
                    
                    entry = vm.CurrentCategoryEntries.FirstOrDefault(e => e.Id == guid);
                    
                    if (entry != null)
                    {
                        entryCategory = category;
                        vm.SelectedCategory = previousCategory;
                        break;
                    }
                    vm.SelectedCategory = previousCategory;
                }
            }
            else if (TryGetArg<string>(args, "entry_title", out var title))
            {
                var searchCategories = vm.DataBankCategories.AsEnumerable();
                
                if (TryGetArg<string>(args, "category_name", out var categoryName))
                {
                    searchCategories = searchCategories.Where(c => c.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var category in searchCategories)
                {
                    var previousCategory = vm.SelectedCategory;
                    vm.SelectedCategory = category;
                    
                    entry = vm.CurrentCategoryEntries.FirstOrDefault(e => e.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                    
                    if (entry != null)
                    {
                        entryCategory = category;
                        vm.SelectedCategory = previousCategory;
                        break;
                    }
                    vm.SelectedCategory = previousCategory;
                }
            }

            if (entry == null)
            {
                return JsonSerializer.Serialize(new { error = "Entry not found. Provide either 'entry_id' or 'entry_title' parameter." });
            }

            var entryTitle = entry.Title;
            _ = vm.DeleteEntryAsync(entry);

            return JsonSerializer.Serialize(new { success = true, message = $"Entry '{entryTitle}' deleted successfully" });
        }

        private string AddTaskTagsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? task = null;

            if (TryGetArg<string>(args, "task_id", out var id) && Guid.TryParse(id, out var guid))
            {
                task = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "task_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found. Provide either 'task_id' or 'task_title' parameter." });
            }

            if (!TryGetArg<string>(args, "tags", out var tagsStr) || string.IsNullOrWhiteSpace(tagsStr))
            {
                return JsonSerializer.Serialize(new { error = "Tags parameter is required" });
            }

            var tags = tagsStr.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var addedTags = new List<string>();

            foreach (var tag in tags)
            {
                if (!task.Tags.Contains(tag))
                {
                    task.Tags.Add(tag);
                    addedTags.Add(tag);
                }
            }

            if (addedTags.Count == 0)
            {
                return JsonSerializer.Serialize(new { success = true, message = "No new tags to add (all tags already exist)" });
            }

            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                message = $"Added {addedTags.Count} tag(s) to task '{task.Title}': {string.Join(", ", addedTags)}" 
            });
        }

        private string RemoveTaskTagsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            TaskItem? task = null;

            if (TryGetArg<string>(args, "task_id", out var id) && Guid.TryParse(id, out var guid))
            {
                task = vm.FindTaskById(guid);
            }
            else if (TryGetArg<string>(args, "task_title", out var title))
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    task = vm.Tasks.FirstOrDefault(t => t.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (task == null)
            {
                return JsonSerializer.Serialize(new { error = "Task not found. Provide either 'task_id' or 'task_title' parameter." });
            }

            if (!TryGetArg<string>(args, "tags", out var tagsStr) || string.IsNullOrWhiteSpace(tagsStr))
            {
                return JsonSerializer.Serialize(new { error = "Tags parameter is required" });
            }

            var tags = tagsStr.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var removedTags = new List<string>();

            foreach (var tag in tags)
            {
                if (task.Tags.Remove(tag))
                {
                    removedTags.Add(tag);
                }
            }

            if (removedTags.Count == 0)
            {
                return JsonSerializer.Serialize(new { success = true, message = "No tags removed (tags not found on task)" });
            }

            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                message = $"Removed {removedTags.Count} tag(s) from task '{task.Title}': {string.Join(", ", removedTags)}" 
            });
        }

        #endregion

        #region Screenshot Handlers

        private string GetCurrentScreenshotsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var screenshots = vm.CurrentDataAssets;

            var result = screenshots.Select(asset => new
            {
                id = asset.Id.ToString(),
                name = asset.Name,
                description = asset.Description,
                type = asset.AssetType.ToString(),
                capturedAt = asset.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, screenshots = result });
        }

        private string ListAvailableScreenshotsHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var screenshots = vm.CurrentDataAssets;

            if (screenshots.Count == 0)
            {
                return JsonSerializer.Serialize(new 
                { 
                    screenshots = new List<object>(),
                    total = 0,
                    message = "No screenshots currently available. Request the user to capture screenshots first."
                });
            }

            var result = screenshots.Select((asset, index) => new
            {
                id = asset.Id.ToString(),
                index = index,
                name = asset.Name,
                description = asset.Description,
                type = asset.AssetType.ToString(),
                capturedAt = asset.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                width = asset.FullImage?.PixelWidth ?? 0,
                height = asset.FullImage?.PixelHeight ?? 0
            }).ToList();

            return JsonSerializer.Serialize(new 
            { 
                screenshots = result,
                total = result.Count,
                message = "List of available screenshots. Use the 'id' value to retrieve a screenshot in base64 format with get_screenshot_base64."
            });
        }

        private string GetScreenshotBase64Handler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "screenshot_id", out var screenshotIdStr))
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = "screenshot_id parameter is required"
                });
            }

            if (!Guid.TryParse(screenshotIdStr, out var screenshotId))
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Invalid screenshot ID format: {screenshotIdStr}"
                });
            }

            var screenshot = vm.CurrentDataAssets.FirstOrDefault(a => a.Id == screenshotId);
            if (screenshot == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Screenshot with ID {screenshotIdStr} not found. Use list_available_screenshots to get valid IDs."
                });
            }

            if (screenshot.FullImage == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = "Screenshot image data is not available"
                });
            }

            try
            {
                // Convert BitmapSource to PNG bytes, then to base64
                var pngEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                pngEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot.FullImage));

                byte[] pngBytes;
                using (var memoryStream = new MemoryStream())
                {
                    pngEncoder.Save(memoryStream);
                    pngBytes = memoryStream.ToArray();
                }

                var base64String = Convert.ToBase64String(pngBytes);

                // Return a special response format that the orchestration service will detect
                // and convert into a proper multimodal vision message
                return JsonSerializer.Serialize(new 
                { 
                    _imageResponse = true,  // Special marker for orchestration service
                    success = true,
                    id = screenshot.Id.ToString(),
                    name = screenshot.Name,
                    description = screenshot.Description,
                    type = screenshot.AssetType.ToString(),
                    width = screenshot.FullImage.PixelWidth,
                    height = screenshot.FullImage.PixelHeight,
                    capturedAt = screenshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    mimeType = "image/png",
                    base64 = base64String
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Failed to encode screenshot as base64: {ex.Message}"
                });
            }
        }

        #endregion

        #region Reminder Handlers

        private string GetRemindersHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var reminders = vm.Reminders.AsEnumerable();

            var includeCompleted = TryGetArg<bool>(args, "include_completed", out var inc) && inc;
            if (!includeCompleted)
            {
                reminders = reminders.Where(r => !r.IsCompleted);
            }

            if (TryGetArg<string>(args, "severity", out var severity) && Enum.TryParse<ReminderSeverity>(severity, out var severityEnum))
            {
                reminders = reminders.Where(r => r.Severity == severityEnum);
            }

            var result = reminders.Select(r => new
            {
                id = r.Id.ToString(),
                title = r.Title,
                dueDate = r.DueDate.ToString("yyyy-MM-dd HH:mm"),
                severity = r.Severity.ToString(),
                isCompleted = r.IsCompleted,
                isOverdue = r.IsOverdue,
                timeLeft = r.TimeLeftText
            }).OrderBy(r => r.dueDate).ToList();

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
                return JsonSerializer.Serialize(new { error = "Valid due_datetime in ISO format is required" });
            }

            var reminder = new ReminderItem
            {
                Title = title,
                DueDate = dueDateTime,
                Severity = ReminderSeverity.Medium
            };

            if (TryGetArg<string>(args, "severity", out var severity) && Enum.TryParse<ReminderSeverity>(severity, out var severityEnum))
            {
                reminder.Severity = severityEnum;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.Reminders.Add(reminder);
            });
            _ = vm.SaveTasksAndRemindersAsync();

            return JsonSerializer.Serialize(new { success = true, reminderId = reminder.Id.ToString(), message = $"Reminder '{title}' created successfully" });
        }

        private string SnoozeReminderHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "title", out var title))
            {
                return JsonSerializer.Serialize(new { error = "Title is required" });
            }

            ReminderItem? reminder = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                reminder = vm.Reminders.FirstOrDefault(r => r.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            });

            if (reminder == null)
            {
                return JsonSerializer.Serialize(new { error = "Reminder not found" });
            }

            var minutes = TryGetArg<int>(args, "minutes", out var min) ? min : 15;
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                vm.SnoozeReminder(reminder, minutes);
            });

            return JsonSerializer.Serialize(new { success = true, message = $"Reminder '{reminder.Title}' snoozed for {minutes} minutes" });
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
                color = c.Color,
                entryCount = c.EntryCount
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

            var previousCategory = vm.SelectedCategory;
            vm.SelectedCategory = category;

            var entries = vm.CurrentCategoryEntries.AsEnumerable();

            if (TryGetArg<string>(args, "search_query", out var searchQuery) && !string.IsNullOrWhiteSpace(searchQuery))
            {
                entries = entries.Where(e => e.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
            }

            var result = entries.Select(e => new
            {
                id = e.Id.ToString(),
                title = e.Title,
                entryType = e.EntryType.ToString(),
                tags = e.Tags,
                createdDate = e.CreatedDate.ToString("yyyy-MM-dd HH:mm")
            }).ToList();

            vm.SelectedCategory = previousCategory;

            return JsonSerializer.Serialize(new { count = result.Count, entries = result });
        }

        private string GetEntryContentHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "entry_title", out var entryTitle))
            {
                return JsonSerializer.Serialize(new { error = "entry_title is required" });
            }

            DataBankEntry? entry = null;
            foreach (var category in vm.DataBankCategories)
            {
                var previousCategory = vm.SelectedCategory;
                vm.SelectedCategory = category;
                
                entry = vm.CurrentCategoryEntries.FirstOrDefault(e => e.Title.Contains(entryTitle, StringComparison.OrdinalIgnoreCase));
                
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
                content = entry.Content,
                entryType = entry.EntryType.ToString(),
                tags = entry.Tags,
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

            if (!TryGetArg<string>(args, "title", out var title) || string.IsNullOrWhiteSpace(title))
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

            _ = vm.AddNewEntryAsync(entry.Title, entry.EntryType);

            return JsonSerializer.Serialize(new { success = true, entryId = entry.Id.ToString(), message = $"Entry '{title}' created successfully" });
        }

        #endregion

        #region System Handlers

        private string GetAppSummaryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            var activeTasks = vm.Tasks.Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled).Take(5).ToList();
            var upcomingReminders = vm.Reminders.Where(r => !r.IsCompleted).OrderBy(r => r.DueDate).Take(5).ToList();

            return JsonSerializer.Serialize(new
            {
                taskCount = vm.Tasks.Count,
                activeTaskCount = activeTasks.Count,
                completedTasks = vm.Tasks.Count(t => t.IsCompleted),
                reminderCount = vm.Reminders.Count,
                overdueReminders = vm.Reminders.Count(r => r.IsOverdue),
                dataBankCategories = vm.DataBankCategories.Count,
                totalDataEntries = vm.DataBankCategories.Sum(c => c.EntryCount),
                currentDataAssets = vm.CurrentDataAssets.Count,
                taskList = activeTasks.Select(t => new { title = t.Title, priority = t.Priority.ToString() }).ToList(),
                upcomingReminders = upcomingReminders.Select(r => new { title = r.Title, dueDate = r.DueDate.ToString("yyyy-MM-dd HH:mm") }).ToList()
            });
        }

        #endregion

        #region Automation Handlers

        private string ShowNotificationHandler(Dictionary<string, object> args)
        {
            if (!TryGetArg<string>(args, "message", out var message))
            {
                return JsonSerializer.Serialize(new { error = "message is required" });
            }

            var title = TryGetArg<string>(args, "title", out var t) ? t : string.Empty;
            var type = TryGetArg<string>(args, "type", out var ty) ? ty : "info";
            var duration = TryGetArg<int>(args, "duration", out var d) ? d : 5;

            return JsonSerializer.Serialize(new { success = true, message = $"Notification would be shown: {title} - {message}" });
        }

        private string CopyToClipboardHandler(Dictionary<string, object> args)
        {
            if (!TryGetArg<string>(args, "content", out var content))
            {
                return JsonSerializer.Serialize(new { error = "content is required" });
            }

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Clipboard.SetText(content);
                });
                return JsonSerializer.Serialize(new { success = true, message = "Content copied to clipboard" });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private string GetClipboardTextHandler(Dictionary<string, object> args)
        {
            try
            {
                string? text = null;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    text = System.Windows.Clipboard.GetText();
                });
                return JsonSerializer.Serialize(new { success = true, content = text ?? string.Empty });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private string GetClipboardHistoryHandler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();
            var history = vm.ClipboardHistory.AsEnumerable();

            // Filter by type if specified
            if (TryGetArg<string>(args, "type", out var typeFilter) && !string.IsNullOrEmpty(typeFilter) && typeFilter != "all")
            {
                history = typeFilter.ToLowerInvariant() switch
                {
                    "text" => history.Where(h => h.AssetType == DataAssetType.ClipboardText),
                    "image" => history.Where(h => h.AssetType == DataAssetType.ClipboardImage),
                    "files" => history.Where(h => h.AssetType == DataAssetType.ClipboardFiles),
                    _ => history
                };
            }

            // Apply limit
            var limit = TryGetArg<int>(args, "limit", out var l) ? l : 10;
            history = history.Take(limit);

            var result = history.Select(item => new
            {
                id = item.Id.ToString(),
                type = item.AssetType.ToString(),
                name = item.Name,
                capturedAt = item.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                preview = item.AssetType == DataAssetType.ClipboardText 
                    ? (item.TextContent?.Length > 200 ? item.TextContent.Substring(0, 200) + "..." : item.TextContent)
                    : item.AssetType == DataAssetType.ClipboardFiles 
                        ? string.Join(", ", item.FilePaths ?? new List<string>())
                        : item.AssetType == DataAssetType.ClipboardImage && item.FullImage != null
                            ? $"Image ({item.FullImage.PixelWidth}x{item.FullImage.PixelHeight})"
                            : null,
                textContent = item.AssetType == DataAssetType.ClipboardText ? item.TextContent : null,
                filePaths = item.AssetType == DataAssetType.ClipboardFiles ? item.FilePaths : null,
                imageSize = item.AssetType == DataAssetType.ClipboardImage && item.FullImage != null
                    ? new { width = item.FullImage.PixelWidth, height = item.FullImage.PixelHeight }
                    : null
            }).ToList();

            return JsonSerializer.Serialize(new 
            { 
                count = result.Count,
                totalInHistory = vm.ClipboardHistory.Count,
                items = result 
            });
        }

        private string GetClipboardImageBase64Handler(Dictionary<string, object> args)
        {
            var vm = _getViewModel();

            if (!TryGetArg<string>(args, "clipboard_id", out var clipboardIdStr))
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = "clipboard_id parameter is required"
                });
            }

            if (!Guid.TryParse(clipboardIdStr, out var clipboardId))
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Invalid clipboard ID format: {clipboardIdStr}"
                });
            }

            var clipboardItem = vm.ClipboardHistory.FirstOrDefault(a => a.Id == clipboardId);
            if (clipboardItem == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Clipboard item with ID {clipboardIdStr} not found. Use get_clipboard_history to get valid IDs."
                });
            }

            if (clipboardItem.AssetType != DataAssetType.ClipboardImage)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Clipboard item is not an image. Type is: {clipboardItem.AssetType}. Use get_clipboard_history with type='image' to find image items."
                });
            }

            if (clipboardItem.FullImage == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = "Clipboard image data is not available"
                });
            }

            try
            {
                // Convert BitmapSource to PNG bytes, then to base64 (same as screenshots)
                var pngEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                pngEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(clipboardItem.FullImage));

                byte[] pngBytes;
                using (var memoryStream = new MemoryStream())
                {
                    pngEncoder.Save(memoryStream);
                    pngBytes = memoryStream.ToArray();
                }

                var base64String = Convert.ToBase64String(pngBytes);

                // Return a special response format that the orchestration service will detect
                // and convert into a proper multimodal vision message
                return JsonSerializer.Serialize(new 
                { 
                    _imageResponse = true,  // Special marker for orchestration service
                    success = true,
                    id = clipboardItem.Id.ToString(),
                    name = clipboardItem.Name,
                    description = clipboardItem.Description,
                    type = clipboardItem.AssetType.ToString(),
                    source = "clipboard_history",  // Marker to distinguish from screenshots
                    width = clipboardItem.FullImage.PixelWidth,
                    height = clipboardItem.FullImage.PixelHeight,
                    capturedAt = clipboardItem.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    mimeType = "image/png",
                    base64 = base64String
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new 
                { 
                    error = $"Failed to encode clipboard image as base64: {ex.Message}"
                });
            }
        }

        private string SaveToFileHandler(Dictionary<string, object> args)
        {
            if (!TryGetArg<string>(args, "content", out var content))
            {
                return JsonSerializer.Serialize(new { error = "content is required" });
            }

            if (!TryGetArg<string>(args, "filename", out var filename))
            {
                return JsonSerializer.Serialize(new { error = "filename is required" });
            }

            var folder = TryGetArg<string>(args, "folder", out var f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var append = TryGetArg<bool>(args, "append", out var a) && a;

            try
            {
                var filePath = Path.Combine(folder, filename);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                if (append)
                    File.AppendAllText(filePath, content);
                else
                    File.WriteAllText(filePath, content);

                return JsonSerializer.Serialize(new { success = true, filePath = filePath });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private string ReadFileHandler(Dictionary<string, object> args)
        {
            if (!TryGetArg<string>(args, "filepath", out var filepath))
            {
                return JsonSerializer.Serialize(new { error = "filepath is required" });
            }

            try
            {
                var content = File.ReadAllText(filepath);
                return JsonSerializer.Serialize(new { success = true, content = content });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private string ListAutomationsHandler(Dictionary<string, object> args)
        {
            return JsonSerializer.Serialize(new { count = 0, automations = new List<object>(), message = "Automation listing not yet implemented" });
        }

        private string TriggerAutomationHandler(Dictionary<string, object> args)
        {
            return JsonSerializer.Serialize(new { error = "Automation triggering not yet implemented" });
        }

        private string GetContextVariableHandler(Dictionary<string, object> args)
        {
            if (_currentAutomationContext == null)
            {
                return JsonSerializer.Serialize(new { error = "No automation context available" });
            }

            if (!TryGetArg<string>(args, "name", out var name))
            {
                return JsonSerializer.Serialize(new { error = "name is required" });
            }

            var value = _currentAutomationContext.GetVariable<string>(name);
            if (value == null)
            {
                return JsonSerializer.Serialize(new { error = $"Variable '{name}' not found in context" });
            }

            return JsonSerializer.Serialize(new { success = true, name = name, value = value });
        }

        private string SetContextVariableHandler(Dictionary<string, object> args)
        {
            if (_currentAutomationContext == null)
            {
                return JsonSerializer.Serialize(new { error = "No automation context available" });
            }

            if (!TryGetArg<string>(args, "name", out var name))
            {
                return JsonSerializer.Serialize(new { error = "name is required" });
            }

            if (!TryGetArg<string>(args, "value", out var value))
            {
                return JsonSerializer.Serialize(new { error = "value is required" });
            }

            _currentAutomationContext.SetVariable(name, value);
            return JsonSerializer.Serialize(new { success = true, message = $"Variable '{name}' set to '{value}'" });
        }

        #endregion

        #region Utility

        private static bool TryGetArg<T>(Dictionary<string, object> args, string key, out T value)
        {
            value = default!;
            if (!args.TryGetValue(key, out var obj))
                return false;

            try
            {
                // Handle JsonElement (from deserialized JSON)
                if (obj is System.Text.Json.JsonElement jsonElement)
                {
                    if (typeof(T) == typeof(string))
                    {
                        value = (T)(object)jsonElement.GetString()!;
                        return value != null;
                    }
                    else if (typeof(T) == typeof(int))
                    {
                        value = (T)(object)jsonElement.GetInt32();
                        return true;
                    }
                    else if (typeof(T) == typeof(bool))
                    {
                        value = (T)(object)jsonElement.GetBoolean();
                        return true;
                    }
                    else if (typeof(T) == typeof(double))
                    {
                        value = (T)(object)jsonElement.GetDouble();
                        return true;
                    }
                }

                // Try direct cast first
                if (obj is T directValue)
                {
                    value = directValue;
                    return true;
                }

                // Try type conversion
                value = (T)Convert.ChangeType(obj, typeof(T));
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
