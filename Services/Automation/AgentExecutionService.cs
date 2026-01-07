using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIA.Models;
using AIA.Models.AI;
using AIA.Models.Automation;
using AIA.Services.AI;

namespace AIA.Services.Automation
{
    /// <summary>
    /// Service that executes automation agents
    /// </summary>
    public class AgentExecutionService
    {
        private readonly Func<OverlayViewModel> _getViewModel;
        private readonly Dictionary<Guid, CancellationTokenSource> _runningAgents = new();
        private readonly SemaphoreSlim _agentSemaphore;

        public AgentExecutionService(Func<OverlayViewModel> getViewModel, int maxConcurrentAgents = 3)
        {
            _getViewModel = getViewModel;
            _agentSemaphore = new SemaphoreSlim(maxConcurrentAgents);
        }

        /// <summary>
        /// Executes an automation's agent
        /// </summary>
        public async Task<AgentExecutionResult> ExecuteAsync(
            AutomationTask automation,
            AutomationContext context,
            AutomationExecution execution)
        {
            var cts = new CancellationTokenSource();
            _runningAgents[execution.Id] = cts;

            try
            {
                await _agentSemaphore.WaitAsync(cts.Token);

                var agent = automation.Agent;
                var orchestration = _getViewModel().AIOrchestration;

                execution.AddTrace(TraceLevel.Info, $"Starting agent: {agent.Name}", 
                    $"Type: {agent.AgentType}, Max iterations: {agent.MaxIterations}");

                // Build the prompt
                var userPrompt = agent.ResolveUserPrompt(context);
                execution.AddTrace(TraceLevel.Debug, "Resolved user prompt", userPrompt);

                // Get AI provider
                var provider = agent.PreferredProviderId.HasValue
                    ? orchestration.Providers.FirstOrDefault(p => p.Id == agent.PreferredProviderId.Value)
                    : null;

                // Build conversation history
                var messages = new List<AIMessage>();

                // Add system prompt
                if (!string.IsNullOrEmpty(agent.SystemPrompt))
                {
                    messages.Add(new AIMessage { Role = "system", Content = BuildAgentSystemPrompt(agent, context) });
                }

                // Add user prompt
                messages.Add(new AIMessage { Role = "user", Content = userPrompt });

                // Execute based on agent type
                return agent.AgentType switch
                {
                    AgentType.SimplePrompt => await ExecuteSimpleAgentAsync(automation, agent, messages, provider, execution, cts.Token),
                    AgentType.MultiStep => await ExecuteMultiStepAgentAsync(automation, agent, messages, provider, context, execution, cts.Token),
                    AgentType.Orchestrator => await ExecuteOrchestratorAgentAsync(automation, agent, messages, provider, context, execution, cts.Token),
                    _ => AgentExecutionResult.Failure("Unknown agent type")
                };
            }
            catch (OperationCanceledException)
            {
                return AgentExecutionResult.Failure("Agent execution was cancelled");
            }
            catch (Exception ex)
            {
                execution.AddTrace(TraceLevel.Error, "Agent execution failed", ex.Message);
                return AgentExecutionResult.Failure(ex.Message);
            }
            finally
            {
                _runningAgents.Remove(execution.Id);
                _agentSemaphore.Release();
            }
        }

        /// <summary>
        /// Cancels a running agent
        /// </summary>
        public void Cancel(Guid executionId)
        {
            if (_runningAgents.TryGetValue(executionId, out var cts))
            {
                cts.Cancel();
            }
        }

        private string BuildAgentSystemPrompt(AutomationAgent agent, AutomationContext context)
        {
            var sb = new StringBuilder();

            sb.AppendLine(agent.SystemPrompt);
            sb.AppendLine();
            sb.AppendLine("Current date/time: " + DateTime.Now.ToString("f"));
            sb.AppendLine();

            if (agent.AgentType == AgentType.MultiStep || agent.AgentType == AgentType.Orchestrator)
            {
                sb.AppendLine("You are a multi-step agent that can iterate to complete complex tasks.");
                sb.AppendLine($"Maximum iterations allowed: {agent.MaxIterations}");
                sb.AppendLine($"Maximum total tokens: {agent.MaxTotalTokens}");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(agent.CompletionCriteria))
                {
                    sb.AppendLine("Completion criteria:");
                    sb.AppendLine(agent.CompletionCriteria);
                    sb.AppendLine();
                }

                sb.AppendLine("When your task is complete, respond with '[TASK_COMPLETE]' followed by a summary.");
                sb.AppendLine("If you need to continue iterating, explain your next steps.");
                sb.AppendLine();
            }

            if (agent.AgentType == AgentType.Orchestrator)
            {
                sb.AppendLine("As an orchestrator agent, you can spawn sub-agents to handle subtasks.");
                sb.AppendLine("Use the 'run_sub_agent' tool to delegate work to specialized agents.");
                sb.AppendLine();
            }

