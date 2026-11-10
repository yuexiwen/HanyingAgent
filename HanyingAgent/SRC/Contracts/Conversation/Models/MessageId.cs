using System.Text.Json.Serialization;

namespace HanyingAgent.Contracts.Conversation.Models;

public readonly record struct MessageId
{
    [JsonConstructor]
    public MessageId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
}
