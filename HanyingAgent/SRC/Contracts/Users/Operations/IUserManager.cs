using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Contracts.Users.Operations.Requests;

namespace HanyingAgent.Contracts.Users.Operations;

public interface IUserManager
{
    Task<UserInfo> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns null when the user does not exist.
    /// </summary>
    Task<UserInfo?> GetAsync(
        UserId userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserInfo>> ListAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the display name of an existing user and rejects an unknown user.
    /// </summary>
    Task<UserInfo> UpdateAsync(
        UpdateUserRequest request,
        CancellationToken cancellationToken);
}
