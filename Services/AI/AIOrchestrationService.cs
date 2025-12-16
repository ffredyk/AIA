using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Models.AI;
using TaskStatus = AIA.Models.TaskStatus;

namespace AIA.Services.AI
{
    /// <summary>
    /// Main AI Orchestration service that manages providers, routing, and generation
    /// </summary>
    public class AIOrchestrationService
    {
        private static readonly string ConfigFolder;
        private static readonly string ProvidersFile;
        private static readonly string SettingsFile;

        private readonly Dictionary<AIProviderType, IAIProviderClient> _clients;
        private readonly AIToolsService _toolsService;
        private readonly Func<OverlayViewModel> _getViewModel;

        public ObservableCollection<AIProvider> Providers { get; } = new();
        public AIOrchestrationSettings Settings { get; private set; } = new();

        static AIOrchestrationService()
        {
            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            ConfigFolder = Path.Combine(exeDirectory, "ai-config");
            ProvidersFile = Path.Combine(ConfigFolder, "providers.json");
            SettingsFile = Path.Combine(ConfigFolder, "settings.json");
        }

        public AIOrchestrationService(Func<OverlayViewModel> getViewModel)
        {
            _getViewModel = getViewModel;
            _toolsService = new AIToolsService(getViewModel);

            // Register all provider clients
            _clients = new Dictionary<AIProviderType, IAIProviderClient>
            {
                [AIProviderType.OpenAI] = new OpenAIClient(),
                [AIProviderType.AzureOpenAI] = new AzureOpenAIClient(),
                [AIProviderType.Google] = new GoogleGeminiClient(),
                [AIProviderType.Anthropic] = new AnthropicClient()
            };

            // Load configuration
            _ = LoadConfigurationAsync();
        }

