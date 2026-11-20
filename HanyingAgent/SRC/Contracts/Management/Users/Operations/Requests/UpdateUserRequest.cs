using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Contracts.Management.Users.Operations.Requests;

public sealed record UpdateUserRequest(
    UserId UserId,
    string DisplayName);
