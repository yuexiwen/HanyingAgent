using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations;
using HanyingAgent.Contracts.Agent.Capabilities.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Management.Sessions.Models;

namespace HanyingAgent.Runtime.Agent.Capabilities.Conversation;

public sealed class ConversationHistoryReader(IConversationManager conversations)
{
    public async Task<IReadOnlyList<ConversationMessage>> ReadAsync(
        SessionId sessionId, int? maxTurns, CancellationToken token)
    {
        if (maxTurns is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), "Turn count must be positive.");
        }
        var selected = new List<ConversationMessage>();
        string? cursor = null;
        var turns = 0;
        do
        {
            var page = await conversations.ListBySessionAsync(
                new ListConversationMessagesRequest(sessionId, PageSize: 200, ContinuationToken: cursor), token);
            // Older pages are anchored by sequence, so new appends do not shift the selected history.
            for (var index = page.Items.Count - 1; index >= 0; index--)
            {
                var message = page.Items[index];
                selected.Add(message);
                if (message.Content is UserConversationContent && ++turns == maxTurns)
                {
                    break;
                }
            }
            cursor = page.ContinuationToken;
        }
        while (cursor is not null && (maxTurns is null || turns < maxTurns));
        selected.Reverse();
        return selected.ToArray();
    }
}
