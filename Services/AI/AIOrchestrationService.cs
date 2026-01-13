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
        /// Generate a streaming response from the AI (text only, no tool use)
        /// </summary>
        public async IAsyncEnumerable<string> GenerateStreamAsync(string userMessage, List<AIMessage>? conversationHistory = null, AIProvider? specificProvider = null)
        {
            var provider = specificProvider ?? GetBestProvider(userMessage);
            if (provider == null)
            {
                yield return "Error: No AI providers configured. Please configure at least one provider in Orchestration settings.";
                yield break;
            }

            if (!_clients.TryGetValue(provider.ProviderType, out var client))
            {
                yield return $"Error: No client available for provider type: {provider.ProviderType}";
                yield break;
            }

            if (!client.ValidateConfiguration(provider))
            {
                yield return $"Error: Provider '{provider.Name}' is not properly configured.";
                yield break;
            }

            // Build the request
            var request = new AIRequest
            {
                Temperature = Settings.DefaultTemperature,
                MaxTokens = Settings.DefaultMaxTokens,
                SystemPrompt = BuildSystemPrompt(),
                StreamResponse = true
            };

            // Add conversation history
            if (conversationHistory != null)
            {
                request.Messages.AddRange(conversationHistory);
            }

            // Add user message
            request.Messages.Add(new AIMessage { Role = "user", Content = userMessage });

            // Stream the response
            await foreach (var chunk in client.GenerateStreamAsync(provider, request))
            {
                yield return chunk;
            }
        }

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
        /// Generate a streaming response from the AI with tool use support
        /// </summary>
        public async IAsyncEnumerable<(string Content, string Status)> GenerateStreamWithToolsAsync(string userMessage, List<AIMessage>? conversationHistory = null, AIProvider? specificProvider = null)
        {
            var provider = specificProvider ?? GetBestProvider(userMessage);
            if (provider == null)
            {
                yield return ("Error: No AI providers configured. Please configure at least one provider in Orchestration settings.", "Error");
                yield break;
            }

            if (!_clients.TryGetValue(provider.ProviderType, out var client))
            {
                yield return ($"Error: No client available for provider type: {provider.ProviderType}", "Error");
                yield break;
            }

            if (!client.ValidateConfiguration(provider))
            {
                yield return ($"Error: Provider '{provider.Name}' is not properly configured.", "Error");
                yield break;
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

            // First, get response (potentially with tool calls)
            yield return ("", "Analyzing request...");

            var response = await client.GenerateAsync(provider, request);

            if (!response.Success)
            {
                yield return ($"Error: {response.Error}", "Error");
                yield break;
            }

            // Handle tool calls if present
            if (response.RequiresToolExecution && Settings.EnableToolUse)
            {
                const int maxIterations = 5;
                int iteration = 0;
                bool hasInjectedImage = false;

                while (response.RequiresToolExecution && iteration < maxIterations)
                {
                    iteration++;

                    yield return ("", $"Executing tools ({iteration}/{maxIterations})...");

                    System.Diagnostics.Debug.WriteLine($"=== Tool execution iteration {iteration} ===");
                    System.Diagnostics.Debug.WriteLine($"Tool calls count: {response.ToolCalls?.Count ?? 0}");

                    // Add assistant message with tool calls
                    request.Messages.Add(new AIMessage
                    {
                        Role = "assistant",
                        Content = response.Content,
                        ToolCalls = response.ToolCalls
                    });

                    // Execute each tool call and check for image responses
                    foreach (var toolCall in response.ToolCalls!)
                    {
                        yield return ("", $"Calling {toolCall.Name}...");

                        var result = _toolsService.ExecuteTool(toolCall.Name, toolCall.Arguments);

                        System.Diagnostics.Debug.WriteLine($"Tool {toolCall.Name} result length: {result.Length}");

                        // Check if this is an image response that needs special handling
                        var imageData = TryExtractImageFromToolResult(result);

                        if (imageData != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"? Detected image response from {toolCall.Name}, injecting vision message");
                            hasInjectedImage = true;

                            // Add a simple tool result acknowledgment
                            request.Messages.Add(new AIMessage
                            {
                                Role = "tool",
                                Content = JsonSerializer.Serialize(new {
                                    success = true,
                                    message = "Screenshot retrieved successfully. Analyzing image..."
                                }),
                                ToolCallId = toolCall.Id,
                                Name = toolCall.Name
                            });

                            // Add a user message with the image for vision analysis
                            request.Messages.Add(new AIMessage
                            {
                                Role = "user",
                                Content = "Here is the screenshot. Please describe what you see in detail.",
                                Images = new List<AIImageContent>
                                {
                                    new AIImageContent
                                    {
                                        Base64Data = imageData.Base64,
                                        MimeType = imageData.MimeType,
                                        Description = imageData.Description
                                    }
                                }
                            });

                            System.Diagnostics.Debug.WriteLine($"Image injected: {imageData.Width}x{imageData.Height}, {imageData.Base64.Length} chars");
                        }
                        else
                        {
                            // Regular tool result
                            request.Messages.Add(new AIMessage
                            {
                                Role = "tool",
                                Content = result,
                                ToolCallId = toolCall.Id,
                                Name = toolCall.Name
                            });
                        }
                    }

                    // Get next response
                    yield return ("", "Generating response...");

                    // If we just injected an image, disable tools for the next call
                    // to prevent the AI from calling more tools instead of analyzing the image
                    if (hasInjectedImage)
                    {
                        System.Diagnostics.Debug.WriteLine("Temporarily disabling tools for vision response");
                        var originalTools = request.Tools;
                        request.Tools = null;

                        response = await client.GenerateAsync(provider, request);

                        request.Tools = originalTools;
                        hasInjectedImage = false; // Reset flag
                    }
                    else
                    {
                        response = await client.GenerateAsync(provider, request);
                      }

                    System.Diagnostics.Debug.WriteLine($"Next response - Success: {response.Success}, HasContent: {!string.IsNullOrEmpty(response.Content)}, HasToolCalls: {response.RequiresToolExecution}");

                    if (!response.Success)
                    {
                        yield return ($"Error: {response.Error}", "Error");
                        yield break;
                    }
                }

                // Stream the final content (either after tool execution or when max iterations reached)
                if (!string.IsNullOrEmpty(response.Content))
                {
                    yield return ("", "Streaming response...");

                    System.Diagnostics.Debug.WriteLine($"Streaming final content: {response.Content.Length} chars");

                    // If the AI provided content in the response, stream it character by character
                    foreach (var c in response.Content)
                    {
                        yield return (c.ToString(), "Streaming");
                        await Task.Delay(5); // Small delay for smooth streaming effect
                    }
                }
                else
                {
                    // No content in response, might be another tool call or empty response
                    System.Diagnostics.Debug.WriteLine($"?? No content in final response. Iterations: {iteration}, RequiresToolExecution: {response.RequiresToolExecution}, FinishReason: {response.FinishReason}");

                    if (response.RequiresToolExecution)
                    {
                        yield return ("I reached the maximum number of tool calls. The last tool execution did not produce a final response.", "Streaming");
                    }
                    else
                    {
                        yield return ("I processed your request but didn't generate a response. Please try rephrasing your question.", "Streaming");
                    }
                }
            }
            else
            {
                // No tools needed, stream the response directly
                if (!string.IsNullOrEmpty(response.Content))
                {
                    // Already have the content, simulate streaming for consistency
                    foreach (var c in response.Content)
                    {
                        yield return (c.ToString(), "Streaming");
                        await Task.Delay(5);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to extract image data from a tool result JSON
        /// </summary>
        private static ImageToolResult? TryExtractImageFromToolResult(string toolResult)
        {
            try
            {
                using var doc = JsonDocument.Parse(toolResult);
                var root = doc.RootElement;

                // Check for the special _imageResponse marker
                if (root.TryGetProperty("_imageResponse", out var marker) && marker.GetBoolean())
                {
                    return new ImageToolResult
                    {
                        Base64 = root.GetProperty("base64").GetString() ?? "",
                        MimeType = root.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "image/png" : "image/png",
                        Name = root.TryGetProperty("name", out var n) ? n.GetString() : null,
                        Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                        Width = root.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                        Height = root.TryGetProperty("height", out var h) ? h.GetInt32() : 0
                    };
                }
            }
            catch
            {
                // Not a valid JSON or doesn't contain image data
            }

            return null;
        }

        private class ImageToolResult
        {
            public string Base64 { get; set; } = "";
            public string MimeType { get; set; } = "image/png";
            public string? Name { get; set; }
            public string? Description { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
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

                    // Check if this is an image response that needs special handling
                    var imageData = TryExtractImageFromToolResult(result);

                    if (imageData != null)
                    {
                        // Add a simple tool result acknowledgment
                        request.Messages.Add(new AIMessage
                        {
                            Role = "tool",
                            Content = JsonSerializer.Serialize(new
                            {
                                success = true,
                                message = $"Screenshot retrieved: {imageData.Description ?? imageData.Name}. The image has been provided for your analysis."
                            }),
                            ToolCallId = toolCall.Id,
                            Name = toolCall.Name
                        });

                        // Add a user message with the image for vision analysis
                        request.Messages.Add(new AIMessage
                        {
                            Role = "user",
                            Content = "Here is the screenshot from the tool call. Please analyze it and describe what you see.",
                            Images = new List<AIImageContent>
                            {
                                new AIImageContent
                                {
                                    Base64Data = imageData.Base64,
                                    MimeType = imageData.MimeType,
                                    Description = imageData.Description
                                }
                            }
                        });
                    }
                    else
                    {
                        request.Messages.Add(new AIMessage
                        {
                            Role = "tool",
                            Content = result,
                            ToolCallId = toolCall.Id,
                            Name = toolCall.Name
                        });
                    }
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
            sb.AppendLine("You also have VISION capabilities - you can see and analyze screenshots when the user asks about their screen.");
            sb.AppendLine();
            sb.AppendLine("Current date/time: " + DateTime.Now.ToString("f"));
            sb.AppendLine();

            if (Settings.EnableToolUse)
            {
                sb.AppendLine("You have access to tools to retrieve and manage the user's data. Use them when needed:");
                sb.AppendLine("- Use get_tasks, get_reminders, get_databank_entries to retrieve information");
                sb.AppendLine("- Use create_task, create_reminder, create_databank_entry to create new items");
                sb.AppendLine("- Use get_app_summary for a quick overview of the user's data");
                sb.AppendLine("- Use list_available_screenshots and get_screenshot_base64 to see the user's screen");
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
        /// Generate a chat title using AI based on the first user message
        /// </summary>
        public async Task<string?> GenerateChatTitleAsync(string firstUserMessage)
        {
            System.Diagnostics.Debug.WriteLine($"GenerateChatTitleAsync called with message: {firstUserMessage?.Substring(0, Math.Min(50, firstUserMessage?.Length ?? 0))}...");

            // Find the provider to use for auto-naming
            AIProvider? provider = null;

            if (Settings.AutoNamingProviderId.HasValue)
            {
                provider = Providers.FirstOrDefault(p => p.Id == Settings.AutoNamingProviderId.Value && p.IsEnabled);
                System.Diagnostics.Debug.WriteLine($"Auto-naming provider ID specified: {Settings.AutoNamingProviderId.Value}, found: {provider != null}");
            }

            // Fallback to first enabled provider
            if (provider == null)
            {
                provider = Providers.FirstOrDefault(p => p.IsEnabled);
                System.Diagnostics.Debug.WriteLine($"Using fallback provider: {provider?.Name ?? "none"}");
            }

            if (provider == null)
            {
                System.Diagnostics.Debug.WriteLine("No provider available for auto-naming");
                return null;
            }

            if (!_clients.TryGetValue(provider.ProviderType, out var client))
            {
                System.Diagnostics.Debug.WriteLine($"No client available for provider type: {provider.ProviderType}");
                return null;
            }

            if (!client.ValidateConfiguration(provider))
            {
                System.Diagnostics.Debug.WriteLine($"Provider validation failed for: {provider.Name}");
                return null;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Calling AI for title generation with temp={Settings.AutoNamingTemperature}, maxTokens={Settings.AutoNamingMaxTokens}");

                var request = new AIRequest
                {
                    Messages = new List<AIMessage>
                    {
                        new AIMessage
                        {
                            Role = "user",
                            Content = $"Generate a concise 3-5 word title that summarizes this message. Return ONLY the title, no quotes, no punctuation, no explanation:\n\n{firstUserMessage}"
                        }
                    },
                    MaxTokens = Settings.AutoNamingMaxTokens,
                    Temperature = Settings.AutoNamingTemperature,
                    SystemPrompt = "You are a title generator. You create very short, concise titles (3-5 words max) that capture the essence of a message. Respond with only the title, nothing else.",
                    Tools = null, // IMPORTANT: Explicitly disable tools for auto-naming
                    StreamResponse = false
                };

                System.Diagnostics.Debug.WriteLine($"Making API call to {provider.Name} ({provider.ProviderType})...");
                var response = await client.GenerateAsync(provider, request);
                System.Diagnostics.Debug.WriteLine($"API call completed. Success: {response.Success}");

                // Check if response has tool calls (which would make Content empty)
                if (response.ToolCalls != null && response.ToolCalls.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Response has {response.ToolCalls.Count} tool calls, but tools should be disabled for auto-naming!");
                }

                if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
                {
                    System.Diagnostics.Debug.WriteLine($"Raw response content: '{response.Content}'");

                    // Clean up the response - remove quotes, trim, and limit length
                    var title = response.Content.Trim().Trim('"', '\'', '.', '!', '?');

                    // Limit to reasonable length
                    if (title.Length > 50)
                        title = title.Substring(0, 47) + "...";

                    System.Diagnostics.Debug.WriteLine($"Generated title (cleaned): '{title}'");
                    return title;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"AI response failed or empty. Success: {response.Success}, Content: '{response.Content ?? "(null)"}', Error: {response.Error ?? "(none)"}");
                    System.Diagnostics.Debug.WriteLine($"Response FinishReason: {response.FinishReason ?? "(none)"}, ToolCalls count: {response.ToolCalls?.Count ?? 0}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in GenerateChatTitleAsync: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                // Silently fail - will keep default name
            }

            return null;
        }

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
