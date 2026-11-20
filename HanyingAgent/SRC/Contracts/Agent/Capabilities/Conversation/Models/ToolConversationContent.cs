using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;

public sealed record ToolConversationContent(
    [property: JsonRequired] ToolObservation Observation) : ConversationContent;
