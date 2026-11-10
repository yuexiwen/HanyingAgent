using System.Text.Json;
using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Tests.WorkingMemory;

public sealed class MemoryIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Constructor_RejectsMissingOrWhitespaceValue(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new MemoryId(value!));
    }

    [Fact]
    public void Constructor_PreservesValue()
    {
        var id = new MemoryId("memory-1");

        Assert.Equal("memory-1", id.Value);
    }

    [Theory]
    [InlineData("memory-1", "memory-1", true)]
    [InlineData("memory-1", "memory-2", false)]
    [InlineData("memory-1", "Memory-1", false)]
    public void Equality_UsesExactValue(string first, string second, bool equal)
    {
        Assert.Equal(equal, new MemoryId(first) == new MemoryId(second));
    }

    [Fact]
    public void Json_RoundTripsValue()
    {
        var id = new MemoryId("memory-1");

        var restored = JsonSerializer.Deserialize<MemoryId>(JsonSerializer.Serialize(id));

        Assert.Equal(id, restored);
    }

    [Fact]
    public void DefaultValue_HasNoUsableIdentifier()
    {
        Assert.Null(default(MemoryId).Value);
    }
}
