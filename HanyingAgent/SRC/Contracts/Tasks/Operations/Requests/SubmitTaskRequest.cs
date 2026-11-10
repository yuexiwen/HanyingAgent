using HanyingAgent.Contracts.Sessions.Models;

namespace HanyingAgent.Contracts.Tasks.Operations.Requests;

public sealed record SubmitTaskRequest(
    SessionId SessionId,
    string Input);
