using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Conversation.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserConversationContent), "user")]
[JsonDerivedType(typeof(AssistantConversationContent), "assistant")]
[JsonDerivedType(typeof(ToolConversationContent), "tool")]
public abstract record ConversationContent;
