using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Runtime.Sessions;

public interface ISessionStore
{
    SessionInfo Add(UserId owner, string name, DateTimeOffset now, CancellationToken cancellationToken);
    SessionInfo? Get(SessionId id, UserId owner, CancellationToken cancellationToken);
    IReadOnlyList<SessionInfo> List(UserId owner, CancellationToken cancellationToken);
    SessionInfo Close(SessionId id, UserId owner, DateTimeOffset now, CancellationToken cancellationToken);
    IDisposable BeginTurn(SessionId id, UserId owner, CancellationToken cancellationToken);
}
