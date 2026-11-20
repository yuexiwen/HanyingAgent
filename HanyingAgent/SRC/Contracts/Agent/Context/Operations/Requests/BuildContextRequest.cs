using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Context.Operations.Requests;

public sealed record BuildContextRequest(
    [property: JsonRequired] string SystemInstruction,
    [property: JsonRequired] IReadOnlyList<ModelMessage> Messages,
    [property: JsonRequired] IReadOnlyList<MemoryItem> Memories,
    ModelOptions? Options = null,
    int? MaxInputCharacters = null);
