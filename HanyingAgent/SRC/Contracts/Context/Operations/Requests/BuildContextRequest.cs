using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Context.Operations.Requests;

public sealed record BuildContextRequest(
    [property: JsonRequired] string SystemInstruction,
    [property: JsonRequired] IReadOnlyList<ModelMessage> Messages,
    [property: JsonRequired] IReadOnlyList<MemoryOwner> MemoryOwners,
    ModelOptions? Options = null);
