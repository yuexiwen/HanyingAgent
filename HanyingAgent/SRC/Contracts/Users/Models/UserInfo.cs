namespace HanyingAgent.Contracts.Users.Models;

public sealed record UserInfo(
    UserId Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
