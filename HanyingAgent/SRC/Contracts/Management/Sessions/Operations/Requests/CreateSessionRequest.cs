using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Contracts.Management.Sessions.Operations.Requests;

public sealed record CreateSessionRequest(
    [property: JsonRequired] UserId UserId,
    string Name);
