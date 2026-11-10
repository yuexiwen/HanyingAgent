namespace HanyingAgent.Contracts.Tasks.Models;

public enum TaskStatus
{
    Created = 0,
    Running = 1,
    WaitingForModel = 2,
    WaitingForTool = 3,
    WaitingForUser = 4,
    Cancelling = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8
}
