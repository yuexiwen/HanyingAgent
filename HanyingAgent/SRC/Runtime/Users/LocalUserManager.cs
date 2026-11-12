using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Contracts.Users.Operations;
using HanyingAgent.Contracts.Users.Operations.Requests;

namespace HanyingAgent.Runtime.Users;

public sealed class LocalUserManager(IUserStore store, TimeProvider clock) : IUserManager
{
    public Task<UserInfo> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        return Task.FromResult(store.Add(request.DisplayName, clock.GetUtcNow(), cancellationToken));
    }

    public Task<UserInfo?> GetAsync(UserId userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId.Value);
        return Task.FromResult(store.Get(userId, cancellationToken));
    }

    public Task<IReadOnlyList<UserInfo>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult(store.List(cancellationToken));

    public Task<UserInfo> UpdateAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        return Task.FromResult(store.Update(request.UserId, request.DisplayName, clock.GetUtcNow(), cancellationToken));
    }
}
