using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Infrastructure.Persistence;

namespace HanyingAgent.Tests.WorkingMemory;

public sealed class InMemoryWorkingMemoryStoreTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_MakesExactItemAvailable()
    {
        IWorkingMemoryStore store = new InMemoryWorkingMemoryStore();
        var item = CreateItem();

        await store.AddAsync(item, CancellationToken.None);

        Assert.Equal(item, await store.GetAsync(item.Id, CancellationToken.None));
        Assert.Equal(item, Assert.Single(await store.ListByOwnerAsync(item.Owner, CancellationToken.None)));
    }

    [Fact]
    public async Task AddAsync_RejectsDuplicateIdWithoutReplacingItem()
    {
        var store = new InMemoryWorkingMemoryStore();
        var item = CreateItem();
        await store.AddAsync(item, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddAsync(item with { Content = "replacement" }, CancellationToken.None));

        Assert.Equal(item, await store.GetAsync(item.Id, CancellationToken.None));
        Assert.Single(await store.ListByOwnerAsync(item.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownId()
    {
        var store = new InMemoryWorkingMemoryStore();

        Assert.Null(await store.GetAsync(new MemoryId("unknown"), CancellationToken.None));
    }

    [Fact]
    public async Task StoreInstances_DoNotShareState()
    {
        var first = new InMemoryWorkingMemoryStore();
        var second = new InMemoryWorkingMemoryStore();
        var item = CreateItem();
        await first.AddAsync(item, CancellationToken.None);

        Assert.Null(await second.GetAsync(item.Id, CancellationToken.None));
        Assert.Empty(await second.ListByOwnerAsync(item.Owner, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesMutableFieldsAndPreservesIdentity()
    {
        var store = new InMemoryWorkingMemoryStore();
        var owner = new MemoryOwner(MemoryScope.Task, new SessionId("session-1"), new TaskId("task-1"));
        var original = CreateItem(owner: owner) with { Type = MemoryType.Preference };
        var update = CreateUpdate(expiresAt: InitialTime.AddHours(2));
        var updatedAt = InitialTime.AddMinutes(1);
        await store.AddAsync(original, CancellationToken.None);

        var result = await store.UpdateAsync(original.Id, update, updatedAt, CancellationToken.None);

        var expected = original with
        {
            Content = update.Content,
            Source = update.Source,
            Confidence = update.Confidence,
            Importance = update.Importance,
            Status = update.Status,
            UpdatedAt = updatedAt,
            ExpiresAt = update.ExpiresAt
        };
        Assert.Equal(expected, result);
        Assert.Equal(expected, await store.GetAsync(original.Id, CancellationToken.None));
        Assert.NotSame(original, result);
        Assert.Equal("original", original.Content);
        Assert.Equal(InitialTime, original.UpdatedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task UpdateAsync_AppliesContentWithoutMovingUpdatedAtBackwards(int minuteOffset)
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem() with { UpdatedAt = InitialTime.AddMinutes(10) };
        var candidateTime = original.UpdatedAt.AddMinutes(minuteOffset);
        await store.AddAsync(original, CancellationToken.None);

        var result = await store.UpdateAsync(
            original.Id, CreateUpdate(), candidateTime, CancellationToken.None);

        Assert.Equal("updated", result.Content);
        Assert.Equal(minuteOffset > 0 ? candidateTime : original.UpdatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_LastAppliedPayloadWinsEvenWithEarlierTimestamp()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);
        await store.UpdateAsync(
            original.Id, CreateUpdate("first"), InitialTime.AddMinutes(2), CancellationToken.None);

        var result = await store.UpdateAsync(
            original.Id, CreateUpdate("last"), InitialTime.AddMinutes(1), CancellationToken.None);

        Assert.Equal("last", result.Content);
        Assert.Equal(InitialTime.AddMinutes(2), result.UpdatedAt);
        Assert.Equal(result, await store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_CanClearExpiration()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);

        var result = await store.UpdateAsync(
            original.Id, CreateUpdate(expiresAt: null), InitialTime, CancellationToken.None);

        Assert.Null(result.ExpiresAt);
        Assert.Null((await store.GetAsync(original.Id, CancellationToken.None))!.ExpiresAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task UpdateAsync_RetainsSuppliedExpiration(int minuteOffset)
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        var expiration = InitialTime.AddMinutes(minuteOffset);
        await store.AddAsync(original, CancellationToken.None);

        var result = await store.UpdateAsync(
            original.Id, CreateUpdate(expiresAt: expiration), InitialTime, CancellationToken.None);

        Assert.Equal(expiration, result.ExpiresAt);
        Assert.Equal(result, await store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnknownIdWithoutCreatingRecord()
    {
        var store = new InMemoryWorkingMemoryStore();
        var unknown = new MemoryId("unknown");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.UpdateAsync(unknown, CreateUpdate(), InitialTime, CancellationToken.None));

        Assert.Null(await store.GetAsync(unknown, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecordAndAllowsIdToBeAddedAgain()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);

        await store.DeleteAsync(original.Id, CancellationToken.None);

        Assert.Null(await store.GetAsync(original.Id, CancellationToken.None));
        Assert.Empty(await store.ListByOwnerAsync(original.Owner, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.DeleteAsync(original.Id, CancellationToken.None));

        var replacement = original with { Content = "new record" };
        await store.AddAsync(replacement, CancellationToken.None);
        Assert.Equal(replacement, await store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RejectsUnknownIdWithoutChangingOtherRecords()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.DeleteAsync(new MemoryId("unknown"), CancellationToken.None));

        Assert.Equal(original, Assert.Single(
            await store.ListByOwnerAsync(original.Owner, CancellationToken.None)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ListByOwnerAsync_MatchesExactScopeAndIds(int ownerIndex)
    {
        var store = new InMemoryWorkingMemoryStore();
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
            new(MemoryScope.Global)
        ];
        var items = owners.Select((owner, index) => CreateItem($"memory-{index}", owner)).ToArray();
        foreach (var item in items)
        {
            await store.AddAsync(item, CancellationToken.None);
        }

        var selected = owners[ownerIndex];
        var equivalentOwner = new MemoryOwner(
            selected.Scope, selected.SessionId, selected.TaskId, selected.UserId);
        var result = await store.ListByOwnerAsync(equivalentOwner, CancellationToken.None);

        Assert.Equal(items[ownerIndex], Assert.Single(result));
    }

    [Fact]
    public async Task ListByOwnerAsync_ReturnsEmptyWhenOwnerHasNoRecords()
    {
        var store = new InMemoryWorkingMemoryStore();
        await store.AddAsync(CreateItem(), CancellationToken.None);

        var result = await store.ListByOwnerAsync(
            new MemoryOwner(MemoryScope.Session, new SessionId("unknown")), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByOwnerAsync_ReturnsAllCandidatesWithoutReadFilteringOrLimit()
    {
        var store = new InMemoryWorkingMemoryStore();
        var statuses = Enum.GetValues<MemoryStatus>();
        var items = Enumerable.Range(0, 25).Select(index => CreateItem($"memory-{index}") with
        {
            Type = index % 2 == 0 ? MemoryType.Fact : MemoryType.Preference,
            Status = statuses[index % statuses.Length],
            ExpiresAt = index % 3 == 0 ? null : InitialTime.AddDays(-1)
        }).ToArray();
        foreach (var item in items)
        {
            await store.AddAsync(item, CancellationToken.None);
        }

        var result = await store.ListByOwnerAsync(items[0].Owner, CancellationToken.None);

        Assert.Equal(items.Length, result.Count);
        Assert.All(items, item => Assert.Contains(item, result));
        foreach (var item in items)
        {
            Assert.Equal(item, await store.GetAsync(item.Id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ListByOwnerAsync_SnapshotSurvivesLaterUpdatesAddsAndDeletes()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);
        var snapshot = await store.ListByOwnerAsync(original.Owner, CancellationToken.None);

        await store.UpdateAsync(original.Id, CreateUpdate(), InitialTime.AddMinutes(1), CancellationToken.None);
        var added = CreateItem("memory-2");
        await store.AddAsync(added, CancellationToken.None);
        await store.DeleteAsync(original.Id, CancellationToken.None);

        Assert.Equal(original, Assert.Single(snapshot));
        Assert.Equal(added, Assert.Single(
            await store.ListByOwnerAsync(original.Owner, CancellationToken.None)));
    }

    [Fact]
    public async Task ListByOwnerAsync_ChangingSnapshotElementsDoesNotChangeStoredRecords()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);
        var snapshot = await store.ListByOwnerAsync(original.Owner, CancellationToken.None);
        var elements = Assert.IsAssignableFrom<IList<MemoryItem>>(snapshot);

        elements[0] = original with { Content = "local replacement" };

        Assert.Equal(original, await store.GetAsync(original.Id, CancellationToken.None));
        Assert.Equal(original, Assert.Single(
            await store.ListByOwnerAsync(original.Owner, CancellationToken.None)));
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Update")]
    [InlineData("List")]
    public async Task Operations_RejectNullArguments(string operationName)
    {
        var store = new InMemoryWorkingMemoryStore();
        Func<Task> operation = operationName switch
        {
            "Add" => () => store.AddAsync(null!, CancellationToken.None),
            "Update" => () => store.UpdateAsync(
                new MemoryId("memory-1"), null!, InitialTime, CancellationToken.None),
            "List" => () => store.ListByOwnerAsync(null!, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName))
        };

        await Assert.ThrowsAsync<ArgumentNullException>(operation);
    }

    [Fact]
    public async Task AddAsync_RejectsNullOwnerWithoutSavingItem()
    {
        var store = new InMemoryWorkingMemoryStore();
        var item = CreateItem() with { Owner = null! };

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.AddAsync(item, CancellationToken.None));

        Assert.Null(await store.GetAsync(item.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Get")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public async Task Operations_RejectDefaultMemoryId(string operationName)
    {
        var store = new InMemoryWorkingMemoryStore();
        Func<Task> operation = operationName switch
        {
            "Add" => () => store.AddAsync(CreateItem() with { Id = default }, CancellationToken.None),
            "Get" => () => store.GetAsync(default, CancellationToken.None),
            "Update" => () => store.UpdateAsync(default, CreateUpdate(), InitialTime, CancellationToken.None),
            "Delete" => () => store.DeleteAsync(default, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName))
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(operation);
        Assert.Empty(await store.ListByOwnerAsync(new MemoryOwner(MemoryScope.Global), CancellationToken.None));
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Get")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("List")]
    public async Task Operations_ObservePreCancellationWithoutChangingState(string operationName)
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        var added = CreateItem("memory-2");
        await store.AddAsync(original, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        Func<Task> operation = operationName switch
        {
            "Add" => () => store.AddAsync(added, token),
            "Get" => () => store.GetAsync(original.Id, token),
            "Update" => () => store.UpdateAsync(original.Id, CreateUpdate(), InitialTime.AddMinutes(1), token),
            "Delete" => () => store.DeleteAsync(original.Id, token),
            "List" => () => store.ListByOwnerAsync(original.Owner, token),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName))
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(operation);

        Assert.Equal(token, exception.CancellationToken);
        Assert.Null(await store.GetAsync(added.Id, CancellationToken.None));
        Assert.Equal(original, await store.GetAsync(original.Id, CancellationToken.None));
        Assert.Equal(original, Assert.Single(
            await store.ListByOwnerAsync(original.Owner, CancellationToken.None)));
    }

    [Fact]
    public async Task ConcurrentAddsAndReads_PreserveEveryDistinctId()
    {
        var store = new InMemoryWorkingMemoryStore();
        var items = Enumerable.Range(0, 128).Select(index => CreateItem($"memory-{index}")).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = items.Select(item => Task.Run(async () =>
        {
            await start.Task;
            await store.AddAsync(item, CancellationToken.None);
            Assert.Equal(item, await store.GetAsync(item.Id, CancellationToken.None));
            Assert.Contains(item, await store.ListByOwnerAsync(item.Owner, CancellationToken.None));
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        var result = await store.ListByOwnerAsync(items[0].Owner, CancellationToken.None);
        Assert.Equal(items.Length, result.Count);
        Assert.Equal(items.Length, result.Select(item => item.Id).Distinct().Count());
        Assert.All(items, item => Assert.Contains(item, result));
    }

    [Fact]
    public async Task ConcurrentDuplicateAdds_AllowExactlyOneWinner()
    {
        var store = new InMemoryWorkingMemoryStore();
        var candidates = Enumerable.Range(0, 32)
            .Select(index => CreateItem() with { Content = $"candidate-{index}" }).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = candidates.Select(item => Task.Run(async () =>
        {
            await start.Task;
            return await Record.ExceptionAsync(() => store.AddAsync(item, CancellationToken.None));
        })).ToArray();

        start.SetResult();
        var errors = await Task.WhenAll(tasks);

        Assert.Single(errors, error => error is null);
        Assert.All(errors.Where(error => error is not null), error =>
            Assert.IsType<InvalidOperationException>(error));
        Assert.Contains(
            Assert.Single(await store.ListByOwnerAsync(candidates[0].Owner, CancellationToken.None)),
            candidates);
    }

    [Fact]
    public async Task ConcurrentUpdatesAndReads_KeepWholePayloadAndMaximumTimestamp()
    {
        var store = new InMemoryWorkingMemoryStore();
        var original = CreateItem();
        await store.AddAsync(original, CancellationToken.None);
        var statuses = Enum.GetValues<MemoryStatus>();
        var candidates = Enumerable.Range(1, 64).Select(index => original with
        {
            Content = $"content-{index}",
            Source = $"source-{index}",
            Confidence = index / 64d,
            Importance = 1 - index / 64d,
            Status = statuses[index % statuses.Length],
            UpdatedAt = InitialTime.AddSeconds(index),
            ExpiresAt = InitialTime.AddMinutes(index)
        }).ToArray();
        var candidatePayloads = candidates.Select(item => item with { UpdatedAt = InitialTime }).ToArray();
        var validPayloads = candidatePayloads.Append(original).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = candidates.Select(candidate => Task.Run(async () =>
        {
            await start.Task;
            var update = new MemoryUpdateRequest(
                candidate.Content, candidate.Source, candidate.Confidence,
                candidate.Importance, candidate.Status, candidate.ExpiresAt);
            var result = await store.UpdateAsync(
                candidate.Id, update, candidate.UpdatedAt, CancellationToken.None);

            Assert.Equal(candidate with { UpdatedAt = result.UpdatedAt }, result);
            Assert.True(result.UpdatedAt >= candidate.UpdatedAt);
        })).ToArray();
        var reader = Task.Run(async () =>
        {
            await start.Task;
            var previousUpdatedAt = InitialTime;
            for (var index = 0; index < 128; index++)
            {
                var current = Assert.IsType<MemoryItem>(
                    await store.GetAsync(original.Id, CancellationToken.None));
                Assert.Contains(current with { UpdatedAt = InitialTime }, validPayloads);
                Assert.True(current.UpdatedAt >= previousUpdatedAt);
                previousUpdatedAt = current.UpdatedAt;
            }
        });

        start.SetResult();
        await Task.WhenAll(writers.Append(reader));

        var final = Assert.IsType<MemoryItem>(
            await store.GetAsync(original.Id, CancellationToken.None));
        Assert.Equal(candidates.Max(item => item.UpdatedAt), final.UpdatedAt);
        Assert.Contains(final with { UpdatedAt = InitialTime }, candidatePayloads);
    }

    [Fact]
    public async Task ConcurrentDeletesAndReads_RemoveEveryDistinctId()
    {
        var store = new InMemoryWorkingMemoryStore();
        var items = Enumerable.Range(0, 64).Select(index => CreateItem($"memory-{index}")).ToArray();
        foreach (var item in items)
        {
            await store.AddAsync(item, CancellationToken.None);
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = items.Select(item => Task.Run(async () =>
        {
            await start.Task;
            await store.DeleteAsync(item.Id, CancellationToken.None);
            Assert.Null(await store.GetAsync(item.Id, CancellationToken.None));
            Assert.DoesNotContain(item, await store.ListByOwnerAsync(item.Owner, CancellationToken.None));
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        Assert.Empty(await store.ListByOwnerAsync(items[0].Owner, CancellationToken.None));
    }

    private static MemoryItem CreateItem(string id = "memory-1", MemoryOwner? owner = null)
    {
        return new MemoryItem(
            new MemoryId(id), owner ?? new MemoryOwner(MemoryScope.Global), MemoryType.Fact,
            "original", "test", 0.8, 0.6, MemoryStatus.Active,
            InitialTime, InitialTime, InitialTime.AddHours(1));
    }

    private static MemoryUpdateRequest CreateUpdate(string content = "updated", DateTimeOffset? expiresAt = null)
    {
        return new MemoryUpdateRequest(content, "tool", 0.9, 0.7, MemoryStatus.Confirmed, expiresAt);
    }
}
