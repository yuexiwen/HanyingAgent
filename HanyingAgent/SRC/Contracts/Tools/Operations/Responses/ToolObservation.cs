using HanyingAgent.Contracts.Tools.Models;

namespace HanyingAgent.Contracts.Tools.Operations.Responses;

public sealed record ToolObservation(
    ToolCallId CallId,
    string ToolName,
    ToolCallStatus Status,
    string Summary,
    string? Error,
    string? ArtifactReference,
    DateTimeOffset Timestamp,
    TimeSpan Duration);
