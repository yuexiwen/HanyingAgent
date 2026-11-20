using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Responses;
using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Runtime.Agent.Capabilities.Conversation;

public interface IConversationStore
{
    ConversationMessage Append(
        AppendConversationMessageRequest request, UserId owner, DateTimeOffset now, CancellationToken cancellationToken);
    ConversationMessage? Get(MessageId id, UserId owner, CancellationToken cancellationToken);
    ConversationMessagePage List(
        ListConversationMessagesRequest request, UserId owner, CancellationToken cancellationToken);
}
