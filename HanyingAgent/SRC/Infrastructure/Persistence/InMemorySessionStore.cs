using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Users.Models;
using HanyingAgent.Runtime.Management.Sessions;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly InMemoryAgentState _state;

    internal InMemorySessionStore(InMemoryAgentState state) => _state = state;

    public SessionInfo Add(UserId owner, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_state.Users.ContainsKey(owner))
            {
                throw new KeyNotFoundException("Session owner does not exist.");
            }
            var session = new SessionInfo(new SessionId(Guid.NewGuid().ToString("N")), owner, name,
                SessionStatus.Idle, null, now, now);
            _state.Sessions.Add(session.Id, session);
            _state.History.Add(session.Id, []);
            return session;
        }
    }

    public SessionInfo? Get(SessionId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _state.Sessions.ContainsKey(id) ? _state.RequireSession(id, owner) : null;
        }
    }

    public IReadOnlyList<SessionInfo> List(UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _state.Sessions.Values.Where(session => session.UserId == owner && !_state.HiddenSessions.Contains(session.Id))
                .OrderBy(session => session.CreatedAt).ThenBy(session => session.Id.Value, StringComparer.Ordinal).ToArray();
        }
    }

    public SessionInfo Close(SessionId id, UserId owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _state.RequireSession(id, owner);
            if (session.Status == SessionStatus.Active)
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
            _state.Sessions[id] = closed;
            return closed;
        }
    }

    public void Hide(SessionId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.RequireSession(id, owner);
            _state.HiddenSessions.Add(id);
        }
    }

    public IDisposable BeginTurn(SessionId id, UserId owner, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.RequireWritableSession(id, owner);
            _state.Sessions[id] = _state.Sessions[id] with { Status = SessionStatus.Active };
            return new TurnLease(_state, id);
        }
    }

    private sealed class TurnLease(InMemoryAgentState state, SessionId id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (state.Gate)
                {
                    var session = state.Sessions[id];
                    if (session.Status == SessionStatus.Active && session.CurrentTaskId is null)
                    {
                        state.Sessions[id] = session with { Status = SessionStatus.Idle };
                    }
                }
            }
        }
    }
}
