using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;

namespace HanyingAgent.Contracts.Conversation.Operations.Requests;

public sealed record AppendConversationMessageRequest(
    [property: JsonRequired] MessageId Id,
    [property: JsonRequired] SessionId SessionId,
    TaskId? TaskId,
    [property: JsonRequired] ConversationContent Content);
