using HanyingAgent.Contracts.Tasks.Models;

namespace HanyingAgent.Contracts.Tasks.Operations.Requests;

public sealed record ProvideTaskInputRequest(
    TaskId TaskId,
    long ExpectedVersion,
    string Input);
