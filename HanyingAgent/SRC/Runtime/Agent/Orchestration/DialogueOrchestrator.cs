using System.Text.Json;
using HanyingAgent.Contracts.Agent.Context.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Agent.Execution.Models;
using HanyingAgent.Diagnostics;
using HanyingAgent.Runtime.Agent.Context;
using HanyingAgent.Runtime.Agent.Execution;
using HanyingAgent.Runtime.Management.Sessions;
using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Runtime.Management.Users;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Runtime.Agent.Orchestration;

public sealed class DialogueOrchestrator(
    LocalSessionManager sessions,
    IConversationManager conversations,
    ConversationWindow window,
    IWorkingMemoryManager memory,
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
            phase = "Execute";
            var outcome = await executor.ExecuteAsync(new BuildContextRequest(
                request.SystemInstruction, messages, memories, request.Options, request.MaxInputCharacters),
                cancellationToken, waitingForModel);
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
