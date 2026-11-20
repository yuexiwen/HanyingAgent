using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;

public sealed record ConversationMessage(
    [property: JsonRequired] MessageId Id,
    [property: JsonRequired] SessionId SessionId,
    TaskId? TaskId,
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] ConversationContent Content,
    [property: JsonRequired] DateTimeOffset CreatedAt);
