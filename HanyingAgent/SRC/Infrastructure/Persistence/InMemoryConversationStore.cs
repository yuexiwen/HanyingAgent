using System.Text;
using System.Text.Json;
using HanyingAgent.Capabilities.Conversation;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly InMemoryAgentState _state;

    internal InMemoryConversationStore(InMemoryAgentState state) => _state = state;

    public ConversationMessage Append(
        AppendConversationMessageRequest request, UserId owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _state.RequireSession(request.SessionId, owner);
            var content = LocalConversationManager.SnapshotContent(request.Content);
            if (_state.Messages.TryGetValue(request.Id, out var existing))
            {
                if (existing.SessionId != request.SessionId || existing.TaskId != request.TaskId ||
                    !SameContent(existing.Content, content))
                {
                    throw new InvalidOperationException("Message ID was already used with a different payload.");
                }
                return existing;
            }
            if (session.Status == SessionStatus.Closed)
            {
                throw new InvalidOperationException("Session is closed.");
            }
            if (request.TaskId is { } taskId)
            {
                var task = _state.RequireTask(taskId, owner);
                if (task.SessionId != session.Id || session.CurrentTaskId != taskId || InMemoryAgentState.IsTerminal(task.Status))
                {
                    throw new InvalidOperationException("Message task is not the active task of this session.");
                }
            }
            else if (session.CurrentTaskId is not null)
            {
                throw new InvalidOperationException("Messages in a task-owned session require its TaskId.");
            }
            var history = _state.History[request.SessionId];
            var message = new ConversationMessage(request.Id, request.SessionId, request.TaskId,
                checked((long)history.Count + 1), content, now);
            _state.Messages.Add(message.Id, message);
            history.Add(message);
            _state.Sessions[session.Id] = session with
            {
                LastActiveAt = now > session.LastActiveAt ? now : session.LastActiveAt
            };
            return message;
        }
    }

    public ConversationMessage? Get(MessageId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_state.Messages.TryGetValue(id, out var message))
            {
                return null;
            }
            _state.RequireSession(message.SessionId, owner);
            return message;
        }
    }

    public ConversationMessagePage List(
        ListConversationMessagesRequest request, UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.RequireSession(request.SessionId, owner);
            var history = _state.History[request.SessionId];
            var before = (long)history.Count + 1;
            if (request.ContinuationToken is { } token)
            {
                Cursor? cursor;
                try
                {
                    cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(token));
                }
                catch (Exception exception) when (exception is FormatException or JsonException)
                {
                    throw new ArgumentException("Invalid conversation cursor.", nameof(request), exception);
                }
                if (cursor is null || cursor.Session != request.SessionId.Value ||
                    cursor.Before <= 0 || cursor.Before > before)
                {
                    throw new ArgumentException("Cursor does not belong to this session or is invalid.", nameof(request));
                }
                before = cursor.Before;
            }
            var end = checked((int)(before - 1));
            var start = Math.Max(0, end - request.PageSize);
            var items = history.GetRange(start, end - start).ToArray();
            var next = start == 0 ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new Cursor(request.SessionId.Value, start + 1L))));
            return new ConversationMessagePage(items, next);
        }
    }

    private static bool SameContent(ConversationContent first, ConversationContent second) =>
        (first, second) switch
        {
            (AssistantConversationContent a, AssistantConversationContent b) =>
                a.Text == b.Text && a.FinishReason == b.FinishReason &&
                a.ProviderFinishReason == b.ProviderFinishReason && a.ToolCalls.SequenceEqual(b.ToolCalls),
            _ => first == second
        };

    private sealed record Cursor(string Session, long Before);
}
