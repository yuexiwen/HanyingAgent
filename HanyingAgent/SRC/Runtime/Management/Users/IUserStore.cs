using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Runtime.Management.Users;

public interface IUserStore
{
    UserInfo Add(string displayName, DateTimeOffset now, CancellationToken cancellationToken);
    UserInfo? Get(UserId id, CancellationToken cancellationToken);
    IReadOnlyList<UserInfo> List(CancellationToken cancellationToken);
    UserInfo Update(UserId id, string displayName, DateTimeOffset now, CancellationToken cancellationToken);
}
