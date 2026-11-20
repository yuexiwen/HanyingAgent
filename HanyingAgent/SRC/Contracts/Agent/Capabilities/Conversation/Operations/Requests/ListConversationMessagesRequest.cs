using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Management.Sessions.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Requests;

public sealed record ListConversationMessagesRequest(
    [property: JsonRequired] SessionId SessionId,
    int PageSize = 20,
    string? ContinuationToken = null);
