using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIA.Models.AI;

namespace AIA.Services.AI
{
    /// <summary>
    /// Base interface for AI provider clients
    /// </summary>
    public interface IAIProviderClient
    {
        AIProviderType ProviderType { get; }
        Task<AIResponse> GenerateAsync(AIProvider provider, AIRequest request);
        bool ValidateConfiguration(AIProvider provider);
    }

    /// <summary>
    /// OpenAI API client implementation
    /// </summary>
    public class OpenAIClient : IAIProviderClient
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.openai.com/v1";

        public AIProviderType ProviderType => AIProviderType.OpenAI;

        public OpenAIClient()
        {
            _httpClient = new HttpClient();
        }

        public bool ValidateConfiguration(AIProvider provider)
        {
            return !string.IsNullOrEmpty(provider.ApiKey) && !string.IsNullOrEmpty(provider.ModelId);
        }

        public async Task<AIResponse> GenerateAsync(AIProvider provider, AIRequest request)
        {
            try
            {
                var messages = new List<object>();
                
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    messages.Add(new { role = "system", content = request.SystemPrompt });
                }

                foreach (var msg in request.Messages)
                {
                    if (msg.ToolCallId != null)
                    {
                        messages.Add(new { role = "tool", tool_call_id = msg.ToolCallId, content = msg.Content });
                    }
                    else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCalls = new List<object>();
                        foreach (var tc in msg.ToolCalls)
                        {
                            toolCalls.Add(new
                            {
                                id = tc.Id,
                                type = "function",
                                function = new { name = tc.Name, arguments = JsonSerializer.Serialize(tc.Arguments) }
                            });
                        }
                        messages.Add(new { role = msg.Role, content = msg.Content, tool_calls = toolCalls });
                    }
                    else
                    {
                        messages.Add(new { role = msg.Role, content = msg.Content });
                    }
                }

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = provider.ModelId,
                    ["messages"] = messages,
                    ["temperature"] = request.Temperature,
                    ["max_tokens"] = request.MaxTokens
                };

                if (request.Tools != null && request.Tools.Count > 0)
                {
                    var tools = new List<object>();
                    foreach (var tool in request.Tools)
                    {
                        var properties = new Dictionary<string, object>();
                        var required = new List<string>();

                        foreach (var param in tool.Parameters)
                        {
                            var paramDef = new Dictionary<string, object>
                            {
                                ["type"] = param.Value.Type,
                                ["description"] = param.Value.Description
                            };
                            
                            // Only add enum if it has values
                            if (param.Value.Enum != null && param.Value.Enum.Length > 0)
                            {
                                paramDef["enum"] = param.Value.Enum;
                            }
                            
                            properties[param.Key] = paramDef;
                            
                            if (param.Value.Required)
                            {
                                required.Add(param.Key);
                            }
                        }

                        var parametersObj = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = required
                        };

                        tools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = tool.Name,
                                description = tool.Description,
                                parameters = parametersObj
                            }
                        });
                    }
                    requestBody["tools"] = tools;
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
                httpRequest.Content = content;

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AIResponse { Error = $"OpenAI API error: {response.StatusCode} - {responseContent}" };
                }

                var result = JsonSerializer.Deserialize<OpenAIResponse>(responseContent);
                if (result?.Choices == null || result.Choices.Count == 0)
                {
                    return new AIResponse { Error = "No response from OpenAI" };
                }

                var choice = result.Choices[0];
                var aiResponse = new AIResponse
                {
                    Content = choice.Message?.Content ?? string.Empty,
                    FinishReason = choice.FinishReason,
                    PromptTokens = result.Usage?.PromptTokens ?? 0,
                    CompletionTokens = result.Usage?.CompletionTokens ?? 0,
                    UsedProvider = provider
                };

                if (choice.Message?.ToolCalls != null)
                {
                    aiResponse.ToolCalls = new List<AIToolCall>();
                    foreach (var tc in choice.Message.ToolCalls)
                    {
                        aiResponse.ToolCalls.Add(new AIToolCall
                        {
                            Id = tc.Id ?? Guid.NewGuid().ToString(),
                            Name = tc.Function?.Name ?? string.Empty,
                            Arguments = string.IsNullOrEmpty(tc.Function?.Arguments) 
                                ? new Dictionary<string, object>() 
                                : JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Function.Arguments) ?? new()
                        });
                    }
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                return new AIResponse { Error = $"OpenAI request failed: {ex.Message}" };
            }
        }

        #region OpenAI Response Models
        private class OpenAIResponse
        {
            [JsonPropertyName("choices")]
            public List<OpenAIChoice>? Choices { get; set; }
            
            [JsonPropertyName("usage")]
            public OpenAIUsage? Usage { get; set; }
        }

        private class OpenAIChoice
        {
            [JsonPropertyName("message")]
            public OpenAIMessage? Message { get; set; }
            
            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class OpenAIMessage
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }
            
            [JsonPropertyName("content")]
            public string? Content { get; set; }
            
            [JsonPropertyName("tool_calls")]
            public List<OpenAIToolCall>? ToolCalls { get; set; }
        }

        private class OpenAIToolCall
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
            
            [JsonPropertyName("type")]
            public string? Type { get; set; }
            
            [JsonPropertyName("function")]
            public OpenAIFunction? Function { get; set; }
        }

        private class OpenAIFunction
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }
            
            [JsonPropertyName("arguments")]
            public string? Arguments { get; set; }
        }

        private class OpenAIUsage
        {
            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }
            
            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }
        }
        #endregion
    }

    /// <summary>
    /// Azure OpenAI API client implementation
    /// </summary>
    public class AzureOpenAIClient : IAIProviderClient
    {
        private readonly HttpClient _httpClient;

        public AIProviderType ProviderType => AIProviderType.AzureOpenAI;

        public AzureOpenAIClient()
        {
            _httpClient = new HttpClient();
        }

        public bool ValidateConfiguration(AIProvider provider)
        {
            return !string.IsNullOrEmpty(provider.ApiKey) 
                && !string.IsNullOrEmpty(provider.Endpoint)
                && !string.IsNullOrEmpty(provider.DeploymentName);
        }

        public async Task<AIResponse> GenerateAsync(AIProvider provider, AIRequest request)
        {
            try
            {
                var messages = new List<object>();
                
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    messages.Add(new { role = "system", content = request.SystemPrompt });
                }

                foreach (var msg in request.Messages)
                {
                    if (msg.ToolCallId != null)
                    {
                        messages.Add(new { role = "tool", tool_call_id = msg.ToolCallId, content = msg.Content });
                    }
                    else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var toolCalls = new List<object>();
                        foreach (var tc in msg.ToolCalls)
                        {
                            toolCalls.Add(new
                            {
                                id = tc.Id,
                                type = "function",
                                function = new { name = tc.Name, arguments = JsonSerializer.Serialize(tc.Arguments) }
                            });
                        }
                        messages.Add(new { role = msg.Role, content = msg.Content, tool_calls = toolCalls });
                    }
                    else
                    {
                        messages.Add(new { role = msg.Role, content = msg.Content });
                    }
                }

                var requestBody = new Dictionary<string, object>
                {
                    ["messages"] = messages,
                    ["temperature"] = request.Temperature,
                    ["max_completion_tokens"] = request.MaxTokens
                };

                if (request.Tools != null && request.Tools.Count > 0)
                {
                    var tools = new List<object>();
                    foreach (var tool in request.Tools)
                    {
                        var properties = new Dictionary<string, object>();
                        var required = new List<string>();

                        foreach (var param in tool.Parameters)
                        {
                            var paramDef = new Dictionary<string, object>
                            {
                                ["type"] = param.Value.Type,
                                ["description"] = param.Value.Description
                            };
                            
                            // Only add enum if it has values
                            if (param.Value.Enum != null && param.Value.Enum.Length > 0)
                            {
                                paramDef["enum"] = param.Value.Enum;
                            }
                            
                            properties[param.Key] = paramDef;
                            
                            if (param.Value.Required)
                            {
                                required.Add(param.Key);
                            }
                        }

                        var parametersObj = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = required
                        };

                        tools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = tool.Name,
                                description = tool.Description,
                                parameters = parametersObj
                            }
                        });
                    }
                    requestBody["tools"] = tools;
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Azure OpenAI uses a different URL pattern
                var endpoint = provider.Endpoint.TrimEnd('/');
                var apiUrl = $"{endpoint}/openai/deployments/{provider.DeploymentName}/chat/completions?api-version=2024-02-15-preview";

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Headers.Add("api-key", provider.ApiKey);
                httpRequest.Content = content;

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AIResponse { Error = $"Azure OpenAI API error: {response.StatusCode} - {responseContent}" };
                }

                // Azure OpenAI uses the same response format as OpenAI
                var result = JsonSerializer.Deserialize<AzureOpenAIResponse>(responseContent);
                if (result?.Choices == null || result.Choices.Count == 0)
                {
                    return new AIResponse { Error = "No response from Azure OpenAI" };
                }

                var choice = result.Choices[0];
                var aiResponse = new AIResponse
                {
                    Content = choice.Message?.Content ?? string.Empty,
                    FinishReason = choice.FinishReason,
                    PromptTokens = result.Usage?.PromptTokens ?? 0,
                    CompletionTokens = result.Usage?.CompletionTokens ?? 0,
                    UsedProvider = provider
                };

                if (choice.Message?.ToolCalls != null)
                {
                    aiResponse.ToolCalls = new List<AIToolCall>();
                    foreach (var tc in choice.Message.ToolCalls)
                    {
                        aiResponse.ToolCalls.Add(new AIToolCall
                        {
                            Id = tc.Id ?? Guid.NewGuid().ToString(),
                            Name = tc.Function?.Name ?? string.Empty,
                            Arguments = string.IsNullOrEmpty(tc.Function?.Arguments)
                                ? new Dictionary<string, object>()
                                : JsonSerializer.Deserialize<Dictionary<string, object>>(tc.Function.Arguments) ?? new()
                        });
                    }
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                return new AIResponse { Error = $"Azure OpenAI request failed: {ex.Message}" };
            }
        }

        #region Azure OpenAI Response Models
        private class AzureOpenAIResponse
        {
            [JsonPropertyName("choices")]
            public List<AzureChoice>? Choices { get; set; }

            [JsonPropertyName("usage")]
            public AzureUsage? Usage { get; set; }
        }

        private class AzureChoice
        {
            [JsonPropertyName("message")]
            public AzureMessage? Message { get; set; }

            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private class AzureMessage
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }

            [JsonPropertyName("tool_calls")]
            public List<AzureToolCall>? ToolCalls { get; set; }
        }

        private class AzureToolCall
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("function")]
            public AzureFunction? Function { get; set; }
        }

        private class AzureFunction
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("arguments")]
            public string? Arguments { get; set; }
        }

        private class AzureUsage
        {
            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }
        }
        #endregion
    }

    /// <summary>
    /// Google Gemini API client implementation
    /// </summary>
    public class GoogleGeminiClient : IAIProviderClient
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public AIProviderType ProviderType => AIProviderType.Google;

        public GoogleGeminiClient()
        {
            _httpClient = new HttpClient();
        }

        public bool ValidateConfiguration(AIProvider provider)
        {
            return !string.IsNullOrEmpty(provider.ApiKey) && !string.IsNullOrEmpty(provider.ModelId);
        }

        public async Task<AIResponse> GenerateAsync(AIProvider provider, AIRequest request)
        {
            try
            {
                var contents = new List<object>();

                // Add system instruction if present
                object? systemInstruction = null;
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } };
                }

                foreach (var msg in request.Messages)
                {
                    var role = msg.Role == "assistant" ? "model" : "user";
                    
                    if (msg.ToolCallId != null)
                    {
                        contents.Add(new
                        {
                            role = "function",
                            parts = new[] { new { functionResponse = new { name = msg.Name, response = new { result = msg.Content } } } }
                        });
                    }
                    else
                    {
                        contents.Add(new { role, parts = new[] { new { text = msg.Content } } });
                    }
                }

                var requestBody = new Dictionary<string, object>
                {
                    ["contents"] = contents,
                    ["generationConfig"] = new
                    {
                        temperature = request.Temperature,
                        maxOutputTokens = request.MaxTokens
                    }
                };

                if (systemInstruction != null)
                {
                    requestBody["systemInstruction"] = systemInstruction;
                }

                if (request.Tools != null && request.Tools.Count > 0)
                {
                    var functionDeclarations = new List<object>();
                    foreach (var tool in request.Tools)
                    {
                        var properties = new Dictionary<string, object>();
                        var required = new List<string>();

                        foreach (var param in tool.Parameters)
                        {
                            var paramDef = new Dictionary<string, object>
                            {
                                ["type"] = param.Value.Type.ToUpperInvariant(),
                                ["description"] = param.Value.Description
                            };
                            
                            // Only add enum if it has values
                            if (param.Value.Enum != null && param.Value.Enum.Length > 0)
                            {
                                paramDef["enum"] = param.Value.Enum;
                            }
                            
                            properties[param.Key] = paramDef;
                            
                            if (param.Value.Required)
                            {
                                required.Add(param.Key);
                            }
                        }

                        var parametersObj = new Dictionary<string, object>
                        {
                            ["type"] = "OBJECT",
                            ["properties"] = properties,
                            ["required"] = required
                        };

                        functionDeclarations.Add(new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            parameters = parametersObj
                        });
                    }
                    requestBody["tools"] = new[] { new { functionDeclarations } };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var apiUrl = $"{BaseUrl}/models/{provider.ModelId}:generateContent?key={provider.ApiKey}";

                var response = await _httpClient.PostAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AIResponse { Error = $"Google API error: {response.StatusCode} - {responseContent}" };
                }

                var result = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
                if (result?.Candidates == null || result.Candidates.Count == 0)
                {
                    return new AIResponse { Error = "No response from Google Gemini" };
                }

                var candidate = result.Candidates[0];
                var aiResponse = new AIResponse
                {
                    Content = ExtractTextContent(candidate),
                    FinishReason = candidate.FinishReason,
                    UsedProvider = provider
                };

                // Handle function calls
                var functionCalls = ExtractFunctionCalls(candidate);
                if (functionCalls.Count > 0)
                {
                    aiResponse.ToolCalls = functionCalls;
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                return new AIResponse { Error = $"Google Gemini request failed: {ex.Message}" };
            }
        }

        private static string ExtractTextContent(GeminiCandidate candidate)
        {
            if (candidate.Content?.Parts == null) return string.Empty;
            
            var sb = new StringBuilder();
            foreach (var part in candidate.Content.Parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    sb.Append(part.Text);
                }
            }
            return sb.ToString();
        }

        private static List<AIToolCall> ExtractFunctionCalls(GeminiCandidate candidate)
        {
            var calls = new List<AIToolCall>();
            if (candidate.Content?.Parts == null) return calls;

            foreach (var part in candidate.Content.Parts)
            {
                if (part.FunctionCall != null)
                {
                    calls.Add(new AIToolCall
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = part.FunctionCall.Name ?? string.Empty,
                        Arguments = part.FunctionCall.Args ?? new Dictionary<string, object>()
                    });
                }
            }
            return calls;
        }

        #region Gemini Response Models
        private class GeminiResponse
        {
            [JsonPropertyName("candidates")]
            public List<GeminiCandidate>? Candidates { get; set; }
        }

        private class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent? Content { get; set; }

            [JsonPropertyName("finishReason")]
            public string? FinishReason { get; set; }
        }

        private class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart>? Parts { get; set; }

            [JsonPropertyName("role")]
            public string? Role { get; set; }
        }

        private class GeminiPart
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("functionCall")]
            public GeminiFunctionCall? FunctionCall { get; set; }
        }

        private class GeminiFunctionCall
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("args")]
            public Dictionary<string, object>? Args { get; set; }
        }
        #endregion
    }

    /// <summary>
    /// Anthropic Claude API client implementation
    /// </summary>
    public class AnthropicClient : IAIProviderClient
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.anthropic.com/v1";

        public AIProviderType ProviderType => AIProviderType.Anthropic;

        public AnthropicClient()
        {
            _httpClient = new HttpClient();
        }

        public bool ValidateConfiguration(AIProvider provider)
        {
            return !string.IsNullOrEmpty(provider.ApiKey) && !string.IsNullOrEmpty(provider.ModelId);
        }

        public async Task<AIResponse> GenerateAsync(AIProvider provider, AIRequest request)
        {
            try
            {
                var messages = new List<object>();

                foreach (var msg in request.Messages)
                {
                    if (msg.ToolCallId != null)
                    {
                        messages.Add(new
                        {
                            role = "user",
                            content = new[]
                            {
                                new
                                {
                                    type = "tool_result",
                                    tool_use_id = msg.ToolCallId,
                                    content = msg.Content
                                }
                            }
                        });
                    }
                    else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        var contentBlocks = new List<object>();
                        if (!string.IsNullOrEmpty(msg.Content))
                        {
                            contentBlocks.Add(new { type = "text", text = msg.Content });
                        }
                        foreach (var tc in msg.ToolCalls)
                        {
                            contentBlocks.Add(new { type = "tool_use", id = tc.Id, name = tc.Name, input = tc.Arguments });
                        }
                        messages.Add(new { role = "assistant", content = contentBlocks });
                    }
                    else
                    {
                        messages.Add(new { role = msg.Role, content = msg.Content });
                    }
                }

                var requestBody = new Dictionary<string, object>
                {
                    ["model"] = provider.ModelId,
                    ["messages"] = messages,
                    ["max_tokens"] = request.MaxTokens
                };

                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    requestBody["system"] = request.SystemPrompt;
                }

                if (request.Tools != null && request.Tools.Count > 0)
                {
                    var tools = new List<object>();
                    foreach (var tool in request.Tools)
                    {
                        var properties = new Dictionary<string, object>();
                        var required = new List<string>();

                        foreach (var param in tool.Parameters)
                        {
                            var paramDef = new Dictionary<string, object>
                            {
                                ["type"] = param.Value.Type,
                                ["description"] = param.Value.Description
                            };
                            
                            // Only add enum if it has values
                            if (param.Value.Enum != null && param.Value.Enum.Length > 0)
                            {
                                paramDef["enum"] = param.Value.Enum;
                            }
                            
                            properties[param.Key] = paramDef;
                            
                            if (param.Value.Required)
                            {
                                required.Add(param.Key);
                            }
                        }

                        var inputSchema = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = required
                        };

                        tools.Add(new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            input_schema = inputSchema
                        });
                    }
                    requestBody["tools"] = tools;
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/messages");
                httpRequest.Headers.Add("x-api-key", provider.ApiKey);
                httpRequest.Headers.Add("anthropic-version", "2023-06-01");
                httpRequest.Content = content;

                var response = await _httpClient.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AIResponse { Error = $"Anthropic API error: {response.StatusCode} - {responseContent}" };
                }

                var result = JsonSerializer.Deserialize<AnthropicResponse>(responseContent);
                if (result == null)
                {
                    return new AIResponse { Error = "No response from Anthropic" };
                }

                var aiResponse = new AIResponse
                {
                    Content = ExtractTextContent(result),
                    FinishReason = result.StopReason,
                    PromptTokens = result.Usage?.InputTokens ?? 0,
                    CompletionTokens = result.Usage?.OutputTokens ?? 0,
                    UsedProvider = provider
                };

                var toolCalls = ExtractToolUse(result);
                if (toolCalls.Count > 0)
                {
                    aiResponse.ToolCalls = toolCalls;
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                return new AIResponse { Error = $"Anthropic request failed: {ex.Message}" };
            }
        }

        private static string ExtractTextContent(AnthropicResponse response)
        {
            if (response.Content == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var block in response.Content)
            {
                if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                {
                    sb.Append(block.Text);
                }
            }
            return sb.ToString();
        }

        private static List<AIToolCall> ExtractToolUse(AnthropicResponse response)
        {
            var calls = new List<AIToolCall>();
            if (response.Content == null) return calls;

            foreach (var block in response.Content)
            {
                if (block.Type == "tool_use")
                {
                    calls.Add(new AIToolCall
                    {
                        Id = block.Id ?? Guid.NewGuid().ToString(),
                        Name = block.Name ?? string.Empty,
                        Arguments = block.Input ?? new Dictionary<string, object>()
                    });
                }
            }
            return calls;
        }

        #region Anthropic Response Models
        private class AnthropicResponse
        {
            [JsonPropertyName("content")]
            public List<AnthropicContentBlock>? Content { get; set; }

            [JsonPropertyName("stop_reason")]
            public string? StopReason { get; set; }

            [JsonPropertyName("usage")]
            public AnthropicUsage? Usage { get; set; }
        }

        private class AnthropicContentBlock
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("input")]
            public Dictionary<string, object>? Input { get; set; }
        }

        private class AnthropicUsage
        {
            [JsonPropertyName("input_tokens")]
            public int InputTokens { get; set; }

            [JsonPropertyName("output_tokens")]
            public int OutputTokens { get; set; }
        }
        #endregion
    }
}
