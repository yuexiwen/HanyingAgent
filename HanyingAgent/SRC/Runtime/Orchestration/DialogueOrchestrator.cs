using System.Text.Json;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Execution.Models;
using HanyingAgent.Diagnostics;
using HanyingAgent.Runtime.Context;
using HanyingAgent.Runtime.Execution;
using HanyingAgent.Runtime.Sessions;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Runtime.Users;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Runtime.Orchestration;

public sealed class DialogueOrchestrator(
    LocalSessionManager sessions,
    IConversationManager conversations,
    ConversationWindow window,
    IWorkingMemoryManager memory,
    IContextBuilder contextBuilder,
    DirectExecutor executor,
    LocalUserContext identity,
    ILogger<DialogueOrchestrator> logger)
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
            var current = await SaveUserAsync(request, taskId, cancellationToken);
            phase = "SelectHistory";
            var messages = await SelectHistoryAsync(current, request, cancellationToken);
            phase = "ReadMemory";
            var memories = await ReadMemoryAsync(cancellationToken);
            phase = "BuildContext";
            var invocation = await BuildContextAsync(request, messages, memories, cancellationToken);
            phase = "WaitForModel";
            NotifyWaitingForModel(waitingForModel, cancellationToken);
            phase = "Execute";
            var outcome = await executor.ExecuteAsync(invocation, cancellationToken);
            phase = "SaveAssistant";
            await SaveAssistantAsync(sessionId, taskId, outcome, cancellationToken);
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

    private async Task<ConversationMessage> SaveUserAsync(
        DialogueRequest request, TaskId? taskId, CancellationToken cancellationToken)
    {
        var current = await conversations.AppendAsync(new AppendConversationMessageRequest(
            new MessageId(Guid.NewGuid().ToString("N")), request.SessionId, taskId,
            new UserConversationContent(request.Input)), cancellationToken);
        logger.LogInformation(LogEvents.DialogueInputRecorded,
            "Dialogue input recorded: {MessageId}, sequence {Sequence}", current.Id.Value, current.Sequence);
        return current;
    }

    private Task<IReadOnlyList<ModelMessage>> SelectHistoryAsync(
        ConversationMessage current, DialogueRequest request, CancellationToken cancellationToken) =>
        window.SelectAsync(current, request.MaxHistoryTurns, cancellationToken);

    private async Task<IReadOnlyList<MemoryItem>> ReadMemoryAsync(CancellationToken cancellationToken)
    {
        var userId = await identity.GetUserIdAsync(cancellationToken);
        var owner = new MemoryOwner(MemoryScope.User, userId: userId);
        var memories = await memory.ListRecentAsync(new MemoryReadRequest(owner, Limit: 20), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (memories is null || memories.Any(item => item is null || item.Owner != owner))
        {
            throw new InvalidOperationException("Working memory returned invalid data or a different owner.");
        }
        return memories;
    }

    private async Task<ModelInvocation> BuildContextAsync(
        DialogueRequest request, IReadOnlyList<ModelMessage> messages,
        IReadOnlyList<MemoryItem> memories, CancellationToken cancellationToken)
    {
        var invocation = await contextBuilder.BuildAsync(new BuildContextRequest(
            request.SystemInstruction, messages, memories, request.Options, request.MaxInputCharacters), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return invocation;
    }

    private static void NotifyWaitingForModel(Action? waitingForModel, CancellationToken cancellationToken)
    {
        waitingForModel?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task SaveAssistantAsync(
        SessionId sessionId, TaskId? taskId, ExecutionOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Conclusion == ExecutionConclusion.UnsupportedAction)
        {
            logger.LogWarning(LogEvents.DialogueResponseNotRecorded,
                "Unsupported action was not recorded as text-only assistant history");
            return;
        }
        // A response already exists. Saving failure must not trigger a second model invocation.
        var assistant = await conversations.AppendAsync(new AppendConversationMessageRequest(
            new MessageId(Guid.NewGuid().ToString("N")), sessionId, taskId,
            new AssistantConversationContent(outcome.Text, [], outcome.FinishReason, outcome.ProviderFinishReason)),
            cancellationToken);
        logger.LogInformation(LogEvents.DialogueResponseRecorded,
            "Dialogue response recorded: {MessageId}, sequence {Sequence}, {Conclusion}",
            assistant.Id.Value, assistant.Sequence, outcome.Conclusion);
    }
}
