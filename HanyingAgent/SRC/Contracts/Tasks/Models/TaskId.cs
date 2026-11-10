using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Tasks.Models;

public readonly record struct TaskId
{
    [JsonConstructor]
    public TaskId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
