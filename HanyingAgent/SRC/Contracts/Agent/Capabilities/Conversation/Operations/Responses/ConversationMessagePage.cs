using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Responses;

public sealed record ConversationMessagePage(
    [property: JsonRequired] IReadOnlyList<ConversationMessage> Items,
    string? ContinuationToken);
