using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Sessions.Operations.Requests;

namespace HanyingAgent.Contracts.Management.Sessions.Operations;

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
    /// Hides a session from lists without deleting data or stopping its task.
    /// The session remains accessible by its ID.
    /// </summary>
    Task HideAsync(SessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Closes an idle session, returns an already closed session unchanged,
    /// and rejects a session that still has an unfinished task.
    /// </summary>
    Task<SessionInfo> CloseAsync(
        SessionId sessionId,
        CancellationToken cancellationToken);
}
