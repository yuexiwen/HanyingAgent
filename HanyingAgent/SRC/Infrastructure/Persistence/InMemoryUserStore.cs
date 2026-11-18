using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Runtime.Users;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed class InMemoryUserStore : IUserStore
{
    private readonly InMemoryAgentState _state;

    internal InMemoryUserStore(InMemoryAgentState state) => _state = state;

    public UserInfo Add(string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = new UserInfo(new UserId(Guid.NewGuid().ToString("N")), displayName, now, now);
            _state.Users.Add(user.Id, user);
            return user;
        }
    }

    public UserInfo? Get(UserId id, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _state.Users.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<UserInfo> List(CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _state.Users.Values.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id.Value, StringComparer.Ordinal).ToArray();
        }
    }

    public UserInfo Update(UserId id, string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_state.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = _state.Users.TryGetValue(id, out var existing)
                ? existing : throw new KeyNotFoundException("User does not exist.");
            var updated = user with { DisplayName = displayName, UpdatedAt = now > user.UpdatedAt ? now : user.UpdatedAt };
            _state.Users[id] = updated;
            return updated;
        }
    }
}
