using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;

public sealed record AssistantConversationContent(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls,
    ModelFinishReason? FinishReason = null,
    string? ProviderFinishReason = null) : ConversationContent;
