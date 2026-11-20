using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Runtime.Management.Sessions;

public interface ISessionStore
{
    SessionInfo Add(UserId owner, string name, DateTimeOffset now, CancellationToken cancellationToken);
    SessionInfo? Get(SessionId id, UserId owner, CancellationToken cancellationToken);
    IReadOnlyList<SessionInfo> List(UserId owner, CancellationToken cancellationToken);
    void Hide(SessionId id, UserId owner, CancellationToken cancellationToken);
    SessionInfo Close(SessionId id, UserId owner, DateTimeOffset now, CancellationToken cancellationToken);
    IDisposable BeginTurn(SessionId id, UserId owner, CancellationToken cancellationToken);
}
