using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Sessions.Models;

public readonly record struct SessionId
{
    [JsonConstructor]
    public SessionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
