using System;

namespace AIA.Domain.Models
{
    /// <summary>
    /// Represents an AI provider configuration
    /// </summary>
    public class AIProvider
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public AIProviderType ProviderType { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public string? Endpoint { get; set; }
        public string? DeploymentName { get; set; }
        public string ModelId { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        // Sync metadata
        public DateTime? LastSyncedAt { get; set; }
        public bool IsSyncPending { get; set; }
        public string? SyncVersion { get; set; }
    }

    public enum AIProviderType
    {
        OpenAI,
        AzureOpenAI,
        Google,
        Anthropic
    }

    /// <summary>
    /// Orchestration mode for AI interactions
    /// </summary>
    public enum AIOrchestrationMode
    {
        Simple,    // Auto-naming, simple tasks
        Advanced   // Complex conversations, full tool use
    }
}
