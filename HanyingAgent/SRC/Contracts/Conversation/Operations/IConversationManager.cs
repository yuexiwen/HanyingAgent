using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;

namespace HanyingAgent.Contracts.Conversation.Operations;

public interface IConversationManager
{
    /// <summary>
    /// Appends once for a stable message ID, returns a matching retry unchanged,
    /// and rejects reuse of that ID for a different payload.
    /// </summary>
    Task<ConversationMessage> AppendAsync(
        AppendConversationMessageRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns null when the message does not exist.
    /// </summary>
    Task<ConversationMessage?> GetAsync(
        MessageId messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the newest page first, ordered by sequence ascending within each page,
    /// with a continuation token for older messages.
    /// </summary>
    Task<ConversationMessagePage> ListBySessionAsync(
        ListConversationMessagesRequest request,
        CancellationToken cancellationToken);
}
