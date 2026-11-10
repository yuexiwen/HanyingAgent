using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Contracts.Users.Operations.Requests;

public sealed record UpdateUserRequest(
    UserId UserId,
    string DisplayName);
