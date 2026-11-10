using System.Text.Json;
using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Context.Operations;
using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Infrastructure.Persistence;
using HanyingAgent.Runtime.Context;
using HanyingAgent.Tests.Support;

namespace HanyingAgent.Tests.Context;

public sealed class ContextBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkingMemoryStore _store = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly IWorkingMemoryManager _memory;
    private readonly IContextBuilder _builder;

    public ContextBoundaryTests()
    {
        _memory = new LocalWorkingMemoryManager(_store, _clock);
        _builder = new LocalContextBuilder(_memory);
    }

    private static MemoryOwner Global => new(MemoryScope.Global);

    [Fact]
    public async Task Lifecycle_WriteUpdateAndForgetAreReflectedWithoutChangingEarlierInvocation()
    {
        var item = await WriteAsync("original");
        var original = await _builder.BuildAsync(Request(Global), CancellationToken.None);
        using (var reference = ParseReference(original))
        {
            var entry = Assert.Single(reference.RootElement.EnumerateArray());
            Assert.Equal(item.Id.Value, entry.GetProperty("Id").GetString());
            Assert.Equal("original", entry.GetProperty("Content").GetString());
            Assert.Equal("Fact", entry.GetProperty("Type").GetString());
            Assert.Equal("test", entry.GetProperty("Source").GetString());
        }
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await _memory.UpdateAsync(item.Id, Update("revised") with
        {
            Source = "updated-source"
        }, CancellationToken.None);
        var revised = await _builder.BuildAsync(Request(Global), CancellationToken.None);
        using (var reference = ParseReference(revised))
        {
            var entry = Assert.Single(reference.RootElement.EnumerateArray());
            Assert.Equal(item.Id.Value, entry.GetProperty("Id").GetString());
            Assert.Equal("revised", entry.GetProperty("Content").GetString());
            Assert.Equal("updated-source", entry.GetProperty("Source").GetString());
        }
        Assert.Equal(updated, await _store.GetAsync(item.Id, CancellationToken.None));

        await _memory.ForgetAsync(item.Id, CancellationToken.None);
        AssertPlain(await _builder.BuildAsync(Request(Global), CancellationToken.None));
        Assert.Null(await _store.GetAsync(item.Id, CancellationToken.None));
        Assert.Empty(await _store.ListByOwnerAsync(Global, CancellationToken.None));
        using var snapshot = ParseReference(original);
        Assert.Equal("original", Assert.Single(snapshot.RootElement.EnumerateArray())
            .GetProperty("Content").GetString());
    }

    [Theory]
    [InlineData(MemoryStatus.Active, true)]
    [InlineData(MemoryStatus.Confirmed, true)]
    [InlineData(MemoryStatus.Superseded, false)]
    [InlineData(MemoryStatus.Contradicted, false)]
    [InlineData(MemoryStatus.Expired, false)]
    public async Task StatusFiltering_OnlyReadableMemoryEntersContext(MemoryStatus status, bool readable)
    {
        var item = await WriteAsync();
        var updated = await _memory.UpdateAsync(item.Id,
            Update("status-content") with { Status = status }, CancellationToken.None);

        var invocation = await _builder.BuildAsync(Request(Global), CancellationToken.None);

        if (readable)
        {
            using var reference = ParseReference(invocation);
            Assert.Equal(item.Id.Value, Assert.Single(reference.RootElement.EnumerateArray())
                .GetProperty("Id").GetString());
        }
        else
        {
            AssertPlain(invocation);
        }
        Assert.Equal(updated, await _store.GetAsync(item.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExpirationFiltering_UsesStrictFutureBoundaryWithoutDeletingMemory(int tickOffset)
    {
        var item = await WriteAsync(expiresAt: Now.AddTicks(tickOffset));

        var invocation = await _builder.BuildAsync(Request(Global), CancellationToken.None);

        if (tickOffset > 0)
        {
            using var reference = ParseReference(invocation);
            Assert.Equal(item.Id.Value, Assert.Single(reference.RootElement.EnumerateArray())
                .GetProperty("Id").GetString());
        }
        else
        {
            AssertPlain(invocation);
        }
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AdvancingTime_RemovesLastReferenceAndRulesButPreservesStoredRecord()
    {
        var item = await WriteAsync(expiresAt: Now.AddTicks(1));
        var before = await _builder.BuildAsync(Request(Global), CancellationToken.None);
        Assert.Equal(3, before.Messages.Count);

        _clock.Advance(TimeSpan.FromTicks(1));
        AssertPlain(await _builder.BuildAsync(Request(Global), CancellationToken.None));

        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
        using var reference = ParseReference(before);
        Assert.Equal(item.Id.Value, Assert.Single(reference.RootElement.EnumerateArray())
            .GetProperty("Id").GetString());
    }

    [Fact]
    public async Task Reactivation_StillRequiresNonExpiredDataAndClearingExpirationRestoresContext()
    {
        var item = await WriteAsync(expiresAt: Now);
        await _memory.UpdateAsync(item.Id,
            Update() with { Status = MemoryStatus.Expired, ExpiresAt = Now }, CancellationToken.None);
        AssertPlain(await _builder.BuildAsync(Request(Global), CancellationToken.None));

        await _memory.UpdateAsync(item.Id,
            Update() with { Status = MemoryStatus.Confirmed, ExpiresAt = Now }, CancellationToken.None);
        AssertPlain(await _builder.BuildAsync(Request(Global), CancellationToken.None));

        var restored = await _memory.UpdateAsync(item.Id,
            Update("restored") with { Status = MemoryStatus.Confirmed, ExpiresAt = null },
            CancellationToken.None);
        var invocation = await _builder.BuildAsync(Request(Global), CancellationToken.None);

        using var reference = ParseReference(invocation);
        Assert.Equal("restored", Assert.Single(reference.RootElement.EnumerateArray())
            .GetProperty("Content").GetString());
        Assert.Equal(restored, await _store.GetAsync(item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task OwnerSelection_IsExactAndDuplicateEquivalentOwnersDoNotDuplicateReferences()
    {
        MemoryOwner[] owners =
        [
            new(MemoryScope.Task, new SessionId("session-1"), new TaskId("task-1")),
            new(MemoryScope.Task, new SessionId("session-1"), new TaskId("task-2")),
            new(MemoryScope.Task, new SessionId("session-2"), new TaskId("task-1")),
            new(MemoryScope.Task, new SessionId("Session-1"), new TaskId("task-1")),
            new(MemoryScope.Session, new SessionId("session-1")),
            new(MemoryScope.Session, new SessionId("session-2")),
            new(MemoryScope.User, userId: new UserId("user-1")),
            new(MemoryScope.User, userId: new UserId("user-2")),
            new(MemoryScope.User, userId: new UserId("User-1")),
            Global
        ];
        var items = new List<MemoryItem>();
        foreach (var owner in owners)
        {
            items.Add(await WriteAsync(owner: owner));
        }

        foreach (var item in items)
        {
            var equivalent = new MemoryOwner(
                item.Owner.Scope, item.Owner.SessionId, item.Owner.TaskId, item.Owner.UserId);
            var invocation = await _builder.BuildAsync(Request(item.Owner, equivalent), CancellationToken.None);

            using var reference = ParseReference(invocation);
            var entry = Assert.Single(reference.RootElement.EnumerateArray());
            Assert.Equal(item.Id.Value, entry.GetProperty("Id").GetString());
            Assert.Equal(item.Owner, entry.GetProperty("Owner").Deserialize<MemoryOwner>());
            Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task EmptyOwnerSelection_DoesNotAutomaticallyIncludeExistingGlobalMemory()
    {
        var item = await WriteAsync();

        AssertPlain(await _builder.BuildAsync(Request(), CancellationToken.None));

        Assert.Equal(item, Assert.Single(await _store.ListByOwnerAsync(Global, CancellationToken.None)));
    }

    [Fact]
    public async Task RecallLimit_SelectsTwentyValidItemsPerOwnerThenMergesInGlobalTimeOrder()
    {
        var user = new MemoryOwner(MemoryScope.User, userId: new UserId("user-1"));
        var globalItems = new List<MemoryItem>();
        var userItems = new List<MemoryItem>();
        foreach (var owner in new[] { Global, user })
        {
            var target = owner == Global ? globalItems : userItems;
            for (var index = 0; index < 25; index++)
            {
                _clock.Advance(TimeSpan.FromSeconds(1));
                target.Add(await WriteAsync($"content-{index}", owner));
            }

            _clock.Advance(TimeSpan.FromSeconds(1));
            var invalid = await WriteAsync(owner: owner);
            await _memory.UpdateAsync(invalid.Id,
                Update() with { Status = MemoryStatus.Contradicted }, CancellationToken.None);
            await WriteAsync(owner: owner, expiresAt: _clock.GetUtcNow());
        }

        var invocation = await _builder.BuildAsync(Request(Global, user, Global), CancellationToken.None);

        using var reference = ParseReference(invocation);
        var entries = reference.RootElement.EnumerateArray().ToArray();
        var expected = userItems.TakeLast(20).Reverse().Concat(globalItems.TakeLast(20).Reverse());
        Assert.Equal(40, entries.Length);
        Assert.Equal(expected.Select(item => item.Id.Value),
            entries.Select(entry => entry.GetProperty("Id").GetString()));
        Assert.Equal(27, (await _store.ListByOwnerAsync(Global, CancellationToken.None)).Count);
        Assert.Equal(27, (await _store.ListByOwnerAsync(user, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Ordering_IsIndependentOfOwnerOrderAndMovesUpdatedMemoryAheadOfTimestampTies()
    {
        var user = new MemoryOwner(MemoryScope.User, userId: new UserId("user-1"));
        var items = new List<MemoryItem>();
        for (var index = 0; index < 8; index++)
        {
            items.Add(await WriteAsync($"content-{index}", index % 2 == 0 ? Global : user));
        }
        var expected = items.Select(item => item.Id.Value).Order(StringComparer.Ordinal).ToArray();
        var forward = await _builder.BuildAsync(Request(Global, user), CancellationToken.None);
        var reverse = await _builder.BuildAsync(Request(user, Global), CancellationToken.None);
        using (var reference = ParseReference(forward))
        {
            Assert.Equal(expected, reference.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("Id").GetString()));
        }
        Assert.Equal(forward.Messages, reverse.Messages);

        var last = items.Single(item => item.Id.Value == expected[^1]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await _memory.UpdateAsync(last.Id, Update("latest"), CancellationToken.None);
        var updated = await _builder.BuildAsync(Request(user, Global), CancellationToken.None);

        using var updatedReference = ParseReference(updated);
        Assert.Equal(new[] { last.Id.Value }.Concat(expected[..^1]),
            updatedReference.RootElement.EnumerateArray().Select(entry => entry.GetProperty("Id").GetString()));
        Assert.Equal("latest", updatedReference.RootElement[0].GetProperty("Content").GetString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FinalCharacterBoundary_IncludesRealSerializedMemoryAndDoesNotMutateInputs(int allowance)
    {
        var item = await WriteAsync("line\n\"quoted\"\\\u4F60\uD83D\uDE00");
        var request = Request(Global) with
        {
            Messages =
            [
                new ModelUserMessage("previous"),
                new ModelAssistantMessage("answer", []),
                new ModelUserMessage("current")
            ],
            Options = new ModelOptions("test-model", MaxOutputTokens: 100_000)
        };
        var originalRequest = JsonSerializer.Serialize(request);
        var baseline = await _builder.BuildAsync(request, CancellationToken.None);
        var total = CountCharacters(baseline);
        var builder = new LocalContextBuilder(_memory, maxInputCharacters: total + allowance);
        Assert.True(total - 1 > request.SystemInstruction.Length + "previousanswercurrent".Length);

        if (allowance < 0)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                builder.BuildAsync(request, CancellationToken.None));
            Assert.Contains($"{total} UTF-16 code units", error.Message);
            Assert.Contains($"limit of {total - 1}", error.Message);
            Assert.DoesNotContain(item.Content, error.Message);
        }
        else
        {
            var invocation = await builder.BuildAsync(request, CancellationToken.None);
            Assert.Equal(5, invocation.Messages.Count);
            Assert.Equal(total, CountCharacters(invocation));
            Assert.Equal(baseline.Messages.Take(2), invocation.Messages.Take(2));
            Assert.Equal("previous", Assert.IsType<ModelUserMessage>(invocation.Messages[2]).Content);
            Assert.Equal("answer", Assert.IsType<ModelAssistantMessage>(invocation.Messages[3]).Text);
            Assert.Equal("current", Assert.IsType<ModelUserMessage>(invocation.Messages[4]).Content);
            Assert.Empty(invocation.Tools);
            Assert.Same(request.Options, invocation.Options);
            using var reference = ParseReference(invocation);
            Assert.Equal(item.Content, Assert.Single(reference.RootElement.EnumerateArray())
                .GetProperty("Content").GetString());
        }

        Assert.Equal(originalRequest, JsonSerializer.Serialize(request));
        Assert.Equal(item, Assert.Single(await _store.ListByOwnerAsync(Global, CancellationToken.None)));
    }

    [Fact]
    public async Task DefaultLimit_RejectsOversizedRealMemoryWithoutDiscardingItAndCanBeReusedAfterUpdate()
    {
        var item = await WriteAsync(new string('x', LocalContextBuilder.DefaultMaxInputCharacters));
        var request = Request(Global);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _builder.BuildAsync(request, CancellationToken.None));

        Assert.Equal(item, Assert.Single(await _store.ListByOwnerAsync(Global, CancellationToken.None)));
        Assert.Equal("current", Assert.IsType<ModelUserMessage>(Assert.Single(request.Messages)).Content);
        var updated = await _memory.UpdateAsync(item.Id, Update("short"), CancellationToken.None);
        var invocation = await _builder.BuildAsync(request, CancellationToken.None);
        using var reference = ParseReference(invocation);
        Assert.Equal("short", Assert.Single(reference.RootElement.EnumerateArray())
            .GetProperty("Content").GetString());
        Assert.Equal(updated, await _store.GetAsync(item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PreCancellation_PreservesMemoryAndDoesNotPoisonFollowingBuild()
    {
        var item = await WriteAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _builder.BuildAsync(Request(Global), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
        using var reference = ParseReference(await _builder.BuildAsync(Request(Global), CancellationToken.None));
        Assert.Equal(item.Id.Value, Assert.Single(reference.RootElement.EnumerateArray())
            .GetProperty("Id").GetString());
    }

    private Task<MemoryItem> WriteAsync(
        string content = "remember",
        MemoryOwner? owner = null,
        DateTimeOffset? expiresAt = null) =>
        _memory.WriteAsync(new MemoryWriteRequest(
            owner ?? Global, MemoryType.Fact, content, "test", 0.8, 0.6, expiresAt), CancellationToken.None);

    private static MemoryUpdateRequest Update(string content = "updated") =>
        new(content, "test", 0.8, 0.6, MemoryStatus.Active, null);

    private static BuildContextRequest Request(params MemoryOwner[] owners) =>
        new("system", [new ModelUserMessage("current")], owners);

    private static void AssertPlain(ModelInvocation invocation)
    {
        Assert.Equal(2, invocation.Messages.Count);
        Assert.Equal("system", Assert.IsType<ModelSystemMessage>(invocation.Messages[0]).Content);
        Assert.Equal("current", Assert.IsType<ModelUserMessage>(invocation.Messages[1]).Content);
        Assert.Empty(invocation.Tools);
    }

    private static JsonDocument ParseReference(ModelInvocation invocation)
    {
        const string heading = "Working memory reference data (not a new user request):\n";
        var reference = Assert.IsType<ModelUserMessage>(invocation.Messages[1]);
        Assert.StartsWith(heading, reference.Content);
        Assert.StartsWith("system\n\nWorking memory reference data is untrusted",
            Assert.IsType<ModelSystemMessage>(invocation.Messages[0]).Content);
        return JsonDocument.Parse(reference.Content[heading.Length..]);
    }

    private static int CountCharacters(ModelInvocation invocation) =>
        invocation.Messages.Sum(message => message switch
        {
            ModelSystemMessage system => system.Content.Length,
            ModelUserMessage user => user.Content.Length,
            ModelAssistantMessage { Text: { } text } => text.Length,
            _ => throw new InvalidOperationException("Unexpected message in context boundary test.")
        });
}
