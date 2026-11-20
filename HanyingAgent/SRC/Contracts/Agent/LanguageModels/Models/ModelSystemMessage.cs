using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Models;

public sealed record ModelSystemMessage(
    [property: JsonRequired] string Content) : ModelMessage;
