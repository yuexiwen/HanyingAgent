using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Users.Models;

public readonly record struct UserId
{
    [JsonConstructor]
    public UserId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
