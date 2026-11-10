using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Sessions.Models;

namespace HanyingAgent.Contracts.Conversation.Operations.Requests;

public sealed record ListConversationMessagesRequest(
    [property: JsonRequired] SessionId SessionId,
    int PageSize = 20,
    string? ContinuationToken = null);
