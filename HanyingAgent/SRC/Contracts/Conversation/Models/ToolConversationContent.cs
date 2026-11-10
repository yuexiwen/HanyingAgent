using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Tools.Operations.Responses;

namespace HanyingAgent.Contracts.Conversation.Models;

public sealed record ToolConversationContent(
    [property: JsonRequired] ToolObservation Observation) : ConversationContent;
