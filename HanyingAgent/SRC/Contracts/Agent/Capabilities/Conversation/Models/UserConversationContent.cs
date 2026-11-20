using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;

public sealed record UserConversationContent(
    [property: JsonRequired] string Text) : ConversationContent;
