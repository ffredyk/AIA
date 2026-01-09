using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Models.Automation;
using TaskStatus = AIA.Models.TaskStatus;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace AIA.Services.Automation
{
    /// <summary>
    /// Service that executes automation actions
    /// </summary>
    public class ActionExecutorService
    {
        private readonly Func<OverlayViewModel> _getViewModel;

        public ActionExecutorService(Func<OverlayViewModel> getViewModel)
        {
            _getViewModel = getViewModel;
        }

        /// <summary>
        /// Executes an automation action
        /// </summary>
        public async Task<ActionResult> ExecuteAsync(AutomationAction action, AutomationContext context)
        {
            if (!action.IsEnabled)
            {
                return ActionResult.Skipped("Action is disabled");
            }

            try
            {
                return action.ActionType switch
                {
                    ActionType.Notification => await ExecuteNotificationAsync(action, context),
                    ActionType.CreateTask => await ExecuteCreateTaskAsync(action, context),
                    ActionType.CreateReminder => await ExecuteCreateReminderAsync(action, context),
                    ActionType.SaveToDataBank => await ExecuteSaveToDataBankAsync(action, context),
                    ActionType.CopyToClipboard => await ExecuteCopyToClipboardAsync(action, context),
                    ActionType.SaveToFile => await ExecuteSaveToFileAsync(action, context),
                    ActionType.RunAutomation => await ExecuteRunAutomationAsync(action, context),
                    ActionType.StoreResult => ExecuteStoreResult(action, context),
                    ActionType.PluginAction => await ExecutePluginActionAsync(action, context),
                    _ => ActionResult.Failed($"Unknown action type: {action.ActionType}")
                };
            }
            catch (Exception ex)
            {
                return ActionResult.Failed(ex.Message);
            }
        }

        private Task<ActionResult> ExecuteNotificationAsync(AutomationAction action, AutomationContext context)
        {
            var title = ResolveVariable(action.GetParameter<string>(NotificationActionParams.Title, "Automation"), context);
            var message = ResolveVariable(action.GetParameter<string>(NotificationActionParams.Message, "Action completed"), context);
            var duration = action.GetParameter<int>(NotificationActionParams.Duration, 5);
            var playSound = action.GetParameter<bool>(NotificationActionParams.PlaySound, false);

            NotificationService.Instance.ShowRichNotification(title, message, NotificationType.Info, duration, playSound);

            return Task.FromResult(ActionResult.Success($"Notification shown: {title}"));
        }

        private async Task<ActionResult> ExecuteCreateTaskAsync(AutomationAction action, AutomationContext context)
        {
            var vm = _getViewModel();

            var title = ResolveVariable(action.GetParameter<string>(CreateTaskActionParams.Title, "New Task"), context);
            var description = ResolveVariable(action.GetParameter<string>(CreateTaskActionParams.Description, ""), context);
            var priorityStr = action.GetParameter<string>(CreateTaskActionParams.Priority, "Medium");
            var dueDateOffset = action.GetParameter<int>(CreateTaskActionParams.DueDateOffset, 0);

            if (!Enum.TryParse<TaskPriority>(priorityStr, out var priority))
            {
                priority = TaskPriority.Medium;
            }

            var task = new TaskItem
            {
                Title = title,
                Description = description,
                Priority = priority,
                Status = TaskStatus.NotStarted
            };

            if (dueDateOffset > 0)
            {
                task.DueDate = DateTime.Now.AddDays(dueDateOffset);
            }

            await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                vm.Tasks.Add(task);
                vm.ApplyTaskFilter();
            });
            await vm.SaveTasksAndRemindersAsync();

            // Store task ID in context for chaining
            context.SetVariable("created_task_id", task.Id.ToString());

            return ActionResult.Success($"Task created: {title}");
        }

        private async Task<ActionResult> ExecuteCreateReminderAsync(AutomationAction action, AutomationContext context)
        {
            var vm = _getViewModel();

            var title = ResolveVariable(action.GetParameter<string>(CreateReminderActionParams.Title, "New Reminder"), context);
            var dueDateOffset = action.GetParameter<int>(CreateReminderActionParams.DueDateOffset, 60); // Default 60 minutes
            var severityStr = action.GetParameter<string>(CreateReminderActionParams.Severity, "Medium");

            if (!Enum.TryParse<ReminderSeverity>(severityStr, out var severity))
            {
                severity = ReminderSeverity.Medium;
            }

            var reminder = new ReminderItem
            {
                Title = title,
                DueDate = DateTime.Now.AddMinutes(dueDateOffset),
                Severity = severity
            };

            await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                vm.Reminders.Add(reminder);
            });
            await vm.SaveTasksAndRemindersAsync();

            // Store reminder ID in context for chaining
            context.SetVariable("created_reminder_id", reminder.Id.ToString());

            return ActionResult.Success($"Reminder created: {title}");
        }

        private async Task<ActionResult> ExecuteSaveToDataBankAsync(AutomationAction action, AutomationContext context)
        {
            var vm = _getViewModel();

            var categoryIdStr = action.GetParameter<string>(SaveToDataBankActionParams.CategoryId, "");
            var title = ResolveVariable(action.GetParameter<string>(SaveToDataBankActionParams.Title, "Automation Result"), context);
            var tags = ResolveVariable(action.GetParameter<string>(SaveToDataBankActionParams.Tags, ""), context);

            // Get content from context (agent result)
            var content = context.GetVariable<string>("agent_result") ?? "";

            DataBankCategory? category = null;
            if (Guid.TryParse(categoryIdStr, out var categoryId))
            {
                category = await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var cat in vm.DataBankCategories)
                    {
                        if (cat.Id == categoryId)
                            return cat;
                    }
                    return null;
                });
            }

            // Use first category if not specified
            category ??= await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                return vm.DataBankCategories.Count > 0 ? vm.DataBankCategories[0] : null;
            });

            if (category == null)
            {
                return ActionResult.Failed("No data bank category available");
            }

            var entry = new DataBankEntry
            {
                Title = title,
                Content = content,
                Tags = tags,
                EntryType = DataEntryType.Text,
                CategoryId = category.Id
            };

            await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
            {
                vm.CurrentCategoryEntries.Add(entry);
                category.EntryCount++;
            });
            await vm.SaveDataBanksAsync();

            context.SetVariable("created_entry_id", entry.Id.ToString());

            return ActionResult.Success($"Saved to data bank: {title}");
        }

        private Task<ActionResult> ExecuteCopyToClipboardAsync(AutomationAction action, AutomationContext context)
        {
            // Get content from context (agent result or specified content)
            var content = context.GetVariable<string>("agent_result") ?? "";

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                WpfClipboard.SetText(content);
            });

            return Task.FromResult(ActionResult.Success("Content copied to clipboard"));
        }

        private async Task<ActionResult> ExecuteSaveToFileAsync(AutomationAction action, AutomationContext context)
        {
            var filePath = ResolveVariable(action.GetParameter<string>(SaveToFileActionParams.FilePath, ""), context);
            var fileName = ResolveVariable(action.GetParameter<string>(SaveToFileActionParams.FileName, "automation_result.txt"), context);
            var appendMode = action.GetParameter<bool>(SaveToFileActionParams.AppendMode, false);

            // Get content from context
            var content = context.GetVariable<string>("agent_result") ?? "";

            // Build full path
            var fullPath = string.IsNullOrEmpty(filePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName)
                : Path.Combine(filePath, fileName);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (appendMode && File.Exists(fullPath))
            {
                await File.AppendAllTextAsync(fullPath, Environment.NewLine + content);
            }
            else
            {
                await File.WriteAllTextAsync(fullPath, content);
            }

            context.SetVariable("saved_file_path", fullPath);

            return ActionResult.Success($"Saved to file: {fullPath}");
        }

        private async Task<ActionResult> ExecuteRunAutomationAsync(AutomationAction action, AutomationContext context)
        {
            var automationIdStr = action.GetParameter<string>(RunAutomationActionParams.AutomationId, "");
            var passContext = action.GetParameter<bool>(RunAutomationActionParams.PassContext, true);

            if (!Guid.TryParse(automationIdStr, out var automationId))
            {
                return ActionResult.Failed("Invalid automation ID");
            }

            var vm = _getViewModel();
            var targetAutomation = vm.AutomationService.Automations
                .FirstOrDefault(a => a.Id == automationId);

            if (targetAutomation == null)
            {
                return ActionResult.Failed($"Automation not found: {automationId}");
            }

            // Create new context, optionally passing variables
            var newContext = new AutomationContext
            {
                Trigger = new AutomationChainTrigger
                {
                    Name = "Chained from automation",
                    SourceAutomationId = context.Trigger is ManualTrigger mt ? Guid.Empty : 
                        (context.TriggerData as AutomationChainTriggerData)?.SourceAutomationId ?? Guid.Empty
                }
            };

            if (passContext)
            {
                foreach (var variable in context.Variables)
                {
                    newContext.SetVariable(variable.Key, variable.Value);
                }
            }

            // Trigger the automation (fire and forget for now)
            _ = vm.AutomationService.TriggerManuallyAsync(targetAutomation, newContext);

            return await Task.FromResult(ActionResult.Success($"Triggered automation: {targetAutomation.Name}"));
        }

        private ActionResult ExecuteStoreResult(AutomationAction action, AutomationContext context)
        {
            // Result is already stored in context as "agent_result"
            // This action type is mainly for documentation/clarity in the action chain
            return ActionResult.Success("Result stored in context");
        }

        private async Task<ActionResult> ExecutePluginActionAsync(AutomationAction action, AutomationContext context)
        {
            var pluginId = action.GetParameter<string>(PluginActionParams.PluginId, "");
            var actionId = action.GetParameter<string>(PluginActionParams.ActionId, "");

            // Plugin action execution would be implemented through the plugin system
            // For now, return a placeholder
            await Task.CompletedTask;

            return ActionResult.Failed("Plugin actions not yet implemented");
        }

        /// <summary>
        /// Resolves variables in a string template using the automation context
        /// </summary>
        private static string ResolveVariable(string? template, AutomationContext context)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            var result = template;

            // Replace {{variable_name}} patterns
            foreach (var variable in context.Variables)
            {
                var pattern = $"{{{{{variable.Key}}}}}";
                var value = variable.Value?.ToString() ?? "";
                result = result.Replace(pattern, value);
            }

            // Also support simpler {variable_name} patterns
            foreach (var variable in context.Variables)
            {
                var pattern = $"{{{variable.Key}}}";
                var value = variable.Value?.ToString() ?? "";
                result = result.Replace(pattern, value);
            }

            return result;
        }
    }

    /// <summary>
    /// Result of an action execution
    /// </summary>
    public class ActionResult
    {
        public bool IsSuccess { get; set; }
        public bool IsSkipped { get; set; }
        public string Message { get; set; } = string.Empty;

        public static ActionResult Success(string message = "")
        {
            return new ActionResult { IsSuccess = true, Message = message };
        }

        public static ActionResult Failed(string message)
        {
            return new ActionResult { IsSuccess = false, Message = message };
        }

        public static ActionResult Skipped(string message)
        {
            return new ActionResult { IsSuccess = true, IsSkipped = true, Message = message };
        }
    }
}
