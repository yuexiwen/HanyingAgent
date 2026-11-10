using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Tools.Operations.Responses;

namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelToolMessage(
    [property: JsonRequired] ToolObservation Observation) : ModelMessage;