            // Add context variables info
            if (context.Variables.Count > 0)
            {
                sb.AppendLine("Available context variables:");
                foreach (var variable in context.Variables.Take(20))
                {
                    var valueStr = variable.Value?.ToString() ?? "null";
                    if (valueStr.Length > 100)
                        valueStr = valueStr.Substring(0, 100) + "...";
                    sb.AppendLine($"  - {variable.Key}: {valueStr}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private async Task<AgentExecutionResult> ExecuteSimpleAgentAsync(
            AutomationTask automation,
            AutomationAgent agent,
            List<AIMessage> messages,
            AIProvider? provider,
            AutomationExecution execution,
            CancellationToken cancellationToken)
        {
            var orchestration = _getViewModel().AIOrchestration;

            execution.AddTrace(TraceLevel.Info, "Executing simple prompt agent");
            execution.CurrentIteration = 1;

            var response = await orchestration.GenerateAsync(
                messages.Last().Content,
                messages.Take(messages.Count - 1).ToList(),
                provider);

            execution.TotalTokensUsed += response.PromptTokens + response.CompletionTokens;

            if (!response.Success)
            {
                execution.AddTrace(TraceLevel.Error, "AI generation failed", response.Error);
                return AgentExecutionResult.Failed(response.Error ?? "AI generation failed");
            }

            execution.AddTrace(TraceLevel.Info, "Agent response received", 
                response.Content.Length > 500 ? response.Content.Substring(0, 500) + "..." : response.Content);

            return AgentExecutionResult.Succeeded(response.Content);
        }

        private async Task<AgentExecutionResult> ExecuteMultiStepAgentAsync(
            AutomationTask automation,
            AutomationAgent agent,
            List<AIMessage> messages,
            AIProvider? provider,
            AutomationContext context,
            AutomationExecution execution,
            CancellationToken cancellationToken)
        {
            var orchestration = _getViewModel().AIOrchestration;
            var totalTokens = 0;
            var finalResult = new StringBuilder();

            for (int iteration = 1; iteration <= agent.MaxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if automation is paused
                if (automation.Status == AutomationStatus.Paused)
                {
                    execution.AddTrace(TraceLevel.Info, "Agent paused, waiting for resume");
                    while (automation.Status == AutomationStatus.Paused)
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                }

                execution.CurrentIteration = iteration;
                execution.AddTrace(TraceLevel.Info, $"Starting iteration {iteration}/{agent.MaxIterations}");

                var response = await orchestration.GenerateAsync(
                    messages.Last().Content,
                    messages.Take(messages.Count - 1).ToList(),
                    provider);

                totalTokens += response.PromptTokens + response.CompletionTokens;
                execution.TotalTokensUsed = totalTokens;

                if (!response.Success)
                {
                    execution.AddTrace(TraceLevel.Error, $"Iteration {iteration} failed", response.Error);
                    return AgentExecutionResult.Failed(response.Error ?? "AI generation failed");
                }

                var content = response.Content;
                execution.AddTrace(TraceLevel.Debug, $"Iteration {iteration} response",
                    content.Length > 500 ? content.Substring(0, 500) + "..." : content);

                // Check for completion
                if (content.Contains("[TASK_COMPLETE]"))
                {
                    var resultText = content.Replace("[TASK_COMPLETE]", "").Trim();
                    execution.AddTrace(TraceLevel.Info, "Agent signaled task completion", resultText);
                    return AgentExecutionResult.Succeeded(resultText);
                }

                // Check token limit
                if (totalTokens >= agent.MaxTotalTokens)
                {
                    execution.AddTrace(TraceLevel.Warning, "Token limit reached", 
                        $"Used {totalTokens} of {agent.MaxTotalTokens} tokens");
                    return AgentExecutionResult.Succeeded(content, true);
                }

                // Add assistant response to history for next iteration
                messages.Add(new AIMessage { Role = "assistant", Content = content });

                // Add continuation prompt
                messages.Add(new AIMessage
                {
                    Role = "user",
                    Content = "Continue with your task. Remember to respond with '[TASK_COMPLETE]' followed by a summary when finished."
                });

                finalResult.Clear();
                finalResult.Append(content);
            }

            execution.AddTrace(TraceLevel.Warning, "Max iterations reached");
            return AgentExecutionResult.Succeeded(finalResult.ToString(), true);
        }

        private async Task<AgentExecutionResult> ExecuteOrchestratorAgentAsync(
            AutomationTask automation,
            AutomationAgent agent,
            List<AIMessage> messages,
            AIProvider? provider,
            AutomationContext context,
            AutomationExecution execution,
            CancellationToken cancellationToken)
        {
            // Orchestrator agent is similar to multi-step but with sub-agent capabilities
            // For now, implement basic version - sub-agent spawning would need more infrastructure

            execution.AddTrace(TraceLevel.Info, "Executing orchestrator agent");

            // Add orchestrator-specific tools info to system prompt
            var orchestratorPrompt = @"
As an orchestrator, you can:
1. Break down complex tasks into subtasks
2. Decide which type of processing each subtask needs
3. Aggregate results from subtasks
4. Make decisions based on partial results

Report your progress and decisions clearly.
";
            messages[0] = new AIMessage
            {
                Role = "system",
                Content = messages[0].Content + orchestratorPrompt
            };

            // Execute as multi-step for now
            return await ExecuteMultiStepAgentAsync(automation, agent, messages, provider, context, execution, cancellationToken);
        }
    }

    /// <summary>
    /// Result of agent execution
    /// </summary>
    public class AgentExecutionResult
    {
        public bool IsSuccess { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public bool ReachedLimit { get; set; }

        public static AgentExecutionResult Succeeded(string result, bool reachedLimit = false)
        {
            return new AgentExecutionResult { IsSuccess = true, Result = result, ReachedLimit = reachedLimit };
        }

        public static AgentExecutionResult Failure(string error)
        {
            return new AgentExecutionResult { IsSuccess = false, Error = error };
        }

        public static AgentExecutionResult Failed(string error)
        {
            return Failure(error);
        }
    }
}
