using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Models.Automation;

namespace AIA.Services.Automation
{
    /// <summary>
    /// Main service for managing automation tasks
    /// </summary>
    public class AutomationService : IDisposable
    {
        private static readonly string AutomationsFolder;
        private static readonly string SettingsFile;
        private static readonly string HistoryFolder;

        private readonly TriggerMonitorService _triggerMonitor;
        private readonly AgentExecutionService _agentExecutor;
        private readonly ActionExecutorService _actionExecutor;
        private readonly object _executionLock = new();
        private readonly SemaphoreSlim _concurrencySemaphore;
        private bool _isDisposed;

        public ObservableCollection<AutomationTask> Automations { get; } = new();
        public ObservableCollection<AutomationExecution> RunningExecutions { get; } = new();
        public ObservableCollection<AutomationExecution> ExecutionHistory { get; } = new();
        public AutomationSettings Settings { get; private set; } = new();

        /// <summary>
        /// Event fired when an automation starts executing
        /// </summary>
        public event EventHandler<AutomationExecution>? ExecutionStarted;

        /// <summary>
        /// Event fired when an automation completes
        /// </summary>
        public event EventHandler<AutomationExecution>? ExecutionCompleted;

        /// <summary>
        /// Event fired when an automation requires confirmation
        /// </summary>
        public event EventHandler<ConfirmationRequestEventArgs>? ConfirmationRequired;

        static AutomationService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            AutomationsFolder = Path.Combine(exeDirectory, "automations");
            SettingsFile = Path.Combine(AutomationsFolder, "settings.json");
            HistoryFolder = Path.Combine(AutomationsFolder, "history");
        }

        public AutomationService(Func<OverlayViewModel> getViewModel)
        {
            _triggerMonitor = new TriggerMonitorService(this);
            _agentExecutor = new AgentExecutionService(getViewModel);
            _actionExecutor = new ActionExecutorService(getViewModel);
            _concurrencySemaphore = new SemaphoreSlim(Settings.MaxConcurrentAutomations);

            _triggerMonitor.TriggerFired += OnTriggerFired;
        }

        #region Initialization

        /// <summary>
        /// Initializes the automation service
        /// </summary>
        public async Task InitializeAsync()
        {
            EnsureDirectoriesExist();
            await LoadSettingsAsync();
            await LoadAutomationsAsync();
            await LoadHistoryAsync();

            if (Settings.IsEnabled)
            {
                Start();
            }
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(AutomationsFolder))
                Directory.CreateDirectory(AutomationsFolder);
            if (!Directory.Exists(HistoryFolder))
                Directory.CreateDirectory(HistoryFolder);
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts the automation service
        /// </summary>
        public void Start()
        {
            if (!Settings.IsEnabled) return;

            _triggerMonitor.Start();

            // Register triggers for all active automations
            foreach (var automation in Automations.Where(a => a.Status == AutomationStatus.Active))
            {
                RegisterTrigger(automation);
            }
        }

        /// <summary>
        /// Stops the automation service
        /// </summary>
        public void Stop()
        {
            _triggerMonitor.Stop();
        }

        /// <summary>
        /// Pauses trigger monitoring (e.g., when overlay is visible)
        /// </summary>
        public void PauseTriggers()
        {
            if (Settings.PauseOnOverlayVisible)
            {
                _triggerMonitor.Pause();
            }
        }

        /// <summary>
        /// Resumes trigger monitoring
        /// </summary>
        public void ResumeTriggers()
        {
            _triggerMonitor.Resume();
        }

        #endregion

        #region Automation CRUD

        /// <summary>
        /// Creates a new automation
        /// </summary>
        public AutomationTask CreateAutomation(string name, string description = "")
        {
            var automation = new AutomationTask
            {
                Name = name,
                Description = description,
                Agent = new AutomationAgent
                {
                    MaxIterations = Settings.DefaultMaxIterations,
                    MaxTotalTokens = Settings.DefaultMaxTotalTokens
                }
            };

            Automations.Add(automation);
            _ = SaveAutomationAsync(automation);

            return automation;
        }

