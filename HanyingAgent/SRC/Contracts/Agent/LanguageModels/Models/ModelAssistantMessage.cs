using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Models;

public sealed record ModelAssistantMessage(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls) : ModelMessage;
