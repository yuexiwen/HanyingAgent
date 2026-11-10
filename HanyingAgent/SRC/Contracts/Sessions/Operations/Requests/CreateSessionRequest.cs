using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Contracts.Sessions.Operations.Requests;

public sealed record CreateSessionRequest(
    [property: JsonRequired] UserId UserId,
    string Name);
