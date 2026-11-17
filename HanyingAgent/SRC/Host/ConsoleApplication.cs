using System.Text.Json;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Diagnostics;
using HanyingAgent.Host.Console;
using HanyingAgent.Capabilities.Conversation;
using HanyingAgent.Runtime.Tasks;
using HanyingAgent.Runtime.Users;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Host;

public sealed class ConsoleApplication(
    LocalTaskManager tasks,
    ISessionManager sessions,
    ConversationHistoryReader history,
    LocalUserContext identity,
    ILogger<ConsoleApplication> logger,
    TextReader input,
    TextWriter output,
    ConsoleLogControl consoleLogs)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var state = new ConsoleState();
        var renderer = new ConsoleRenderer(output);
        var commands = new ConsoleCommandHandler(tasks, sessions, history, state, renderer, consoleLogs);
        Task<string?>? read = null;
        using var notifications = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await renderer.ShowHelpAsync();
        try
        {
            var userId = await identity.GetUserIdAsync(cancellationToken);
            state.Current = (await sessions.CreateAsync(new CreateSessionRequest(userId, "Default"), cancellationToken)).Id;
            await renderer.WriteLineAsync($"Session: {state.Current.Value.Value} (Default)");
            while (!cancellationToken.IsCancellationRequested)
            {
                var ready = state.Pending.Where(item => item.Value.Completion.IsCompleted).ToArray();
                var hasNotifications = ready.Any(item => !state.Displayed.Contains(item.Key));
                if (hasNotifications && read is not null)
                {
                    await renderer.WriteLineAsync();
                }
                foreach (var (id, update) in ready)
                {
                    state.Pending.Remove(id);
                    try
                    {
                        var result = await update.Completion;
                        if (state.Displayed.Contains(id))
                        {
                            continue;
                        }
                        if (update.SessionId == state.Current)
                        {
                            await renderer.RenderResultAsync(result);
                            state.MarkDisplayed(id);
                        }
                        else
                        {
                            state.Unread[id] = new ConsoleState.SessionResult(update.SessionId, result);
                            await renderer.RenderOtherSessionAsync(update.SessionId, result);
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
                        await renderer.WriteLineAsync($"[Result notification failed for task {id.Value}. Use /task {id.Value}. See logs.]");
                    }
                }
                if (read is null || hasNotifications)
                {
                    await renderer.ShowPromptAsync();
                }
                // Keep one read in flight while results wake the output loop; never read a second line concurrently.
                read ??= Task.Run(() => input.ReadLine(), CancellationToken.None);
                var completed = await Task.WhenAny([read, .. state.Pending.Values.Select(item => item.Completion)])
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
                    await commands.HandleAsync(line, userId, notifications.Token, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await renderer.WriteLineAsync("[Request timed out or dependency cancelled. See logs; you can try another question.]");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or
                    ArgumentException or InvalidOperationException or NotSupportedException or
                    KeyNotFoundException or UnauthorizedAccessException or IOException)
                {
                    logger.LogWarning(LogEvents.SessionCommandFailed, exception, "Console operation failed");
                    await renderer.WriteLineAsync("[Request failed. See logs; you can try another question.]");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await renderer.WriteLineAsync("[Cancelled. Exiting.]");
        }
        finally
        {
            // Stop result waits, not business tasks; Host owns task cancellation and shutdown.
            await notifications.CancelAsync();
            foreach (var (id, update) in state.Pending)
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
}
