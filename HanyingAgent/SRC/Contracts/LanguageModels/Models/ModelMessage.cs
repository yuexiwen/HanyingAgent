using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.LanguageModels.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(ModelSystemMessage), "system")]
[JsonDerivedType(typeof(ModelUserMessage), "user")]
[JsonDerivedType(typeof(ModelAssistantMessage), "assistant")]
[JsonDerivedType(typeof(ModelToolMessage), "tool")]
public abstract record ModelMessage;
