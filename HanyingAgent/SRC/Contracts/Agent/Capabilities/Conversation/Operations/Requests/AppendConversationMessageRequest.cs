using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;
using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Requests;

public sealed record AppendConversationMessageRequest(
    [property: JsonRequired] MessageId Id,
    [property: JsonRequired] SessionId SessionId,
    TaskId? TaskId,
    [property: JsonRequired] ConversationContent Content);
