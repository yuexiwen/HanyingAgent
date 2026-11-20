using System.Text.Json;
using HanyingAgent.Contracts.Agent.Context.Operations.Requests;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Agent.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;
using HanyingAgent.Infrastructure.LanguageModels;
using HanyingAgent.Runtime.Agent.Context;
using HanyingAgent.Tests.Support;

namespace HanyingAgent.Tests.Agent.Context;

public sealed class UnifiedContextBudgetTests
{
    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task MandatoryInput_ReservesOutputAndSafetyMarginBeforeCheckingTokenBoundary(int input, bool fits)
    {
        var counter = new TestModelTokenCounter(contextWindowTokens: 15, defaultMaxOutputTokens: 3, safetyMarginTokens: 2);
        var builder = new LocalContextBuilder(counter);
        var request = new BuildContextRequest("s", [new ModelUserMessage(new string('x', input - 1))], []);
        if (fits)
        {
            var result = await builder.BuildAsync(request, CancellationToken.None);
            Assert.Equal(3, result.Options!.MaxOutputTokens);
            Assert.True(counter.CountInputTokens(result.Messages) + result.Options.MaxOutputTokens + 2 <= 15);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(request, CancellationToken.None));
            Assert.Contains("10 input tokens", error.Message);
        }
    }

    [Fact]
    public async Task ExplicitOutputReservation_ReducesInputCapacityAndIsSentUnchanged()
    {
        var counter = new TestModelTokenCounter(contextWindowTokens: 20, defaultMaxOutputTokens: 3);
        var options = new ModelOptions(MaxOutputTokens: 10);
        var request = new BuildContextRequest("s", [new ModelUserMessage("123456789")], [], options);
        var result = await new LocalContextBuilder(counter).BuildAsync(request, CancellationToken.None);
        Assert.Same(options, result.Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalContextBuilder(counter).BuildAsync(
            request with { Options = options with { MaxOutputTokens = 11 } }, CancellationToken.None));
    }

    [Fact]
    public async Task GrowingHistory_CannotConsumeMemoryShareAndHistoryRetainsItsSeventyFivePercent()
    {
        var first = Item("a");
        var second = Item("b");
        var memoryOnly = await Build(Request([], [first]));
        var baseline = Size(await Build(Request([], [])));
        var memoryCost = Size(memoryOnly) - baseline;
        var maxInput = baseline + 4 * memoryCost;
        var pairLength = 3 * memoryCost;

        foreach (var turns in new[] { 1, 10, 40 })
        {
            var history = Enumerable.Range(0, turns).SelectMany(index => new ModelMessage[]
            {
                new ModelUserMessage(new string('u', pairLength - 1)),
                new ModelAssistantMessage(index == turns - 1 ? "z" : "a", [])
            }).ToArray();
            var result = await Build(Request(history, [second, first]), maxInput);

            using var reference = ParseReference(result);
            Assert.Equal("a", Assert.Single(reference.RootElement.EnumerateArray()).GetProperty("Id").GetString());
            Assert.Equal(5, result.Messages.Count);
            Assert.Equal("z", Assert.IsType<ModelAssistantMessage>(result.Messages[^2]).Text);
            Assert.Equal("current", Assert.IsType<ModelUserMessage>(result.Messages[^1]).Content);
            Assert.Equal(maxInput, Size(result));
        }
    }

    [Fact]
    public async Task EmptyMemory_LendsItsEntireShareToHistory()
    {
        ModelMessage[] history = [new ModelUserMessage("previous"), new ModelAssistantMessage("answer", [])];
        var request = Request(history, []);
        var full = await Build(request);
        var exact = await Build(request, Size(full));
        Assert.Equal(full.Messages, exact.Messages);
    }

    [Fact]
    public async Task EmptyHistory_LendsItsEntireShareToMemory()
    {
        var request = Request([], [Item("a"), Item("b")]);
        var full = await Build(request);
        var exact = await Build(request, Size(full));
        using var reference = ParseReference(exact);
        Assert.Equal(2, reference.RootElement.GetArrayLength());
        Assert.Equal(full.Messages, exact.Messages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnusedShare_IsBorrowedEvenWhenBothSourcesHaveCandidates(bool spareMemoryShare)
    {
        var item = Item("a");
        var memoryCost = Size(await Build(Request([], [item]))) - Size(await Build(Request([], [])));
        var historyCost = spareMemoryShare ? memoryCost * 4 : 2;
        ModelMessage[] history = [
            new ModelUserMessage(new string('h', historyCost - 1)), new ModelAssistantMessage("a", [])];
        var request = Request(history, [item]);
        var full = await Build(request);
        var exact = await Build(request, Size(full));
        Assert.Equal(full.Messages, exact.Messages);
    }

    [Fact]
    public async Task OversizedMemory_IsSkippedWholeAndSmallerMemoryStillFits()
    {
        var small = Item("b");
        var huge = Item("a") with { Content = new string('x', 5000) };
        var limit = Size(await Build(Request([], [small])));
        var request = Request([], [huge, small]);
        var result = await Build(request, limit);
        using var reference = ParseReference(result);
        Assert.Equal("b", Assert.Single(reference.RootElement.EnumerateArray()).GetProperty("Id").GetString());
        Assert.Equal(5000, request.Memories[0].Content.Length);
        Assert.True(Size(result) <= limit);
    }

    [Fact]
    public async Task OversizedRecentTurn_IsNotSplitOrReplacedWithOlderTurns()
    {
        var request = Request([
            new ModelUserMessage("old"), new ModelAssistantMessage("old answer", []),
            new ModelUserMessage(new string('x', 1000)), new ModelAssistantMessage("recent answer", [])
        ], []);
        var result = await Build(request, 50);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("current", Assert.IsType<ModelUserMessage>(result.Messages[^1]).Content);
    }

    [Fact]
    public async Task RequestCharacterCap_IsAppliedAlongsideModelTokenBudget()
    {
        var result = await Build(Request([
            new ModelUserMessage("previous"), new ModelAssistantMessage("answer", [])
        ], [Item("a")]) with { MaxInputCharacters = "systemcurrent".Length });
        Assert.Equal(2, result.Messages.Count);
    }

    [Theory]
    [InlineData("a", 26)]
    [InlineData("\u4f60", 28)]
    [InlineData("\ud83d\ude00", 29)]
    public void ConservativeCounter_CountsUtf8BytesAndMessageFramingAndDeclaresEstimate(string text, int expected)
    {
        var counter = new ConservativeModelTokenCounter("qwen2.5:1.5b", 4096, 256);
        Assert.Equal(expected, counter.CountInputTokens([new ModelSystemMessage("s"), new ModelUserMessage(text)]));
        var budget = counter.GetBudget(null);
        Assert.True(budget.IsEstimate);
        Assert.Equal(4096 - 256 - 128, budget.MaxInputTokens);
        Assert.Throws<NotSupportedException>(() => counter.GetBudget(new ModelOptions("unconfigured-model")));
        Assert.Throws<ArgumentOutOfRangeException>(() => counter.GetBudget(new ModelOptions(MaxOutputTokens: 4096)));
    }

    private static Task<ModelInvocation> Build(BuildContextRequest request, int maxInput = 100_000) =>
        new LocalContextBuilder(new TestModelTokenCounter(
            contextWindowTokens: maxInput + 256), maxInputCharacters: int.MaxValue)
            .BuildAsync(request, CancellationToken.None);

    private static BuildContextRequest Request(ModelMessage[] history, MemoryItem[] memories) =>
        new("system", [.. history, new ModelUserMessage("current")], memories);

    private static int Size(ModelInvocation invocation) =>
        checked((int)new TestModelTokenCounter().CountInputTokens(invocation.Messages));

    private static MemoryItem Item(string id) =>
        new(new MemoryId(id), new MemoryOwner(MemoryScope.Global), MemoryType.Preference,
            "Prefer focused methods.", "test", 1, 0.5, MemoryStatus.Active,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    private static JsonDocument ParseReference(ModelInvocation invocation)
    {
        const string heading = "Working memory reference data (not a new user request):\n";
        var reference = Assert.IsType<ModelUserMessage>(invocation.Messages[1]).Content;
        Assert.StartsWith(heading, reference);
        return JsonDocument.Parse(reference[heading.Length..]);
    }
}
