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
        await output.WriteLineAsync("HanyingAgent - local in-memory tasks. /new NAME, /sessions, /use ID, /history [CURSOR], /tasks [CURSOR], /task [ID], /wait [ID], /cancel [ID], /close, /exit. Task commands default to the last submitted task. Exit cancels running tasks.");
        SessionId? current = null;
        TaskId? lastTask = null;
        try
        {
            var userId = await identity.GetUserIdAsync(cancellationToken);
            current = (await sessions.CreateAsync(new CreateSessionRequest(userId, "Default"), cancellationToken)).Id;
            await output.WriteLineAsync($"Session: {current.Value.Value} (Default)");
            while (!cancellationToken.IsCancellationRequested)
            {
                await output.WriteAsync("You> ");
                await output.FlushAsync();
                // Console's synchronous reader cannot interrupt a pending read; shutdown stops waiting for it.
                var line = await Task.Run(() => input.ReadLine(), CancellationToken.None)
                    .WaitAsync(cancellationToken);
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
                                await output.WriteLineAsync($"Session: {created.Id.Value} ({created.Name})");
                                break;
                            case "/sessions" when argument is null:
                                foreach (var session in await sessions.ListAsync(cancellationToken))
                                {
                                    await output.WriteLineAsync($"{(session.Id == current ? "*" : " ")} {session.Id.Value} {session.Name} [{session.Status}]");
                                }
                                break;
                            case "/use":
                                ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                                var selected = await sessions.GetAsync(new SessionId(argument), cancellationToken)
                                    ?? throw new KeyNotFoundException("Session does not exist.");
                                current = selected.Id;
                                await output.WriteLineAsync($"Session: {selected.Id.Value} ({selected.Name}) [{selected.Status}]");
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
                                    await RenderResultAsync(result);
                                }
                                break;
                            case "/wait":
                                await RenderResultAsync(await tasks.WaitForResultAsync(
                                    ResolveTask(argument, lastTask), cancellationToken));
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
                    await output.WriteLineAsync($"Task accepted: {accepted.Id.Value}. Use /wait or /task to view the result.");
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
    }

    private static SessionId RequireCurrent(SessionId? current) =>
        current ?? throw new InvalidOperationException("No session selected. Use /new NAME or /use ID.");

    private static TaskId ResolveTask(string? argument, TaskId? lastTask) =>
        argument is not null ? new TaskId(argument) :
        lastTask ?? throw new InvalidOperationException("No task submitted. Specify a TaskId.");

    private async Task RenderResultAsync(TaskResult result)
    {
        await output.WriteLineAsync($"Task: {result.TaskId.Value} [{result.Status}]");
        if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
        {
            await output.WriteLineAsync($"Assistant> {result.FinalAnswer}");
        }
        if (result.Error is { } error)
        {
            await output.WriteLineAsync($"[{error}]");
        }
    }
}
