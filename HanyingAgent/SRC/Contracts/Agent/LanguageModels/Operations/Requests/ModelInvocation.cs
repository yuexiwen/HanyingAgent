using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Operations.Requests;

public sealed record ModelInvocation(
    [property: JsonRequired] IReadOnlyList<ModelMessage> Messages,
    [property: JsonRequired] IReadOnlyList<ToolDefinition> Tools,
    ModelOptions? Options = null);
