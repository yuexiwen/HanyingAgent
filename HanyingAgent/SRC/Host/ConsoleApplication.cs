using System.Text.Json;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Diagnostics;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Operations.Responses;
using HanyingAgent.Runtime.Orchestration;
using HanyingAgent.Runtime.Users;
using Microsoft.Extensions.Logging;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Host;

public sealed class ConsoleApplication(
    LocalTaskManager tasks,
    ISessionManager sessions,
    IConversationManager conversations,
    LocalUserContext identity,
    ILogger<ConsoleApplication> logger,
    TextReader input,
    TextWriter output)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await output.WriteLineAsync("HanyingAgent - local in-memory tasks. Replies appear automatically. /new NAME, /session [ID], /sessions, /use ID, /history [CURSOR], /tasks [CURSOR], /task [ID], /wait [ID], /cancel [ID], /close, /exit. Task commands default to the last submitted task. Exit cancels running tasks.");
        SessionId? current = null;
        TaskId? lastTask = null;
        var pending = new Dictionary<TaskId, PendingResult>();
        var unread = new Dictionary<TaskId, SessionResult>();
        var displayed = new HashSet<TaskId>();
        Task<string?>? read = null;
        using var notifications = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var userId = await identity.GetUserIdAsync(cancellationToken);
            current = (await sessions.CreateAsync(new CreateSessionRequest(userId, "Default"), cancellationToken)).Id;
            await output.WriteLineAsync($"Session: {current.Value.Value} (Default)");
            while (!cancellationToken.IsCancellationRequested)
            {
                var ready = pending.Where(item => item.Value.Completion.IsCompleted).ToArray();
                var hasNotifications = ready.Any(item => !displayed.Contains(item.Key));
                if (hasNotifications && read is not null)
                {
                    await output.WriteLineAsync();
                }
                foreach (var (id, update) in ready)
                {
                    pending.Remove(id);
                    try
                    {
                        var result = await update.Completion;
                        if (displayed.Contains(id))
                        {
                            continue;
                        }
                        if (update.SessionId == current)
                        {
                            await RenderResultAsync(result);
                            displayed.Add(id);
                        }
                        else
                        {
                            unread[id] = new SessionResult(update.SessionId, result);
                            await output.WriteLineAsync(
                                $"[Session: {update.SessionId.Value}, Task: {id.Value} [{result.Status}]. Use /use {update.SessionId.Value} to view the update.]");
                            if (result.Error is { } error)
                            {
                                await output.WriteLineAsync($"[Task: {id.Value}: {error}]");
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(LogEvents.ConsoleTaskNotificationFailed, exception,
                            "Task result notification failed for {TaskId}", id.Value);
                        await output.WriteLineAsync($"[Result notification failed for task {id.Value}. Use /task {id.Value}. See logs.]");
                    }
                }
                if (read is null || hasNotifications)
                {
                    await output.WriteAsync("You> ");
                    await output.FlushAsync();
                }
                // Keep one read in flight while results wake the output loop; never read a second line concurrently.
                read ??= Task.Run(() => input.ReadLine(), CancellationToken.None);
                var completed = await Task.WhenAny([read, .. pending.Values.Select(item => item.Completion)])
                    .WaitAsync(cancellationToken);
                if (completed != read)
                {
                    continue;
                }
                var line = await read;
                read = null;
                if (line is null || line.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (line.TrimStart().StartsWith('/'))
                    {
                        var command = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var argument = command.Length == 2 ? command[1] : null;
                        switch (command[0].ToLowerInvariant())
                        {
                            case "/new":
                                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                                var created = await sessions.CreateAsync(new CreateSessionRequest(userId, argument), cancellationToken);
                                current = created.Id;
                                await RenderSessionAsync(created, current, cancellationToken);
                                break;
                            case "/sessions" when argument is null:
                                foreach (var session in await sessions.ListAsync(cancellationToken))
                                {
                                    var sessionTask = await GetSessionTaskAsync(session, cancellationToken);
                                    await output.WriteLineAsync(
                                        $"{(session.Id == current ? "*" : " ")} {session.Id.Value} {session.Name} [{session.Status}] | {DescribeSessionTask(sessionTask)}");
                                }
                                break;
                            case "/session":
                                var requestedSessionId = argument is null ? RequireCurrent(current) : new SessionId(argument);
                                var requestedSession = await sessions.GetAsync(requestedSessionId, cancellationToken)
                                    ?? throw new KeyNotFoundException("Session does not exist.");
                                await RenderSessionAsync(requestedSession, current, cancellationToken);
                                break;
                            case "/use":
                                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                                var selected = await sessions.GetAsync(new SessionId(argument), cancellationToken)
                                    ?? throw new KeyNotFoundException("Session does not exist.");
                                current = selected.Id;
                                await RenderSessionAsync(selected, current, cancellationToken);
                                foreach (var (id, update) in unread.Where(item => item.Value.SessionId == current).ToArray())
                                {
                                    await RenderResultAsync(update.Result);
                                    displayed.Add(id);
                                    unread.Remove(id);
                                }
                                break;
                            case "/history":
                                var page = await conversations.ListBySessionAsync(new ListConversationMessagesRequest(
                                    RequireCurrent(current), ContinuationToken: argument), cancellationToken);
                                if (page.Items.Count == 0)
                                {
                                    await output.WriteLineAsync("[No messages in this session.]");
                                }
                                foreach (var message in page.Items)
                                {
                                    var text = message.Content switch
                                    {
                                        UserConversationContent user => $"User: {user.Text}",
                                        AssistantConversationContent assistant => $"Assistant [{assistant.FinishReason}]: {assistant.Text}",
                                        _ => throw new NotSupportedException("Unsupported history content.")
                                    };
                                    await output.WriteLineAsync($"{message.Sequence} {text}");
                                }
                                if (page.ContinuationToken is { } cursor)
                                {
                                    await output.WriteLineAsync($"Older messages: /history {cursor}");
                                }
                                break;
                            case "/close" when argument is null:
                                await sessions.CloseAsync(RequireCurrent(current), cancellationToken);
                                current = null;
                                await output.WriteLineAsync("Session closed. Use /new NAME or /use ID.");
                                break;
                            case "/tasks":
                                var taskPage = await tasks.ListBySessionAsync(new ListSessionTasksRequest(
                                    RequireCurrent(current), ContinuationToken: argument), cancellationToken);
                                if (taskPage.Items.Count == 0)
                                {
                                    await output.WriteLineAsync("[No tasks in this session.]");
                                }
                                foreach (var task in taskPage.Items)
                                {
                                    await output.WriteLineAsync($"Task: {task.Id.Value} [{task.Status}] v{task.Version}");
                                }
                                if (taskPage.ContinuationToken is { } taskCursor)
                                {
                                    await output.WriteLineAsync($"Older tasks: /tasks {taskCursor}");
                                }
                                break;
                            case "/task":
                                var taskId = ResolveTask(argument, lastTask);
                                var snapshot = await tasks.GetAsync(taskId, cancellationToken)
                                    ?? throw new KeyNotFoundException("Task does not exist.");
                                await output.WriteLineAsync($"Task: {snapshot.Id.Value} [{snapshot.Status}] v{snapshot.Version}");
                                if (await tasks.GetResultAsync(taskId, cancellationToken) is { } result)
                                {
                                    await RenderResultAsync(result, includeStatus: false);
                                    displayed.Add(taskId);
                                    unread.Remove(taskId);
                                }
                                break;
                            case "/wait":
                                var waitingId = ResolveTask(argument, lastTask);
                                var waited = await tasks.WaitForResultAsync(waitingId, cancellationToken);
                                await RenderResultAsync(waited, includeBody: !displayed.Contains(waitingId));
                                displayed.Add(waitingId);
                                unread.Remove(waitingId);
                                break;
                            case "/cancel":
                                var requested = await tasks.CancelAsync(ResolveTask(argument, lastTask), cancellationToken);
                                await output.WriteLineAsync($"Task: {requested.Id.Value} [{requested.Status}]. Cancellation is confirmed only after execution stops.");
                                break;
                            default:
                                throw new ArgumentException("Unknown command or invalid arguments.");
                        }
                        continue;
                    }
                    var accepted = await tasks.SubmitAsync(
                        new SubmitTaskRequest(RequireCurrent(current), line), cancellationToken);
                    lastTask = accepted.Id;
                    pending.Add(accepted.Id, new PendingResult(accepted.SessionId,
                        tasks.WaitForResultAsync(accepted.Id, notifications.Token)));
                    await output.WriteLineAsync($"Task accepted: {accepted.Id.Value}. The result will appear automatically.");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await output.WriteLineAsync("[Request timed out or dependency cancelled. See logs; you can try another question.]");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or
                    ArgumentException or InvalidOperationException or NotSupportedException or
                    KeyNotFoundException or UnauthorizedAccessException or IOException)
                {
                    logger.LogWarning(LogEvents.SessionCommandFailed, exception, "Console operation failed");
                    await output.WriteLineAsync("[Request failed. See logs; you can try another question.]");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("[Cancelled. Exiting.]");
        }
        finally
        {
            // Stop result waits, not business tasks; Host owns task cancellation and shutdown.
            await notifications.CancelAsync();
            foreach (var (id, update) in pending)
            {
                try
                {
                    await update.Completion;
                }
                catch (OperationCanceledException) when (notifications.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    logger.LogError(LogEvents.ConsoleTaskNotificationFailed, exception,
                        "Task result notification failed during console exit for {TaskId}", id.Value);
                }
            }
        }
    }

    private static SessionId RequireCurrent(SessionId? current) =>
        current ?? throw new InvalidOperationException("No session selected. Use /new NAME or /use ID.");

    private static TaskId ResolveTask(string? argument, TaskId? lastTask) =>
        argument is not null ? new TaskId(argument) :
        lastTask ?? throw new InvalidOperationException("No task submitted. Specify a TaskId.");

    private async Task RenderResultAsync(TaskResult result, bool includeStatus = true, bool includeBody = true)
    {
        if (includeStatus)
        {
            await output.WriteLineAsync($"Task: {result.TaskId.Value} [{result.Status}]");
        }
        if (!includeBody)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
        {
            await output.WriteLineAsync($"Assistant> {result.FinalAnswer}");
        }
        if (result.Error is { } error)
        {
            await output.WriteLineAsync($"[{error}]");
        }
    }

    private async Task RenderSessionAsync(SessionInfo session, SessionId? current, CancellationToken token)
    {
        var task = await GetSessionTaskAsync(session, token);
        await output.WriteLineAsync($"Session: {session.Id.Value} ({session.Name}) [{session.Status}]");
        await output.WriteLineAsync($"Selected: {(session.Id == current ? "yes" : "no")}");
        await output.WriteLineAsync($"Created: {session.CreatedAt:O}, last active: {session.LastActiveAt:O}");
        await output.WriteLineAsync(DescribeSessionTask(task));
        if (session.Status == SessionStatus.Active && task is null)
        {
            await output.WriteLineAsync("[A standalone dialogue is in progress without a TaskId.]");
        }
    }

    private async Task<TaskInfo?> GetSessionTaskAsync(SessionInfo session, CancellationToken token)
    {
        if (session.CurrentTaskId is { } id)
        {
            return await tasks.GetAsync(id, token)
                ?? throw new InvalidOperationException("The session's current task does not exist.");
        }
        var page = await tasks.ListBySessionAsync(new ListSessionTasksRequest(session.Id, PageSize: 1), token);
        return page.Items.SingleOrDefault();
    }

    private static string DescribeSessionTask(TaskInfo? task)
    {
        if (task is null)
        {
            return "Task: none (no tasks submitted)";
        }
        var finished = task.Status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Cancelled;
        return $"Task: {task.Id.Value} [{task.Status}] | Finished: {(finished ? "yes" : "no")}";
    }

    private sealed record PendingResult(SessionId SessionId, Task<TaskResult> Completion);
    private sealed record SessionResult(SessionId SessionId, TaskResult Result);
}
