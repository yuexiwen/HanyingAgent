namespace HanyingAgent.Contracts.Management.Users.Models;

public sealed record UserInfo(
    UserId Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
