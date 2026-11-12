using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Contracts.Users.Operations;
using HanyingAgent.Contracts.Users.Operations.Requests;

namespace HanyingAgent.Runtime.Users;

public sealed class LocalUserContext(IUserManager users)
{
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private UserInfo? _user;

    public async Task<UserId> GetUserIdAsync(CancellationToken cancellationToken)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            _user ??= await users.CreateAsync(new CreateUserRequest("Local user"), cancellationToken);
            return _user.Id;
        }
        finally
        {
            _initialization.Release();
        }
    }
}
