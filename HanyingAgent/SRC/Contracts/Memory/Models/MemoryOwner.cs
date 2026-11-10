using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Contracts.Memory.Models;

public sealed record MemoryOwner
{
    public MemoryOwner(
        MemoryScope scope,
        SessionId? sessionId = null,
        TaskId? taskId = null,
        UserId? userId = null)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown memory scope.");
        }

        if (sessionId is { } session)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(session.Value, nameof(sessionId));
        }

        if (taskId is { } task)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(task.Value, nameof(taskId));
        }

        if (userId is { } user)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(user.Value, nameof(userId));
        }

        switch (scope)
        {
            case MemoryScope.Task:
                if (sessionId is null || taskId is null || userId is not null)
                {
                    throw new ArgumentException("Task memory requires only session and task IDs.");
                }
                break;

            case MemoryScope.Session:
                if (sessionId is null || taskId is not null || userId is not null)
                {
                    throw new ArgumentException("Session memory requires only a session ID.");
                }
                break;

            case MemoryScope.User:
                if (userId is null || sessionId is not null || taskId is not null)
                {
                    throw new ArgumentException("User memory requires only a user ID.");
                }
                break;

            case MemoryScope.Global:
                if (sessionId is not null || taskId is not null || userId is not null)
                {
                    throw new ArgumentException("Global memory must not have owner IDs.");
                }
                break;
        }

        Scope = scope;
        SessionId = sessionId;
        TaskId = taskId;
        UserId = userId;
    }

    public MemoryScope Scope { get; }

    public SessionId? SessionId { get; }

    public TaskId? TaskId { get; }

    public UserId? UserId { get; }
}
