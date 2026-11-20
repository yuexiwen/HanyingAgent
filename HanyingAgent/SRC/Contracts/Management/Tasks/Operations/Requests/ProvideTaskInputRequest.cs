using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Management.Tasks.Operations.Requests;

public sealed record ProvideTaskInputRequest(
    TaskId TaskId,
    long ExpectedVersion,
    string Input);
