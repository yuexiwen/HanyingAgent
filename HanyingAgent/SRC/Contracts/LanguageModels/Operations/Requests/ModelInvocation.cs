using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.Tools.Models;

namespace HanyingAgent.Contracts.LanguageModels.Operations.Requests;

public sealed record ModelInvocation(
    [property: JsonRequired] IReadOnlyList<ModelMessage> Messages,
    [property: JsonRequired] IReadOnlyList<ToolDefinition> Tools,
    ModelOptions? Options = null);
