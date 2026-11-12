namespace HanyingAgent.Contracts.Execution.Models;

public enum ExecutionConclusion
{
    Delivered = 0,
    Incomplete = 1,
    Unconfirmed = 2,
    Refused = 3,
    Filtered = 4,
    UnsupportedAction = 5
}
