using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Models;

public sealed record ModelToolMessage(
    [property: JsonRequired] ToolObservation Observation) : ModelMessage;
