using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;

public readonly record struct ToolCallId
{
    [JsonConstructor]
    public ToolCallId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
