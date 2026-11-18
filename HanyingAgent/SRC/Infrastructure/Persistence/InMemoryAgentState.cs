using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using HanyingAgent.Contracts.Users.Models;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Infrastructure.Persistence;

internal sealed class InMemoryAgentState
{
    internal object Gate { get; } = new();
    internal Dictionary<UserId, UserInfo> Users { get; } = new();
    internal Dictionary<SessionId, SessionInfo> Sessions { get; } = new();
    internal Dictionary<MessageId, ConversationMessage> Messages { get; } = new();
    internal Dictionary<SessionId, List<ConversationMessage>> History { get; } = new();
    internal HashSet<SessionId> HiddenSessions { get; } = new();
    internal Dictionary<TaskId, TaskInfo> Tasks { get; } = new();
    internal Dictionary<TaskId, TaskResult> Results { get; } = new();
    internal Dictionary<SessionId, List<TaskId>> SessionTasks { get; } = new();

    // Store operations hold Gate across validation and all related reads or writes.
    internal SessionInfo RequireSession(SessionId id, UserId owner)
    {
        if (!Sessions.TryGetValue(id, out var session))
        {
            throw new KeyNotFoundException("Session does not exist.");
        }
        if (session.UserId != owner)
        {
            throw new UnauthorizedAccessException("Session belongs to another user.");
        }
        return session;
    }

    internal void RequireWritableSession(SessionId id, UserId owner)
    {
        if (RequireSession(id, owner).Status != SessionStatus.Idle)
        {
            throw new InvalidOperationException("Session is not open for new dialogue.");
        }
    }

    internal TaskInfo RequireTask(TaskId id, UserId owner)
    {
        var task = Tasks.TryGetValue(id, out var existing)
            ? existing : throw new KeyNotFoundException("Task does not exist.");
        RequireSession(task.SessionId, owner);
        return task;
    }

    internal static bool IsTerminal(TaskStatus status) =>
        status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled;
}
