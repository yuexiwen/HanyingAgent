using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Sessions.Operations.Requests;

namespace HanyingAgent.Contracts.Sessions.Operations;

public interface ISessionManager
{
    Task<SessionInfo> CreateAsync(
        CreateSessionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns null when the session does not exist.
    /// </summary>
    Task<SessionInfo?> GetAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionInfo>> ListAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Closes an idle session, returns an already closed session unchanged,
    /// and rejects a session that still has an unfinished task.
    /// </summary>
    Task<SessionInfo> CloseAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
