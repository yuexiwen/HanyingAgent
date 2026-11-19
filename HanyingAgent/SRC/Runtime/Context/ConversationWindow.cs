using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HanyingAgent.Runtime.Context;

public sealed class ConversationWindow(IConversationManager conversations, ILogger<ConversationWindow> logger)
{
    private const int MaxScannedMessages = 200;

    public async Task<IReadOnlyList<ModelMessage>> SelectAsync(
        ConversationMessage current,
        int maxTurns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTurns);
        if (current.Content is not UserConversationContent user)
        {
            throw new ArgumentException("Current message must be a user input.", nameof(current));
        }
        var selected = new List<ModelMessage> { new ModelUserMessage(user.Text) };
        AssistantConversationContent? pending = null;
        string? cursor = null;
        var turns = 0;
        var scanned = 0;
        var stop = false;
        var reason = "HistoryExhausted";
        do
        {
            var page = await conversations.ListBySessionAsync(
                new ListConversationMessagesRequest(current.SessionId, 20, cursor), cancellationToken);
            foreach (var message in page.Items.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (message.Sequence >= current.Sequence)
                {
                    continue;
                }
                scanned++;
                if (message.Content is UserConversationContent previousUser && pending is not null)
                {
                    selected.InsertRange(0,
                        [new ModelUserMessage(previousUser.Text), new ModelAssistantMessage(pending.Text, [])]);
                    turns++;
                    pending = null;
                    if (turns == maxTurns)
                    {
                        reason = "TurnLimit";
                        stop = true;
                        break;
                    }
                }
                else
                {
                    pending = message.Content is AssistantConversationContent
                        { FinishReason: ModelFinishReason.Stop, ToolCalls.Count: 0 } assistant &&
                        !string.IsNullOrWhiteSpace(assistant.Text) ? assistant : null;
                }
                if (scanned == MaxScannedMessages)
                {
                    reason = "ScanLimit";
                    stop = true;
                    break;
                }
            }
            cursor = page.ContinuationToken;
        } while (!stop && cursor is not null);

        logger.LogInformation(LogEvents.HistoryWindowSelected,
            "History candidates loaded: {CandidateTurns} turns, {ScannedMessages} scanned, {OmittedMessages} omitted; " +
            "{SelectionReason}. Context owns final budget selection.",
            turns, scanned, scanned - turns * 2, reason);
        return selected.ToArray();
    }
}
