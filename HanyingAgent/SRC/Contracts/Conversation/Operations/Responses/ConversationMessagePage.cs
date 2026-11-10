using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Conversation.Models;

namespace HanyingAgent.Contracts.Conversation.Operations.Responses;

public sealed record ConversationMessagePage(
    [property: JsonRequired] IReadOnlyList<ConversationMessage> Items,
    string? ContinuationToken);
