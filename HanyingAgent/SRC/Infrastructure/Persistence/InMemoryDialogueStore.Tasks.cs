using System.Text.Json;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Users.Models;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed partial class InMemoryDialogueStore
{
    private readonly Dictionary<TaskId, TaskInfo> _tasks = new();
    private readonly Dictionary<TaskId, TaskResult> _results = new();
    private readonly Dictionary<SessionId, List<TaskId>> _sessionTasks = new();

    public TaskInfo CreateTask(SubmitTaskRequest request, UserId owner, DateTimeOffset now, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            RequireWritableSession(request.SessionId, owner);
            var task = new TaskInfo(new TaskId(Guid.NewGuid().ToString("N")), request.SessionId,
                request.Input, TaskStatus.Created, 1, now, now, null);
            _tasks.Add(task.Id, task);
            if (!_sessionTasks.TryGetValue(task.SessionId, out var history))
            {
                history = [];
                _sessionTasks.Add(task.SessionId, history);
            }
            history.Add(task.Id);
            var session = _sessions[request.SessionId];
            _sessions[session.Id] = session with
            {
                Status = SessionStatus.Active, CurrentTaskId = task.Id,
                LastActiveAt = now > session.LastActiveAt ? now : session.LastActiveAt
            };
            return task;
        }
    }

    public TaskInfo? GetTask(TaskId id, UserId owner, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            return _tasks.ContainsKey(id) ? RequireTask(id, owner) : null;
        }
    }

    public TaskResult? GetTaskResult(TaskId id, UserId owner, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            RequireTask(id, owner);
            return _results.GetValueOrDefault(id);
        }
    }

    public TaskListResponse ListTasks(ListSessionTasksRequest request, UserId owner, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            RequireSession(request.SessionId, owner);
            var history = _sessionTasks.GetValueOrDefault(request.SessionId) ?? [];
            var before = history.Count + 1L;
            if (request.ContinuationToken is { } value)
            {
                TaskCursor? cursor;
                try
                {
                    cursor = JsonSerializer.Deserialize<TaskCursor>(Convert.FromBase64String(value));
                }
                catch (Exception exception) when (exception is FormatException or JsonException)
                {
                    throw new ArgumentException("Invalid task cursor.", nameof(request), exception);
                }
                if (cursor is null || cursor.Session != request.SessionId.Value || cursor.Status != request.Status ||
                    cursor.Before <= 0 || cursor.Before > before)
                {
                    throw new ArgumentException("Task cursor does not match this query.", nameof(request));
                }
                before = cursor.Before;
            }
            var page = new List<TaskInfo>();
            long lastSequence = 0;
            for (var index = checked((int)(before - 2)); index >= 0; index--)
            {
                var task = _tasks[history[index]];
                if (request.Status is not null && task.Status != request.Status)
                {
                    continue;
                }
                if (page.Count == request.PageSize)
                {
                    var next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                        new TaskCursor(request.SessionId.Value, request.Status, lastSequence)));
                    return new TaskListResponse(page.ToArray(), next);
                }
                page.Add(task);
                lastSequence = index + 1L;
            }
            return new TaskListResponse(page.ToArray(), null);
        }
    }

    public TaskInfo StartTask(TaskId id, UserId owner, DateTimeOffset now) =>
        TransitionTask(id, owner, TaskStatus.Created, TaskStatus.Running, now);

    public TaskInfo WaitForModel(TaskId id, UserId owner, DateTimeOffset now) =>
        TransitionTask(id, owner, TaskStatus.Running, TaskStatus.WaitingForModel, now);

    private TaskInfo TransitionTask(TaskId id, UserId owner, TaskStatus expected, TaskStatus next, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = RequireTask(id, owner);
            if (task.Status == TaskStatus.Cancelling)
            {
                return task;
            }
            if (task.Status != expected)
            {
                throw new InvalidOperationException("Invalid task state transition.");
            }
            return UpdateTask(task, next, now);
        }
    }

    public TaskInfo RequestCancellation(TaskId id, UserId owner, DateTimeOffset now, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            var task = RequireTask(id, owner);
            return IsTerminal(task.Status) || task.Status == TaskStatus.Cancelling
                ? task : UpdateTask(task, TaskStatus.Cancelling, now);
        }
    }

    public TaskResult FinishTask(TaskId id, UserId owner, string? answer, string? error,
        bool cancelled, DateTimeOffset now)
    {
        lock (_gate)
        {
            var task = RequireTask(id, owner);
            if (_results.TryGetValue(id, out var existing))
            {
                return existing;
            }
            var session = RequireSession(task.SessionId, owner);
            if (session.CurrentTaskId != id || session.Status != SessionStatus.Active)
            {
                throw new InvalidOperationException("Task no longer owns its session.");
            }
            var status = cancelled ? TaskStatus.Cancelled : error is null ? TaskStatus.Completed : TaskStatus.Failed;
            var completed = UpdateTask(task, status, now);
            var result = new TaskResult(id, status, answer, error, completed.CompletedAt!.Value);
            _results.Add(id, result);
            _sessions[session.Id] = session with
            {
                Status = SessionStatus.Idle, CurrentTaskId = null,
                LastActiveAt = completed.UpdatedAt > session.LastActiveAt ? completed.UpdatedAt : session.LastActiveAt
            };
            return result;
        }
    }

    private TaskInfo UpdateTask(TaskInfo task, TaskStatus status, DateTimeOffset now)
    {
        var updatedAt = now > task.UpdatedAt ? now : task.UpdatedAt;
        var updated = task with
        {
            Status = status, Version = checked(task.Version + 1), UpdatedAt = updatedAt,
            CompletedAt = IsTerminal(status) ? updatedAt : null
        };
        _tasks[task.Id] = updated;
        return updated;
    }

    private TaskInfo RequireTask(TaskId id, UserId owner)
    {
        var task = _tasks.TryGetValue(id, out var existing)
            ? existing : throw new KeyNotFoundException("Task does not exist.");
        RequireSession(task.SessionId, owner);
        return task;
    }

    private static bool IsTerminal(TaskStatus status) =>
        status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled;

    private sealed record TaskCursor(string Session, TaskStatus? Status, long Before);
}
