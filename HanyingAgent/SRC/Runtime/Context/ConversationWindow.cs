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
        string systemInstruction,
        int maxCharacters,
        int maxTurns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemInstruction);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTurns);
        if (current.Content is not UserConversationContent user)
        {
            throw new ArgumentException("Current message must be a user input.", nameof(current));
        }
        long characters = (long)systemInstruction.Length + user.Text.Length;
        if (characters > maxCharacters)
        {
            throw new InvalidOperationException("System instruction and current input exceed the character budget.");
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
                    var pairCharacters = (long)previousUser.Text.Length + pending.Text!.Length;
                    if (characters + pairCharacters > maxCharacters)
                    {
                        reason = "CharacterBudget";
                        stop = true;
                        break;
                    }
                    selected.InsertRange(0,
                        [new ModelUserMessage(previousUser.Text), new ModelAssistantMessage(pending.Text, [])]);
                    characters += pairCharacters;
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
            "History window selected: {SelectedTurns} turns, {ScannedMessages} scanned, {OmittedMessages} omitted, " +
            "{InputCharacters} characters; {SelectionReason}",
            turns, scanned, scanned - turns * 2, characters, reason);
        return selected.ToArray();
    }
}
