using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelUserMessage(
    [property: JsonRequired] string Content) : ModelMessage;
