using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Runtime.Conversation;

public interface IConversationStore
{
    ConversationMessage Append(
        AppendConversationMessageRequest request, UserId owner, DateTimeOffset now, CancellationToken cancellationToken);
    ConversationMessage? Get(MessageId id, UserId owner, CancellationToken cancellationToken);
    ConversationMessagePage List(
        ListConversationMessagesRequest request, UserId owner, CancellationToken cancellationToken);
}
