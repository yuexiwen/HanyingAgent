using System.Text;
using System.Text.Json;
using HanyingAgent.Contracts.Conversation.Models;
using HanyingAgent.Contracts.Conversation.Operations.Requests;
using HanyingAgent.Contracts.Conversation.Operations.Responses;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Runtime.Conversation;
using HanyingAgent.Runtime.Sessions;
using HanyingAgent.Runtime.Users;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed class InMemoryDialogueStore : IUserStore, ISessionStore, IConversationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<UserId, UserInfo> _users = new();
    private readonly Dictionary<SessionId, SessionInfo> _sessions = new();
    private readonly Dictionary<MessageId, ConversationMessage> _messages = new();
    private readonly Dictionary<SessionId, List<ConversationMessage>> _history = new();
    private readonly HashSet<SessionId> _busy = new();

    public UserInfo Add(string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = new UserInfo(new UserId(Guid.NewGuid().ToString("N")), displayName, now, now);
            _users.Add(user.Id, user);
            return user;
        }
    }

    public UserInfo? Get(UserId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _users.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<UserInfo> List(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _users.Values.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id.Value, StringComparer.Ordinal).ToArray();
        }
    }

    public UserInfo Update(UserId id, string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = _users.TryGetValue(id, out var existing)
                ? existing : throw new KeyNotFoundException("User does not exist.");
            var updated = user with { DisplayName = displayName, UpdatedAt = now > user.UpdatedAt ? now : user.UpdatedAt };
            _users[id] = updated;
            return updated;
        }
    }

    public SessionInfo Add(UserId owner, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_users.ContainsKey(owner))
            {
                throw new KeyNotFoundException("Session owner does not exist.");
            }
            var session = new SessionInfo(new SessionId(Guid.NewGuid().ToString("N")), owner, name,
                SessionStatus.Idle, null, now, now);
            _sessions.Add(session.Id, session);
            _history.Add(session.Id, []);
            return session;
        }
    }

    public SessionInfo? Get(SessionId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _sessions.ContainsKey(id) ? RequireSession(id, owner) : null;
        }
    }

    public IReadOnlyList<SessionInfo> List(UserId owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _sessions.Values.Where(session => session.UserId == owner)
                .OrderBy(session => session.CreatedAt).ThenBy(session => session.Id.Value, StringComparer.Ordinal).ToArray();
        }
    }

    public SessionInfo Close(SessionId id, UserId owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = RequireSession(id, owner);
            if (_busy.Contains(id) || session.Status == SessionStatus.Active)
            {
                throw new InvalidOperationException("Cannot close a session with an active execution.");
            }
            if (session.Status == SessionStatus.Closed)
            {
                return session;
            }
            var closed = session with
            {
                Status = SessionStatus.Closed,
                LastActiveAt = now > session.LastActiveAt ? now : session.LastActiveAt
            };
            _sessions[id] = closed;
            return closed;
        }
    }

    public IDisposable BeginTurn(SessionId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireWritableSession(id, owner);
            if (!_busy.Add(id))
            {
                throw new InvalidOperationException("This session already has an execution in progress.");
            }
            return new TurnLease(this, id);
        }
    }

    public ConversationMessage Append(
        AppendConversationMessageRequest request, UserId owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = RequireSession(request.SessionId, owner);
            var content = LocalConversationManager.SnapshotContent(request.Content);
            if (_messages.TryGetValue(request.Id, out var existing))
            {
                if (existing.SessionId != request.SessionId || existing.TaskId != request.TaskId ||
                    !SameContent(existing.Content, content))
                {
                    throw new InvalidOperationException("Message ID was already used with a different payload.");
                }
                return existing;
            }
            RequireWritableSession(request.SessionId, owner);
            var history = _history[request.SessionId];
            var message = new ConversationMessage(request.Id, request.SessionId, request.TaskId,
                checked((long)history.Count + 1), content, now);
            _messages.Add(message.Id, message);
            history.Add(message);
            _sessions[session.Id] = session with
            {
                LastActiveAt = now > session.LastActiveAt ? now : session.LastActiveAt
            };
            return message;
        }
    }

    public ConversationMessage? Get(MessageId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_messages.TryGetValue(id, out var message))
            {
                return null;
            }
            RequireSession(message.SessionId, owner);
            return message;
        }
    }

    public ConversationMessagePage List(
        ListConversationMessagesRequest request, UserId owner, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireSession(request.SessionId, owner);
            var history = _history[request.SessionId];
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

    private SessionInfo RequireSession(SessionId id, UserId owner)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            throw new KeyNotFoundException("Session does not exist.");
        }
        if (session.UserId != owner)
        {
            throw new UnauthorizedAccessException("Session belongs to another user.");
        }
        return session;
    }

    private void RequireWritableSession(SessionId id, UserId owner)
    {
        if (RequireSession(id, owner).Status != SessionStatus.Idle)
        {
            throw new InvalidOperationException("Session is not open for new dialogue.");
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

    private sealed class TurnLease(InMemoryDialogueStore store, SessionId id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (store._gate)
                {
                    store._busy.Remove(id);
                }
            }
        }
    }
}