        #region Configuration Management

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(ConfigFolder))
                Directory.CreateDirectory(ConfigFolder);
        }

        public async Task LoadConfigurationAsync()
        {
            EnsureDirectoryExists();

            // Load providers
            if (File.Exists(ProvidersFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(ProvidersFile);
                    var providers = JsonSerializer.Deserialize<List<AIProvider>>(json);
                    if (providers != null)
                    {
                        Providers.Clear();
                        foreach (var provider in providers)
                        {
                            Providers.Add(provider);
                        }
                    }
                }
                catch { /* Use empty list on error */ }
            }

            // Load settings
            if (File.Exists(SettingsFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AIOrchestrationSettings>(json);
                    if (settings != null)
                    {
                        Settings = settings;
                    }
                }
                catch { /* Use default settings on error */ }
            }
        }

        public async Task SaveConfigurationAsync()
        {
            EnsureDirectoryExists();

            var options = new JsonSerializerOptions { WriteIndented = true };

            // Save providers
            var providersJson = JsonSerializer.Serialize(Providers.ToList(), options);
            await File.WriteAllTextAsync(ProvidersFile, providersJson);

            // Save settings
            var settingsJson = JsonSerializer.Serialize(Settings, options);
            await File.WriteAllTextAsync(SettingsFile, settingsJson);
        }

        public void AddProvider(AIProvider provider)
        {
            Providers.Add(provider);
            _ = SaveConfigurationAsync();
        }

        public void RemoveProvider(AIProvider provider)
        {
            Providers.Remove(provider);
            _ = SaveConfigurationAsync();
        }

        public void SetDefaultProvider(AIProvider provider)
        {
            foreach (var p in Providers)
            {
                p.IsDefault = p.Id == provider.Id;
            }
            _ = SaveConfigurationAsync();
        }

        #endregion

        #region Provider Selection and Routing

        /// <summary>
        /// Get the best provider for a given prompt
        /// </summary>
        public AIProvider? GetBestProvider(string prompt)
        {
            var enabledProviders = Providers.Where(p => p.IsEnabled).ToList();
            if (enabledProviders.Count == 0)
                return null;

            if (!Settings.EnableAutoRouting)
            {
                // Return default or first enabled provider
                return enabledProviders.FirstOrDefault(p => p.IsDefault) ?? enabledProviders.First();
            }

            // Analyze prompt to determine category
            var category = AnalyzePromptCategory(prompt);

            // Find providers that excel in this category
            var matchingProviders = enabledProviders
                .Where(p => p.Strengths.Contains(category, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Priority)
                .ToList();

            if (matchingProviders.Count > 0)
                return matchingProviders.First();

            // Fallback: return highest priority enabled provider
            return enabledProviders.OrderByDescending(p => p.Priority).ThenBy(p => p.IsDefault ? 0 : 1).First();
        }

        /// <summary>
        /// Simple category analysis based on keywords
        /// </summary>
        private static string AnalyzePromptCategory(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();

            // Coding keywords
            if (ContainsAny(lowerPrompt, "code", "programming", "function", "class", "debug", "error", "compile", "syntax", 
                "algorithm", "api", "database", "sql", "javascript", "python", "c#", "java", "typescript", "react", "implement"))
            {
                return AIRouterCategories.Coding;
            }

            // Math keywords
            if (ContainsAny(lowerPrompt, "calculate", "equation", "math", "formula", "solve", "derivative", "integral", 
                "probability", "statistics", "algebra", "geometry", "trigonometry", "calculus"))
            {
                return AIRouterCategories.Math;
            }

            // Analysis keywords
            if (ContainsAny(lowerPrompt, "analyze", "analysis", "compare", "evaluate", "assess", "review", "examine", 
                "investigate", "study", "research", "data", "trend", "pattern", "insight"))
            {
                return AIRouterCategories.Analysis;
            }

            // Creative keywords
            if (ContainsAny(lowerPrompt, "write", "story", "poem", "creative", "imagine", "design", "brainstorm", 
                "idea", "novel", "fiction", "art", "music", "compose", "create"))
            {
                return AIRouterCategories.Creative;
            }

            // Task management keywords
            if (ContainsAny(lowerPrompt, "task", "todo", "reminder", "schedule", "deadline", "organize", "plan", 
                "priority", "project", "workflow", "productivity"))
            {
                return AIRouterCategories.TaskManagement;
            }

            // Summarization keywords
            if (ContainsAny(lowerPrompt, "summarize", "summary", "tldr", "brief", "overview", "key points", "main ideas", 
                "condense", "shorten", "abstract"))
            {
                return AIRouterCategories.Summarization;
            }

            // Research keywords
            if (ContainsAny(lowerPrompt, "research", "find", "search", "look up", "what is", "explain", "how does", 
                "why", "history", "background", "learn", "understand"))
            {
                return AIRouterCategories.Research;
            }

            // Default to conversation
            return AIRouterCategories.Conversation;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k));
        }

        #endregion

        #region AI Generation

        /// <summary>
        /// Generate a response from the AI with optional tool use
        /// </summary>
        public async Task<AIResponse> GenerateAsync(string userMessage, List<AIMessage>? conversationHistory = null, AIProvider? specificProvider = null)
        {
            var provider = specificProvider ?? GetBestProvider(userMessage);
            if (provider == null)
            {
                return new AIResponse { Error = "No AI providers configured. Please configure at least one provider in Orchestration settings." };
            }

            if (!_clients.TryGetValue(provider.ProviderType, out var client))
            {
                return new AIResponse { Error = $"No client available for provider type: {provider.ProviderType}" };
            }

            if (!client.ValidateConfiguration(provider))
            {
                return new AIResponse { Error = $"Provider '{provider.Name}' is not properly configured." };
            }

            // Build the request
            var request = new AIRequest
            {
                Temperature = Settings.DefaultTemperature,
                MaxTokens = Settings.DefaultMaxTokens,
                SystemPrompt = BuildSystemPrompt()
            };

            // Add conversation history
            if (conversationHistory != null)
            {
                request.Messages.AddRange(conversationHistory);
            }

            // Add user message
            request.Messages.Add(new AIMessage { Role = "user", Content = userMessage });

            // Add tools if enabled
            if (Settings.EnableToolUse)
            {
                request.Tools = _toolsService.GetAllTools().ToList();
            }

            // Generate response
            var response = await client.GenerateAsync(provider, request);

            // Handle tool calls if present
            if (response.RequiresToolExecution && Settings.EnableToolUse)
            {
                response = await HandleToolCallsAsync(provider, client, request, response);
            }

            return response;
        }

        /// <summary>
        /// Handle tool calls and continue the conversation
        /// </summary>
        private async Task<AIResponse> HandleToolCallsAsync(AIProvider provider, IAIProviderClient client, AIRequest request, AIResponse response)
        {
            const int maxIterations = 5;
            int iteration = 0;

            while (response.RequiresToolExecution && iteration < maxIterations)
            {
                iteration++;

                // Add assistant message with tool calls
                request.Messages.Add(new AIMessage
                {
                    Role = "assistant",
                    Content = response.Content,
                    ToolCalls = response.ToolCalls
                });

                // Execute each tool call
                foreach (var toolCall in response.ToolCalls!)
                {
                    var result = _toolsService.ExecuteTool(toolCall.Name, toolCall.Arguments);
                    
                    request.Messages.Add(new AIMessage
                    {
                        Role = "tool",
                        Content = result,
                        ToolCallId = toolCall.Id,
                        Name = toolCall.Name
                    });
                }

                // Generate next response
                response = await client.GenerateAsync(provider, request);
            }

            return response;
        }

        /// <summary>
        /// Build the system prompt with context
        /// </summary>
        private string BuildSystemPrompt()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("You are AIA, a helpful AI assistant integrated into a personal productivity application.");
            sb.AppendLine("You have access to tools to help manage the user's tasks, reminders, and data bank.");
            sb.AppendLine();
            sb.AppendLine("Current date/time: " + DateTime.Now.ToString("f"));
            sb.AppendLine();

            if (Settings.EnableToolUse)
            {
                sb.AppendLine("You have access to tools to retrieve and manage the user's data. Use them when needed:");
                sb.AppendLine("- Use get_tasks, get_reminders, get_databank_entries to retrieve information");
                sb.AppendLine("- Use create_task, create_reminder, create_databank_entry to create new items");
                sb.AppendLine("- Use get_app_summary for a quick overview of the user's data");
                sb.AppendLine();
            }

            // Add context based on settings
            var vm = _getViewModel();

            if (Settings.IncludeTasksContext)
            {
                var activeTasks = vm.Tasks.Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled)
                    .Take(Settings.MaxContextItems)
                    .ToList();

                if (activeTasks.Count > 0)
                {
                    sb.AppendLine("Active tasks:");
                    foreach (var task in activeTasks)
                    {
                        sb.AppendLine($"- {task.Title} ({task.Status}, {task.Priority}){(task.IsOverdue ? " [OVERDUE]" : "")}");
                    }
                    sb.AppendLine();
                }
            }

            if (Settings.IncludeRemindersContext)
            {
                var activeReminders = vm.Reminders.Where(r => !r.IsCompleted)
                    .OrderBy(r => r.DueDate)
                    .Take(Settings.MaxContextItems)
                    .ToList();

                if (activeReminders.Count > 0)
                {
                    sb.AppendLine("Upcoming reminders:");
                    foreach (var reminder in activeReminders)
                    {
                        sb.AppendLine($"- {reminder.Title} (Due: {reminder.DueDate:g}){(reminder.IsOverdue ? " [OVERDUE]" : "")}");
                    }
                    sb.AppendLine();
                }
            }

            if (Settings.IncludeDataBankContext && vm.SelectedCategory != null)
            {
                sb.AppendLine($"Selected data bank category: {vm.SelectedCategory.Name} ({vm.SelectedCategory.EntryCount} entries)");
                sb.AppendLine();
            }

            sb.AppendLine("Be concise but helpful. When the user asks about their tasks, reminders, or data, use the appropriate tools to get accurate information.");

            return sb.ToString();
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get a list of recommended model IDs for each provider type
        /// </summary>
        public static Dictionary<AIProviderType, string[]> GetRecommendedModels()
        {
            return new Dictionary<AIProviderType, string[]>
            {
                [AIProviderType.OpenAI] = new[] { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-4", "gpt-3.5-turbo" },
                [AIProviderType.AzureOpenAI] = new[] { "gpt-4o", "gpt-4o-mini", "gpt-4", "gpt-35-turbo" },
                [AIProviderType.Google] = new[] { "gemini-2.0-flash-exp", "gemini-1.5-pro", "gemini-1.5-flash", "gemini-pro" },
                [AIProviderType.Anthropic] = new[] { "claude-3-5-sonnet-20241022", "claude-3-opus-20240229", "claude-3-sonnet-20240229", "claude-3-haiku-20240307" }
            };
        }

        /// <summary>
        /// Test a provider configuration
        /// </summary>
        public async Task<(bool Success, string Message)> TestProviderAsync(AIProvider provider)
        {
            if (!_clients.TryGetValue(provider.ProviderType, out var client))
            {
                return (false, "No client available for this provider type");
            }

            if (!client.ValidateConfiguration(provider))
            {
                return (false, "Invalid configuration. Please check API key, endpoint, and model settings.");
            }

            try
            {
                var request = new AIRequest
                {
                    Messages = new List<AIMessage> { new AIMessage { Role = "user", Content = "Say 'Hello, I am working!' in 5 words or less." } },
                    MaxTokens = 50,
                    Temperature = 0.5
                };

                var response = await client.GenerateAsync(provider, request);

                if (response.Success)
                {
                    return (true, $"Success! Response: {response.Content}");
                }
                else
                {
                    return (false, $"Error: {response.Error}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all registered tools
        /// </summary>
        public IEnumerable<AITool> GetAvailableTools() => _toolsService.GetAllTools();

        #endregion
    }
}
