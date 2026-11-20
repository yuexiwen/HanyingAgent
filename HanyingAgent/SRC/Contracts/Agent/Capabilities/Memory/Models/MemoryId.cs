using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

public readonly record struct MemoryId
{
    [JsonConstructor]
    public MemoryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
