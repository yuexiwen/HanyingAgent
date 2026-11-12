using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Runtime.Users;

namespace HanyingAgent.Runtime.Conversation;

public sealed class LocalConversationManager(
    IConversationStore store,
    LocalUserContext identity,
    TimeProvider clock) : IConversationManager
{
    public async Task<ConversationMessage> AppendAsync(
        AppendConversationMessageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId.Value);
        if (request.TaskId is not null)
        {
            throw new NotSupportedException("Task association requires the future task manager.");
        }
        var content = SnapshotContent(request.Content);
        return store.Append(request with { Content = content }, await identity.GetUserIdAsync(cancellationToken),
            clock.GetUtcNow(), cancellationToken);
    }

    public async Task<ConversationMessage?> GetAsync(MessageId messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId.Value);
        return store.Get(messageId, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    public async Task<ConversationMessagePage> ListBySessionAsync(
        ListConversationMessagesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.PageSize);
        if (request.PageSize > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Page size must not exceed 200.");
        }
        return store.List(request, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    internal static ConversationContent SnapshotContent(ConversationContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        switch (content)
        {
            case UserConversationContent user:
                ArgumentException.ThrowIfNullOrWhiteSpace(user.Text);
                return user;
            case AssistantConversationContent assistant:
                ArgumentNullException.ThrowIfNull(assistant.ToolCalls);
                if (assistant.ToolCalls.Count != 0)
                {
                    throw new NotSupportedException("Tool history is not supported in this text-only phase.");
                }
                if (assistant.FinishReason is { } reason && !Enum.IsDefined(reason))
                {
                    throw new ArgumentOutOfRangeException(nameof(content), "Invalid finish reason.");
                }
                if (string.IsNullOrWhiteSpace(assistant.Text) && assistant.FinishReason is null)
                {
                    throw new ArgumentException("Assistant history requires text or a finish reason.", nameof(content));
                }
                return assistant with { ToolCalls = Array.AsReadOnly(assistant.ToolCalls.ToArray()) };
            default:
                throw new NotSupportedException("Only user and text-only assistant history is supported.");
        }
    }
}
