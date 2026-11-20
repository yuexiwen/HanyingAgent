using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Management.Tasks.Operations.Responses;

namespace HanyingAgent.Host.Console;

internal sealed class ConsoleState
{
    public SessionId? Current { get; set; }
    public TaskId? LastTask { get; set; }
    public Dictionary<TaskId, PendingResult> Pending { get; } = new();
    public Dictionary<TaskId, SessionResult> Unread { get; } = new();
    public HashSet<TaskId> Displayed { get; } = new();

    public SessionId RequireCurrent() =>
        Current ?? throw new InvalidOperationException("No session selected. Use /new NAME or /use ID.");

    public TaskId ResolveTask(string? argument) =>
        argument is not null ? new TaskId(argument) :
        LastTask ?? throw new InvalidOperationException("No task submitted. Specify a TaskId.");

    public void MarkDisplayed(TaskId id)
    {
        Displayed.Add(id);
        Unread.Remove(id);
    }

    internal sealed record PendingResult(SessionId SessionId, Task<TaskResult> Completion);
    internal sealed record SessionResult(SessionId SessionId, TaskResult Result);
}
