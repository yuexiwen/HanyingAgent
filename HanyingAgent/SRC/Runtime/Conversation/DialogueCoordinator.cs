using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Diagnostics;
using HanyingAgent.Runtime.Context;
using HanyingAgent.Runtime.Execution;
using HanyingAgent.Runtime.Sessions;
using HanyingAgent.Contracts.Tasks.Models;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Runtime.Conversation;

public sealed class DialogueCoordinator(
    LocalSessionManager sessions,
    IConversationManager conversations,
    ConversationWindow window,
    DirectExecutor executor,
    ILogger<DialogueCoordinator> logger)
{
    public async Task<ExecutionOutcome> RespondAsync(
        DialogueRequest request, CancellationToken cancellationToken) =>
        await RespondCoreAsync(request, null, null, cancellationToken);

    internal Task<ExecutionOutcome> RespondTaskAsync(
        DialogueRequest request, TaskId taskId, Action waitingForModel, CancellationToken cancellationToken) =>
        RespondCoreAsync(request, taskId, waitingForModel, cancellationToken);

    private async Task<ExecutionOutcome> RespondCoreAsync(
        DialogueRequest request, TaskId? taskId, Action? waitingForModel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        var sessionId = request.SessionId;
        var phase = "AcquireSession";
        try
        {
            using var lease = taskId is null ? await sessions.BeginTurnAsync(sessionId, cancellationToken) : null;
            using var scope = taskId is null ? logger.BeginScope("SessionId:{SessionId}", sessionId.Value) : null;
            phase = "SaveUser";
            var current = await conversations.AppendAsync(new AppendConversationMessageRequest(
                new MessageId(Guid.NewGuid().ToString("N")), sessionId, taskId, new UserConversationContent(request.Input)),
                cancellationToken);
            logger.LogInformation(LogEvents.DialogueInputRecorded,
                "Dialogue input recorded: {MessageId}, sequence {Sequence}", current.Id.Value, current.Sequence);
            phase = "SelectHistory";
            var messages = await window.SelectAsync(current, request.SystemInstruction,
                request.MaxInputCharacters, request.MaxHistoryTurns, cancellationToken);
            phase = "Execute";
            waitingForModel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await executor.ExecuteAsync(new BuildContextRequest(
                request.SystemInstruction, messages, [], request.Options),
                cancellationToken);
            phase = "SaveAssistant";
            if (outcome.Conclusion == ExecutionConclusion.UnsupportedAction)
            {
                logger.LogWarning(LogEvents.DialogueResponseNotRecorded,
                    "Unsupported action was not recorded as text-only assistant history");
                return outcome;
            }
            // A response already exists. Saving failure must not trigger a second model invocation.
            var assistant = await conversations.AppendAsync(new AppendConversationMessageRequest(
                new MessageId(Guid.NewGuid().ToString("N")), sessionId, taskId,
                new AssistantConversationContent(outcome.Text, [], outcome.FinishReason, outcome.ProviderFinishReason)),
                cancellationToken);
            logger.LogInformation(LogEvents.DialogueResponseRecorded,
                "Dialogue response recorded: {MessageId}, sequence {Sequence}, {Conclusion}",
                assistant.Id.Value, assistant.Sequence, outcome.Conclusion);
            return outcome;
        }
        catch (OperationCanceledException) when (phase != "Execute")
        {
            logger.LogInformation(LogEvents.DialogueCancelled,
                "Dialogue cancelled during {Phase} for session {SessionId}", phase, sessionId.Value);
            throw;
        }
        catch (Exception exception) when (phase != "Execute" && exception is
            (ArgumentException or InvalidOperationException or KeyNotFoundException or
             UnauthorizedAccessException or NotSupportedException or JsonException or IOException))
        {
            logger.LogError(LogEvents.DialogueFailed, exception,
                "Dialogue failed during {Phase} for session {SessionId}. No automatic retry.", phase, sessionId.Value);
            throw;
        }
    }
}
