using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations;
using HanyingAgent.Contracts.Sessions.Operations.Requests;
using HanyingAgent.Runtime.Users;

namespace HanyingAgent.Runtime.Sessions;

public sealed class LocalSessionManager(
    ISessionStore store,
    LocalUserContext identity,
    TimeProvider clock) : ISessionManager
{
    public async Task<SessionInfo> CreateAsync(CreateSessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        var owner = await identity.GetUserIdAsync(cancellationToken);
        if (owner != request.UserId)
        {
            throw new UnauthorizedAccessException("Cannot create a session for another user.");
        }
        return store.Add(owner, request.Name, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<SessionInfo?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId.Value);
        return store.Get(sessionId, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken cancellationToken) =>
        store.List(await identity.GetUserIdAsync(cancellationToken), cancellationToken);

    public async Task<SessionInfo> CloseAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId.Value);
        return store.Close(sessionId, await identity.GetUserIdAsync(cancellationToken), clock.GetUtcNow(), cancellationToken);
    }

    public async Task<IDisposable> BeginTurnAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId.Value);
        return store.BeginTurn(sessionId, await identity.GetUserIdAsync(cancellationToken), cancellationToken);
    }
}
