using HanyingAgent.Contracts.Management.Sessions.Models;

namespace HanyingAgent.Contracts.Management.Tasks.Operations.Requests;

public sealed record SubmitTaskRequest(
    SessionId SessionId,
    string Input);