        /// <summary>
        /// Updates an automation
        /// </summary>
        public async Task UpdateAutomationAsync(AutomationTask automation)
        {
            automation.ModifiedDate = DateTime.Now;
            await SaveAutomationAsync(automation);

            // Re-register trigger if active
            if (automation.Status == AutomationStatus.Active)
            {
                UnregisterTrigger(automation);
                RegisterTrigger(automation);
            }
        }

        /// <summary>
        /// Deletes an automation
        /// </summary>
        public async Task DeleteAutomationAsync(AutomationTask automation)
        {
            // Stop if running
            if (automation.Status == AutomationStatus.Running)
            {
                await CancelExecutionAsync(automation);
            }

            UnregisterTrigger(automation);
            Automations.Remove(automation);

            // Delete file
            var filePath = GetAutomationFilePath(automation.Id);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Enables an automation
        /// </summary>
        public void EnableAutomation(AutomationTask automation)
        {
            automation.Enable();
            RegisterTrigger(automation);
            _ = SaveAutomationAsync(automation);
        }

        /// <summary>
        /// Disables an automation
        /// </summary>
        public void DisableAutomation(AutomationTask automation)
        {
            UnregisterTrigger(automation);
            automation.Disable();
            _ = SaveAutomationAsync(automation);
        }

        /// <summary>
        /// Duplicates an automation
        /// </summary>
        public AutomationTask DuplicateAutomation(AutomationTask source)
        {
            var json = JsonSerializer.Serialize(source, GetJsonOptions());
            var duplicate = JsonSerializer.Deserialize<AutomationTask>(json, GetJsonOptions())!;

            duplicate.Id = Guid.NewGuid();
            duplicate.Name = $"{source.Name} (Copy)";
            duplicate.Status = AutomationStatus.Disabled;
            duplicate.CreatedDate = DateTime.Now;
            duplicate.ModifiedDate = DateTime.Now;
            duplicate.TotalExecutions = 0;
            duplicate.SuccessfulExecutions = 0;
            duplicate.FailedExecutions = 0;
            duplicate.LastExecutionDate = null;

            Automations.Add(duplicate);
            _ = SaveAutomationAsync(duplicate);

            return duplicate;
        }

        #endregion

        #region Execution

        /// <summary>
        /// Manually triggers an automation
        /// </summary>
        public async Task<AutomationExecution?> TriggerManuallyAsync(AutomationTask automation, AutomationContext? context = null)
        {
            context ??= new AutomationContext
            {
                Trigger = new ManualTrigger { Name = "Manual Trigger" }
            };

            return await ExecuteAutomationAsync(automation, context);
        }

        /// <summary>
        /// Executes an automation
        /// </summary>
        public async Task<AutomationExecution?> ExecuteAutomationAsync(AutomationTask automation, AutomationContext context)
        {
            // Check if we can execute
            if (!CanExecute(automation))
            {
                return null;
            }

            // Wait for concurrency slot
            if (!await _concurrencySemaphore.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                return null;
            }

            AutomationExecution? execution = null;
            try
            {
                execution = new AutomationExecution
                {
                    AutomationId = automation.Id,
                    AutomationName = automation.Name,
                    TriggerDescription = context.Trigger?.Name ?? "Unknown",
                    ContextSnapshot = new Dictionary<string, object?>(context.Variables)
                };

                lock (_executionLock)
                {
                    RunningExecutions.Add(execution);
                }

                automation.Status = AutomationStatus.Running;
                ExecutionStarted?.Invoke(this, execution);

                execution.AddTrace(TraceLevel.Info, "Automation started", $"Trigger: {context.Trigger?.Name}");

                // Execute the agent
                var result = await _agentExecutor.ExecuteAsync(automation, context, execution);

                // Execute actions if agent succeeded
                if (result.IsSuccess && automation.Actions.Any(a => a.IsEnabled))
                {
                    await ExecuteActionsAsync(automation, context, execution, result.Result);
                }

                // Complete execution
                if (result.IsSuccess)
                {
                    execution.Complete(result.Result ?? "Completed");
                    automation.RecordSuccess(result.Result ?? "Completed");
                }
                else
                {
                    execution.Fail(result.Error ?? "Unknown error");
                    automation.RecordFailure(result.Error ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                if (execution == null)
                {
                    execution = new AutomationExecution
                    {
                        AutomationId = automation.Id,
                        AutomationName = automation.Name
                    };
                }
                execution.Fail(ex.Message);
                automation.RecordFailure(ex.Message);
            }
            finally
            {
                if (execution != null)
                {
                    lock (_executionLock)
                    {
                        RunningExecutions.Remove(execution);
                    }
                }

                if (!automation.IsOneTime)
                {
                    automation.Status = AutomationStatus.Active;
                }

                _concurrencySemaphore.Release();
            }

            // Add to history (execution is guaranteed to be non-null here)
            if (execution != null)
            {
                AddToHistory(execution);
                ExecutionCompleted?.Invoke(this, execution);
            }

            await SaveAutomationAsync(automation);

            return execution;
        }

        /// <summary>
        /// Cancels a running execution
        /// </summary>
        public async Task CancelExecutionAsync(AutomationTask automation)
        {
            var execution = RunningExecutions.FirstOrDefault(e => e.AutomationId == automation.Id);
            if (execution != null)
            {
                execution.Cancel();
                automation.Status = automation.IsOneTime ? AutomationStatus.Cancelled : AutomationStatus.Active;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Pauses a one-time automation
        /// </summary>
        public void PauseAutomation(AutomationTask automation)
        {
            if (automation.IsOneTime && automation.Status == AutomationStatus.Running)
            {
                automation.Pause();
            }
        }

        /// <summary>
        /// Resumes a paused automation
        /// </summary>
        public void ResumeAutomation(AutomationTask automation)
        {
            if (automation.Status == AutomationStatus.Paused)
            {
                automation.Resume();
            }
        }

        private bool CanExecute(AutomationTask automation)
        {
            // Check global enable
            if (!Settings.IsEnabled) return false;

            // Check automation status
            if (automation.Status != AutomationStatus.Active && automation.Status != AutomationStatus.Paused)
                return false;

            // Check concurrent execution limit
            var runningCount = RunningExecutions.Count(e => e.AutomationId == automation.Id);
            if (runningCount >= automation.Limits.MaxConcurrentExecutions)
                return false;

            // Check rate limits (simplified)
            // TODO: Implement proper rate limiting with time windows

            return true;
        }

        private async Task ExecuteActionsAsync(AutomationTask automation, AutomationContext context, 
            AutomationExecution execution, string? agentResult)
        {
            context.SetVariable("agent_result", agentResult);

            foreach (var action in automation.Actions.Where(a => a.IsEnabled))
            {
                // Check confirmation
                if (action.RequireConfirmation)
                {
                    var args = new ConfirmationRequestEventArgs(automation, action, context);
                    ConfirmationRequired?.Invoke(this, args);

                    if (!await args.WaitForConfirmationAsync())
                    {
                        execution.AddTrace(TraceLevel.Warning, $"Action '{action.Name}' skipped - user declined");
                        continue;
                    }
                }

                // Check permissions
                if (!CheckActionPermissions(automation, action))
                {
                    execution.AddTrace(TraceLevel.Warning, $"Action '{action.Name}' skipped - insufficient permissions");
                    continue;
                }

                try
                {
                    await ExecuteActionAsync(action, context, execution);
                    execution.AddTrace(TraceLevel.Info, $"Action '{action.Name}' completed");
                }
                catch (Exception ex)
                {
                    execution.AddTrace(TraceLevel.Error, $"Action '{action.Name}' failed", ex.Message);
                }
            }
        }

        private async Task ExecuteActionAsync(AutomationAction action, AutomationContext context, AutomationExecution execution)
        {
            var result = await _actionExecutor.ExecuteAsync(action, context);
            
            if (!result.IsSuccess && !result.IsSkipped)
            {
                execution.AddTrace(TraceLevel.Error, $"Action '{action.Name}' failed", result.Message);
            }
            else if (result.IsSkipped)
            {
                execution.AddTrace(TraceLevel.Info, $"Action '{action.Name}' skipped", result.Message);
            }
        }

        private bool CheckActionPermissions(AutomationTask automation, AutomationAction action)
        {
            return action.ActionType switch
            {
                ActionType.CreateTask => automation.Permissions.CanCreateTasks,
                ActionType.CreateReminder => automation.Permissions.CanCreateReminders,
                ActionType.SaveToDataBank => automation.Permissions.CanSaveToDataBank,
                ActionType.Notification => automation.Permissions.CanShowNotifications,
                ActionType.RunAutomation => automation.Permissions.CanRunSubAutomations,
                ActionType.PluginAction => automation.Permissions.CanUsePluginActions,
                ActionType.CopyToClipboard => automation.Permissions.CanAccessClipboard,
                ActionType.SaveToFile => automation.Permissions.CanAccessFileSystem,
                _ => true
            };
        }

        #endregion

        #region Triggers

        private void RegisterTrigger(AutomationTask automation)
        {
            if (automation.Trigger == null) return;
            _triggerMonitor.RegisterTrigger(automation.Id, automation.Trigger);
        }

        private void UnregisterTrigger(AutomationTask automation)
        {
            _triggerMonitor.UnregisterTrigger(automation.Id);
        }

        private async void OnTriggerFired(object? sender, TriggerFiredEventArgs e)
        {
            var automation = Automations.FirstOrDefault(a => a.Id == e.AutomationId);
            if (automation == null) return;

            var context = new AutomationContext
            {
                Trigger = e.Trigger,
                TriggerData = e.TriggerData
            };

            // Add trigger data to variables
            if (e.TriggerData != null)
            {
                PopulateContextFromTriggerData(context, e.TriggerData);
            }

            await ExecuteAutomationAsync(automation, context);
        }

        private void PopulateContextFromTriggerData(AutomationContext context, TriggerData data)
        {
            context.SetVariable("trigger_time", data.TriggeredAt);

            switch (data)
            {
                case ClipboardTriggerData clipboard:
                    context.SetVariable("clipboard_text", clipboard.TextContent);
                    context.SetVariable("clipboard_type", clipboard.ContentType.ToString());
                    break;

                case FileChangeTriggerData file:
                    context.SetVariable("file_path", file.FilePath);
                    context.SetVariable("file_name", file.FileName);
                    context.SetVariable("file_extension", file.Extension);
                    context.SetVariable("file_change_type", file.ChangeType.ToString());
                    break;

                case WindowContextTriggerData window:
                    context.SetVariable("window_title", window.WindowTitle);
                    context.SetVariable("process_name", window.ProcessName);
                    break;

                case PluginTriggerData plugin:
                    foreach (var kv in plugin.Data)
                    {
                        context.SetVariable(kv.Key, kv.Value);
                    }
                    break;

                case AutomationChainTriggerData chain:
                    context.SetVariable("source_automation_id", chain.SourceAutomationId);
                    context.SetVariable("source_result", chain.SourceResult);
                    context.SetVariable("source_success", chain.SourceSuccess);
                    break;
            }
        }

        #endregion

        #region History

        private void AddToHistory(AutomationExecution execution)
        {
            ExecutionHistory.Insert(0, execution);

            // Trim history
            while (ExecutionHistory.Count > Settings.MaxHistoryEntries)
            {
                ExecutionHistory.RemoveAt(ExecutionHistory.Count - 1);
            }

            // Save to disk
            _ = SaveExecutionHistoryAsync(execution);
        }

        /// <summary>
        /// Clears execution history
        /// </summary>
        public async Task ClearHistoryAsync()
        {
            ExecutionHistory.Clear();

            // Delete history files
            if (Directory.Exists(HistoryFolder))
            {
                foreach (var file in Directory.GetFiles(HistoryFolder, "*.json"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Import/Export

        /// <summary>
        /// Exports an automation to a file
        /// </summary>
        public async Task<bool> ExportAutomationAsync(AutomationTask automation, string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(automation, GetJsonOptions());
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Imports an automation from a file
        /// </summary>
        public async Task<AutomationTask?> ImportAutomationAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var automation = JsonSerializer.Deserialize<AutomationTask>(json, GetJsonOptions());

                if (automation != null)
                {
                    // Generate new ID to avoid conflicts
                    automation.Id = Guid.NewGuid();
                    automation.Status = AutomationStatus.Disabled;
                    automation.CreatedDate = DateTime.Now;
                    automation.ModifiedDate = DateTime.Now;
                    automation.TotalExecutions = 0;
                    automation.SuccessfulExecutions = 0;
                    automation.FailedExecutions = 0;

                    Automations.Add(automation);
                    await SaveAutomationAsync(automation);
                }

                return automation;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Persistence

        private async Task LoadSettingsAsync()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(SettingsFile);
                    Settings = JsonSerializer.Deserialize<AutomationSettings>(json) ?? new AutomationSettings();
                }
                catch
                {
                    Settings = new AutomationSettings();
                }
            }

            // Update semaphore if needed
            // Note: SemaphoreSlim doesn't support changing max count after creation
        }

        public async Task SaveSettingsAsync()
        {
            EnsureDirectoriesExist();
            var json = JsonSerializer.Serialize(Settings, GetJsonOptions());
            await File.WriteAllTextAsync(SettingsFile, json);
        }

        private async Task LoadAutomationsAsync()
        {
            if (!Directory.Exists(AutomationsFolder)) return;

            var files = Directory.GetFiles(AutomationsFolder, "automation_*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var automation = JsonSerializer.Deserialize<AutomationTask>(json, GetJsonOptions());
                    if (automation != null)
                    {
                        // Reset running status on load
                        if (automation.Status == AutomationStatus.Running)
                        {
                            automation.Status = automation.IsOneTime ? AutomationStatus.Paused : AutomationStatus.Active;
                        }
                        Automations.Add(automation);
                    }
                }
                catch { }
            }
        }

        private async Task SaveAutomationAsync(AutomationTask automation)
        {
            EnsureDirectoriesExist();
            var filePath = GetAutomationFilePath(automation.Id);
            var json = JsonSerializer.Serialize(automation, GetJsonOptions());
            await File.WriteAllTextAsync(filePath, json);
        }

        private async Task LoadHistoryAsync()
        {
            if (!Directory.Exists(HistoryFolder)) return;

            var cutoffDate = DateTime.Now.AddDays(-Settings.HistoryRetentionDays);
            var files = Directory.GetFiles(HistoryFolder, "*.json")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Take(Settings.MaxHistoryEntries);

            foreach (var file in files)
            {
                try
                {
                    var fileTime = File.GetCreationTime(file);
                    if (fileTime < cutoffDate)
                    {
                        File.Delete(file);
                        continue;
                    }

                    var json = await File.ReadAllTextAsync(file);
                    var execution = JsonSerializer.Deserialize<AutomationExecution>(json, GetJsonOptions());
                    if (execution != null)
                    {
                        ExecutionHistory.Add(execution);
                    }
                }
                catch { }
            }
        }

        private async Task SaveExecutionHistoryAsync(AutomationExecution execution)
        {
            EnsureDirectoriesExist();
            var fileName = $"{execution.StartedAt:yyyyMMdd_HHmmss}_{execution.Id:N}.json";
            var filePath = Path.Combine(HistoryFolder, fileName);
            var json = JsonSerializer.Serialize(execution, GetJsonOptions());
            await File.WriteAllTextAsync(filePath, json);
        }

        private string GetAutomationFilePath(Guid automationId)
        {
            return Path.Combine(AutomationsFolder, $"automation_{automationId:N}.json");
        }

        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter(), new TriggerJsonConverter() }
            };
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _triggerMonitor.Dispose();
            _concurrencySemaphore.Dispose();
        }
    }

    /// <summary>
    /// Event args for trigger firing
    /// </summary>
    public class TriggerFiredEventArgs : EventArgs
    {
        public Guid AutomationId { get; set; }
        public AutomationTrigger Trigger { get; set; } = null!;
        public TriggerData? TriggerData { get; set; }
    }

    /// <summary>
    /// Event args for confirmation requests
    /// </summary>
    public class ConfirmationRequestEventArgs : EventArgs
    {
        private readonly TaskCompletionSource<bool> _tcs = new();

        public AutomationTask Automation { get; }
        public AutomationAction Action { get; }
        public AutomationContext Context { get; }
        public string Message { get; set; } = string.Empty;

        public ConfirmationRequestEventArgs(AutomationTask automation, AutomationAction action, AutomationContext context)
        {
            Automation = automation;
            Action = action;
            Context = context;
            Message = $"Automation '{automation.Name}' wants to execute action '{action.Name}'. Allow?";
        }

        public void Confirm()
        {
            _tcs.TrySetResult(true);
        }

        public void Deny()
        {
            _tcs.TrySetResult(false);
        }

        public Task<bool> WaitForConfirmationAsync()
        {
            return _tcs.Task;
        }
    }

    /// <summary>
    /// JSON converter for polymorphic trigger deserialization
    /// </summary>
    public class TriggerJsonConverter : JsonConverter<AutomationTrigger>
    {
        public override AutomationTrigger? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("TriggerType", out var typeElement))
            {
                // Try to infer from properties
                if (root.TryGetProperty("ContentType", out _))
                    return JsonSerializer.Deserialize<ClipboardTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("Key", out _))
                    return JsonSerializer.Deserialize<HotkeyTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("WatchPath", out _))
                    return JsonSerializer.Deserialize<FileChangeTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("WindowTitleFilter", out _))
                    return JsonSerializer.Deserialize<WindowContextTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("PluginId", out _))
                    return JsonSerializer.Deserialize<PluginTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("SourceAutomationId", out _))
                    return JsonSerializer.Deserialize<AutomationChainTrigger>(root.GetRawText(), options);
                if (root.TryGetProperty("ScheduledTime", out _))
                    return JsonSerializer.Deserialize<ScheduleTrigger>(root.GetRawText(), options);
                
                return JsonSerializer.Deserialize<ManualTrigger>(root.GetRawText(), options);
            }

            var triggerType = typeElement.GetString();
            return triggerType switch
            {
                "Clipboard" => JsonSerializer.Deserialize<ClipboardTrigger>(root.GetRawText(), options),
                "Hotkey" => JsonSerializer.Deserialize<HotkeyTrigger>(root.GetRawText(), options),
                "FileChange" => JsonSerializer.Deserialize<FileChangeTrigger>(root.GetRawText(), options),
                "WindowContext" => JsonSerializer.Deserialize<WindowContextTrigger>(root.GetRawText(), options),
                "Plugin" => JsonSerializer.Deserialize<PluginTrigger>(root.GetRawText(), options),
                "AutomationChain" => JsonSerializer.Deserialize<AutomationChainTrigger>(root.GetRawText(), options),
                "Schedule" => JsonSerializer.Deserialize<ScheduleTrigger>(root.GetRawText(), options),
                _ => JsonSerializer.Deserialize<ManualTrigger>(root.GetRawText(), options)
            };
        }

        public override void Write(Utf8JsonWriter writer, AutomationTrigger value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
