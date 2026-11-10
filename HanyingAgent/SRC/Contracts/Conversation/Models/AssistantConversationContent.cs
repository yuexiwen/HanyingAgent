using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.Conversation.Models;

public sealed record AssistantConversationContent(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls,
    ModelFinishReason? FinishReason = null,
    string? ProviderFinishReason = null) : ConversationContent;
