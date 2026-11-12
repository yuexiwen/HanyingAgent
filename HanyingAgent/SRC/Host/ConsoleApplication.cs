using System.Text.Json;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Host.Configuration;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Diagnostics;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HanyingAgent.Host;

public sealed class ConsoleApplication(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentSettings> options,
    ISessionManager sessions,
    IConversationManager conversations,
    LocalUserContext identity,
    ILogger<ConsoleApplication> logger,
    TextReader input,
    TextWriter output)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await output.WriteLineAsync("HanyingAgent - local in-memory conversations. /new NAME, /sessions, /use ID, /history [CURSOR], /close, /exit. Ctrl+C cancels and exits.");
        SessionId? current = null;
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
                            default:
                                throw new ArgumentException("Unknown command or invalid arguments.");
                        }
                        continue;
                    }
                    var sessionId = RequireCurrent(current);
                    using var scope = scopeFactory.CreateScope();
                    var settings = options.Value;
                    var outcome = await scope.ServiceProvider.GetRequiredService<DialogueCoordinator>()
                        .RespondAsync(new DialogueRequest(sessionId, line, settings.SystemInstruction,
                            settings.MaxInputCharacters, settings.MaxHistoryTurns,
                            new ModelOptions(Temperature: settings.Temperature, MaxOutputTokens: settings.MaxOutputTokens)),
                            cancellationToken);
                    if (!string.IsNullOrWhiteSpace(outcome.Text))
                    {
                        await output.WriteLineAsync($"Assistant> {outcome.Text}");
                    }
                    if (outcome.Conclusion != ExecutionConclusion.Delivered)
                    {
                        var status = outcome.Conclusion switch
                        {
                            ExecutionConclusion.Incomplete => "Output truncated; this is not a complete answer.",
                            ExecutionConclusion.Filtered => "Output was filtered.",
                            ExecutionConclusion.Refused => "The model refused the request.",
                            ExecutionConclusion.UnsupportedAction => "Tool execution is not supported.",
                            ExecutionConclusion.Unconfirmed => "Completion is unknown; do not treat this as a complete answer.",
                            _ => throw new InvalidOperationException("Unsupported execution conclusion.")
                        };
                        await output.WriteLineAsync($"[{status}]");
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await output.WriteLineAsync("[Request timed out or dependency cancelled. See logs; you can try another question.]");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or
                    ArgumentException or InvalidOperationException or NotSupportedException or
                    KeyNotFoundException or UnauthorizedAccessException or IOException)
                {
                    if (line.TrimStart().StartsWith('/') || current is null)
                    {
                        logger.LogWarning(LogEvents.SessionCommandFailed, exception, "Session command failed");
                    }
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
}
