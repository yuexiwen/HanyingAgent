using System.Globalization;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Tasks.Operations.Requests;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Tasks;

namespace HanyingAgent.Host.Console;

internal sealed class ConsoleCommandHandler(
    LocalTaskManager tasks,
    ISessionManager sessions,
    ConversationHistoryReader history,
    ConsoleState state,
    ConsoleRenderer renderer,
    ConsoleLogControl consoleLogs)
{
    public async Task HandleAsync(
        string line, UserId userId, CancellationToken notificationToken, CancellationToken token)
    {
        if (!line.TrimStart().StartsWith('/'))
        {
            var accepted = await tasks.SubmitAsync(new SubmitTaskRequest(state.RequireCurrent(), line), token);
            state.LastTask = accepted.Id;
            state.Pending.Add(accepted.Id, new ConsoleState.PendingResult(
                accepted.SessionId, tasks.WaitForResultAsync(accepted.Id, notificationToken)));
            await renderer.WriteLineAsync($"Task accepted: {accepted.Id.Value}. The result will appear automatically.");
            return;
        }

        var command = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var argument = command.Length == 2 ? command[1] : null;
        switch (command[0].ToLowerInvariant())
        {
            case "/help" when argument is null:
                await renderer.ShowHelpAsync();
                break;
            case "/logs":
                if (argument is not null)
                {
                    if (argument.Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        consoleLogs.Enabled = true;
                    }
                    else if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        consoleLogs.Enabled = false;
                    }
                    else
                    {
                        throw new ArgumentException("Use /logs, /logs on, or /logs off.");
                    }
                }
                await renderer.WriteLineAsync($"Console logs: {(consoleLogs.Enabled ? "on" : "off")}.");
                break;
            case "/new":
                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                var created = await sessions.CreateAsync(new CreateSessionRequest(userId, argument), token);
                state.Current = created.Id;
                await ShowSessionAsync(created, token);
                break;
            case "/sessions" when argument is null:
                await ShowSessionListAsync(token);
                break;
            case "/delete":
                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                var hiddenId = await ResolveSessionIndexAsync(argument, token);
                await sessions.HideAsync(hiddenId, token);
                if (state.Current == hiddenId)
                {
                    state.Current = null;
                }
                await renderer.WriteLineAsync($"Session hidden: {hiddenId.Value}. Data and running tasks are retained; use its full ID to access it.");
                await ShowSessionListAsync(token);
                break;
            case "/session":
                var requestedId = argument is null ? state.RequireCurrent() : new SessionId(argument);
                var requested = await sessions.GetAsync(requestedId, token)
                    ?? throw new KeyNotFoundException("Session does not exist.");
                await ShowSessionAsync(requested, token);
                break;
            case "/use":
                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                var selectedId = argument.All(char.IsAsciiDigit)
                    ? await ResolveSessionIndexAsync(argument, token) : new SessionId(argument);
                var selected = await sessions.GetAsync(selectedId, token)
                    ?? throw new KeyNotFoundException("Session does not exist.");
                state.Current = selected.Id;
                await ShowSessionAsync(selected, token);
                foreach (var (id, update) in state.Unread.Where(item => item.Value.SessionId == state.Current).ToArray())
                {
                    await renderer.RenderResultAsync(update.Result);
                    state.MarkDisplayed(id);
                }
                break;
            case "/history":
                await renderer.RenderHistoryAsync(
                    await history.ReadAsync(state.RequireCurrent(), ParseHistoryCount(argument), token), token);
                break;
            case "/close" when argument is null:
                await sessions.CloseAsync(state.RequireCurrent(), token);
                state.Current = null;
                await renderer.WriteLineAsync("Session closed. Use /new NAME or /use ID.");
                break;
            case "/tasks":
                var page = await tasks.ListBySessionAsync(
                    new ListSessionTasksRequest(state.RequireCurrent(), ContinuationToken: argument), token);
                if (page.Items.Count == 0)
                {
                    await renderer.WriteLineAsync("[No tasks in this session.]");
                }
                foreach (var task in page.Items)
                {
                    await renderer.RenderTaskAsync(task);
                }
                if (page.ContinuationToken is { } cursor)
                {
                    await renderer.WriteLineAsync($"Older tasks: /tasks {cursor}");
                }
                break;
            case "/task":
                var taskId = state.ResolveTask(argument);
                var snapshot = await tasks.GetAsync(taskId, token)
                    ?? throw new KeyNotFoundException("Task does not exist.");
                await renderer.RenderTaskAsync(snapshot);
                if (await tasks.GetResultAsync(taskId, token) is { } result)
                {
                    await renderer.RenderResultAsync(result, includeStatus: false);
                    state.MarkDisplayed(taskId);
                }
                break;
            case "/wait":
                var waitingId = state.ResolveTask(argument);
                var waited = await tasks.WaitForResultAsync(waitingId, token);
                await renderer.RenderResultAsync(waited, includeBody: !state.Displayed.Contains(waitingId));
                state.MarkDisplayed(waitingId);
                break;
            case "/cancel":
                var cancelled = await tasks.CancelAsync(state.ResolveTask(argument), token);
                await renderer.WriteLineAsync($"Task: {cancelled.Id.Value} [{cancelled.Status}]. Cancellation is confirmed only after execution stops.");
                break;
            default:
                throw new ArgumentException("Unknown command or invalid arguments.");
        }
    }

    private static int? ParseHistoryCount(string? argument)
    {
        if (string.Equals(argument, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (argument is null)
        {
            return 1;
        }
        if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1)
        {
            throw new ArgumentException("Use /history, /history COUNT (positive integer), or /history all.");
        }
        return count;
    }

    private async Task ShowSessionAsync(SessionInfo session, CancellationToken token) =>
        await renderer.RenderSessionAsync(session, state.Current, await GetSessionTaskAsync(session, token));

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

    private async Task ShowSessionListAsync(CancellationToken token)
    {
        var visible = await sessions.ListAsync(token);
        if (visible.Count == 0)
        {
            await renderer.WriteLineAsync("[No visible sessions. Use /new NAME.]");
        }
        var rows = new List<(SessionInfo Session, TaskInfo? Task)>();
        for (var index = 0; index < visible.Count; index++)
        {
            var session = visible[index];
            rows.Add((session, await GetSessionTaskAsync(session, token)));
        }
        if (rows.Count != 0)
        {
            await renderer.RenderSessionListAsync(rows, state.Current);
        }
    }

    private async Task<SessionId> ResolveSessionIndexAsync(string value, CancellationToken token)
    {
        var visible = await sessions.ListAsync(token);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index < 1 || index > visible.Count)
        {
            throw new ArgumentException("Invalid session index. Use /sessions to view current indices.");
        }
        return visible[index - 1].Id;
    }
}
