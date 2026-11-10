using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Tests.Support;

namespace HanyingAgent.Tests.WorkingMemory;

public sealed class LocalWorkingMemoryManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static MemoryOwner Global => new(MemoryScope.Global);

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LocalWorkingMemoryManager(null!, new ManualTimeProvider(Now)));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalWorkingMemoryManager(new RecordingStore(), null!));
    }

    [Fact]
    public async Task WriteAsync_GeneratesIdAndInitialStateAndPreservesPayload()
    {
        var store = new RecordingStore();
        var clock = new ManualTimeProvider(Now);
        IWorkingMemoryManager manager = new LocalWorkingMemoryManager(store, clock);
        using var cancellation = new CancellationTokenSource();
        var request = WriteRequest() with
        {
            Owner = new MemoryOwner(MemoryScope.User, userId: new UserId("user-1")),
            Type = MemoryType.Preference,
            Content = " content ",
            Source = " source ",
            ExpiresAt = Now.AddHours(1)
        };

        var result = await manager.WriteAsync(request, cancellation.Token);

        Assert.False(string.IsNullOrWhiteSpace(result.Id.Value));
        Assert.Equal(new MemoryItem(
            result.Id, request.Owner, request.Type, request.Content, request.Source,
            request.Confidence, request.Importance, MemoryStatus.Active,
            Now, Now, request.ExpiresAt), result);
        Assert.Same(result, store.Added);
        Assert.Equal("Add", store.Operation);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(cancellation.Token, store.Token);

        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await manager.WriteAsync(request, CancellationToken.None);
        Assert.NotEqual(result.Id, second.Id);
        Assert.Equal(Now.AddSeconds(1), second.CreatedAt);
        Assert.Equal(second.CreatedAt, second.UpdatedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task WriteAndUpdate_AcceptExpirationWithoutRejectingPastOrPresent(int tickOffset)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        var expiration = Now.AddTicks(tickOffset);

        var written = await manager.WriteAsync(
            WriteRequest() with { ExpiresAt = expiration }, CancellationToken.None);
        await manager.UpdateAsync(
            written.Id, UpdateRequest() with { ExpiresAt = expiration }, CancellationToken.None);

        Assert.Equal(expiration, written.ExpiresAt);
        Assert.Equal(expiration, store.Update!.ExpiresAt);
    }

    [Fact]
    public async Task UpdateAsync_DelegatesFullRequestAndReturnsActualStoredResult()
    {
        var storedResult = Item() with { UpdatedAt = Now.AddHours(1) };
        var store = new RecordingStore { UpdateResult = storedResult };
        var manager = CreateManager(store);
        var update = UpdateRequest() with { ExpiresAt = null };
        var id = new MemoryId("memory-1");
        using var cancellation = new CancellationTokenSource();

        var result = await manager.UpdateAsync(id, update, cancellation.Token);

        Assert.Same(storedResult, result);
        Assert.Same(update, store.Update);
        Assert.Equal(id, store.MemoryId);
        Assert.Equal(Now, store.UpdatedAt);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal("Update", store.Operation);
        Assert.Equal(1, store.CallCount);
    }

    [Theory]
    [InlineData(MemoryStatus.Active)]
    [InlineData(MemoryStatus.Confirmed)]
    [InlineData(MemoryStatus.Superseded)]
    [InlineData(MemoryStatus.Contradicted)]
    [InlineData(MemoryStatus.Expired)]
    public async Task UpdateAsync_AcceptsEveryDefinedStatusWithoutReadingOldState(MemoryStatus status)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);

        await manager.UpdateAsync(new MemoryId("memory-1"),
            UpdateRequest() with { Status = status }, CancellationToken.None);

        Assert.Equal(status, store.Update!.Status);
        Assert.Equal(1, store.CallCount);
        Assert.Equal("Update", store.Operation);
    }

    [Fact]
    public async Task UpdateAsync_PassesClockRollbackToStoreInsteadOfDoingReadModifyWrite()
    {
        var store = new RecordingStore();
        var clock = new ManualTimeProvider(Now);
        var manager = new LocalWorkingMemoryManager(store, clock);
        clock.SetUtcNow(Now.AddMinutes(-1));

        await manager.UpdateAsync(new MemoryId("memory-1"), UpdateRequest(), CancellationToken.None);

        Assert.Equal(Now.AddMinutes(-1), store.UpdatedAt);
        Assert.Equal(1, store.CallCount);
        Assert.Equal("Update", store.Operation);
    }

    [Fact]
    public async Task ForgetAsync_DelegatesPhysicalDeletionWithSameToken()
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        var id = new MemoryId("memory-1");
        using var cancellation = new CancellationTokenSource();

        await manager.ForgetAsync(id, cancellation.Token);

        Assert.Equal("Delete", store.Operation);
        Assert.Equal(id, store.MemoryId);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal(1, store.CallCount);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Update")]
    [InlineData("List")]
    public async Task Operations_RejectNullRequestBeforeCallingStore(string operation)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        Func<Task> call = operation switch
        {
            "Write" => () => manager.WriteAsync(null!, CancellationToken.None),
            "Update" => () => manager.UpdateAsync(new MemoryId("memory-1"), null!, CancellationToken.None),
            "List" => () => manager.ListRecentAsync(null!, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await Assert.ThrowsAsync<ArgumentNullException>(call);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Forget")]
    public async Task Operations_RejectDefaultIdBeforeCallingStore(string operation)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => operation == "Update"
            ? manager.UpdateAsync(default, UpdateRequest(), CancellationToken.None)
            : manager.ForgetAsync(default, CancellationToken.None));

        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("List")]
    public async Task Operations_RejectNullOwnerBeforeCallingStore(string operation)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() => operation == "Write"
            ? manager.WriteAsync(WriteRequest() with { Owner = null! }, CancellationToken.None)
            : manager.ListRecentAsync(new MemoryReadRequest(null!), CancellationToken.None));

        Assert.Equal(0, store.CallCount);
    }

    public static IEnumerable<object?[]> InvalidContentCases()
    {
        foreach (var operation in new[] { "Write", "Update" })
        {
            foreach (var field in new[] { "Content", "Source" })
            {
                foreach (var value in new string?[] { null, "", " ", "\t\r\n" })
                {
                    yield return [operation, field, value];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidContentCases))]
    public async Task Operations_RejectMissingContentAndSource(string operation, string field, string? value)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        var write = WriteRequest();
        var update = UpdateRequest();
        write = field == "Content" ? write with { Content = value! } : write with { Source = value! };
        update = field == "Content" ? update with { Content = value! } : update with { Source = value! };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => operation == "Write"
            ? manager.WriteAsync(write, CancellationToken.None)
            : manager.UpdateAsync(new MemoryId("memory-1"), update, CancellationToken.None));

        Assert.Equal(0, store.CallCount);
    }

    public static IEnumerable<object[]> InvalidScoreCases()
    {
        foreach (var operation in new[] { "Write", "Update" })
        {
            foreach (var field in new[] { "Confidence", "Importance" })
            {
                foreach (var value in new[] { -0.01, 1.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    yield return [operation, field, value];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidScoreCases))]
    public async Task Operations_RejectInvalidScores(string operation, string field, double value)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        var write = WriteRequest();
        var update = UpdateRequest();
        write = field == "Confidence" ? write with { Confidence = value } : write with { Importance = value };
        update = field == "Confidence" ? update with { Confidence = value } : update with { Importance = value };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => operation == "Write"
            ? manager.WriteAsync(write, CancellationToken.None)
            : manager.UpdateAsync(new MemoryId("memory-1"), update, CancellationToken.None));

        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task Operations_AcceptInclusiveScoreBounds(double confidence, double importance)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);

        var result = await manager.WriteAsync(
            WriteRequest() with { Confidence = confidence, Importance = importance }, CancellationToken.None);
        await manager.UpdateAsync(result.Id,
            UpdateRequest() with { Confidence = confidence, Importance = importance }, CancellationToken.None);

        Assert.Equal(confidence, result.Confidence);
        Assert.Equal(importance, result.Importance);
        Assert.Equal(confidence, store.Update!.Confidence);
        Assert.Equal(importance, store.Update.Importance);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Update")]
    [InlineData("List")]
    public async Task Operations_RejectUnknownEnumsBeforeCallingStore(string operation)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        Func<Task> call = operation switch
        {
            "Write" => () => manager.WriteAsync(
                WriteRequest() with { Type = (MemoryType)99 }, CancellationToken.None),
            "Update" => () => manager.UpdateAsync(new MemoryId("memory-1"),
                UpdateRequest() with { Status = (MemoryStatus)99 }, CancellationToken.None),
            "List" => () => manager.ListRecentAsync(new MemoryReadRequest(Global, (MemoryType)99), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(call);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task ListRecentAsync_RejectsNonPositiveLimit(int limit)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            manager.ListRecentAsync(new MemoryReadRequest(Global, Limit: limit), CancellationToken.None));

        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData(MemoryScope.Task)]
    [InlineData(MemoryScope.Session)]
    [InlineData(MemoryScope.User)]
    [InlineData(MemoryScope.Global)]
    public async Task ListRecentAsync_RequestsExactOwnerFromStore(MemoryScope scope)
    {
        var owner = scope switch
        {
            MemoryScope.Task => new MemoryOwner(scope, new SessionId("session-1"), new TaskId("task-1")),
            MemoryScope.Session => new MemoryOwner(scope, new SessionId("session-1")),
            MemoryScope.User => new MemoryOwner(scope, userId: new UserId("user-1")),
            _ => Global
        };
        var item = Item() with { Owner = owner };
        var store = new RecordingStore { Candidates = [item] };
        var manager = CreateManager(store);
        using var cancellation = new CancellationTokenSource();

        var result = await manager.ListRecentAsync(new MemoryReadRequest(owner), cancellation.Token);

        Assert.Same(owner, store.Owner);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal("List", store.Operation);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(item, Assert.Single(result));
    }

    [Theory]
    [InlineData(MemoryStatus.Active, true)]
    [InlineData(MemoryStatus.Confirmed, true)]
    [InlineData(MemoryStatus.Superseded, false)]
    [InlineData(MemoryStatus.Contradicted, false)]
    [InlineData(MemoryStatus.Expired, false)]
    public async Task ListRecentAsync_ReturnsOnlyReadableStates(MemoryStatus status, bool readable)
    {
        var item = Item() with { Status = status };
        var store = new RecordingStore { Candidates = [item] };

        var result = await CreateManager(store).ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);

        Assert.Equal(readable ? 1 : 0, result.Count);
        Assert.Equal(item, Assert.Single(store.Candidates));
        Assert.Equal(1, store.CallCount);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task ListRecentAsync_UsesStrictExpirationBoundary(int? tickOffset, bool visible)
    {
        var store = new RecordingStore
        {
            Candidates = [Item() with { ExpiresAt = tickOffset is { } ticks ? Now.AddTicks(ticks) : null }]
        };

        var result = await CreateManager(store).ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);

        Assert.Equal(visible ? 1 : 0, result.Count);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task ListRecentAsync_UsesInjectedClockWhenTimeAdvances()
    {
        var original = Item() with { ExpiresAt = Now.AddSeconds(1) };
        var store = new RecordingStore { Candidates = [original] };
        var clock = new ManualTimeProvider(Now);
        var manager = new LocalWorkingMemoryManager(store, clock);

        Assert.Single(await manager.ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(await manager.ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None));
        Assert.Equal(original, Assert.Single(store.Candidates));
    }

    [Fact]
    public async Task ListRecentAsync_UsesOneNowAfterAwaitingSnapshot()
    {
        var clock = new CountingTimeProvider(Now);
        var pending = new TaskCompletionSource<IReadOnlyList<MemoryItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore { PendingList = pending.Task };
        var manager = new LocalWorkingMemoryManager(store, clock);

        var query = manager.ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);
        Assert.False(query.IsCompleted);
        Assert.Equal(0, clock.ReadCount);
        clock.Now = Now.AddMinutes(1);
        pending.SetResult([
            Item("expired-during-read") with { ExpiresAt = Now.AddSeconds(30) },
            Item("a") with { ExpiresAt = clock.Now.AddTicks(1) },
            Item("b") with { ExpiresAt = clock.Now.AddTicks(1) }
        ]);
        var result = await query;

        Assert.Equal(new[] { "a", "b" }, result.Select(item => item.Id.Value));
        Assert.Equal(1, clock.ReadCount);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(MemoryType.Fact, 1)]
    [InlineData(MemoryType.Preference, 1)]
    [InlineData(MemoryType.Decision, 0)]
    public async Task ListRecentAsync_AppliesOptionalType(MemoryType? type, int count)
    {
        var store = new RecordingStore
        {
            Candidates = [Item("fact"), Item("preference") with { Type = MemoryType.Preference }]
        };

        var result = await CreateManager(store).ListRecentAsync(
            new MemoryReadRequest(Global, type), CancellationToken.None);

        Assert.Equal(count, result.Count);
        if (type is not null)
        {
            Assert.All(result, item => Assert.Equal(type, item.Type));
        }
    }

    [Fact]
    public async Task ListRecentAsync_FiltersThenSortsOrdinallyThenLimits()
    {
        var store = new RecordingStore
        {
            Candidates =
            [
                Item("a"), Item("Z"),
                Item("older") with { UpdatedAt = Now.AddMinutes(-1), Importance = 1 },
                Item("newest") with { UpdatedAt = Now.AddMinutes(1), Importance = 0 },
                Item("expired") with { ExpiresAt = Now, UpdatedAt = Now.AddHours(1) },
                Item("inactive") with { Status = MemoryStatus.Expired, UpdatedAt = Now.AddHours(1) },
                Item("other-type") with { Type = MemoryType.Preference, UpdatedAt = Now.AddHours(1) }
            ]
        };

        var result = await CreateManager(store).ListRecentAsync(
            new MemoryReadRequest(Global, MemoryType.Fact, Limit: 3), CancellationToken.None);

        Assert.Equal(new[] { "newest", "Z", "a" }, result.Select(item => item.Id.Value));
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(int.MaxValue, 25)]
    public async Task ListRecentAsync_AppliesDefaultAndExplicitLimits(int? limit, int expectedCount)
    {
        var store = new RecordingStore
        {
            Candidates = Enumerable.Range(0, 25).Select(index => Item($"memory-{index:D2}")).Reverse().ToArray()
        };
        var request = limit is { } value ? new MemoryReadRequest(Global, Limit: value) : new MemoryReadRequest(Global);

        var result = await CreateManager(store).ListRecentAsync(request, CancellationToken.None);

        Assert.Equal(expectedCount, result.Count);
        Assert.Equal(Enumerable.Range(0, expectedCount).Select(index => $"memory-{index:D2}"),
            result.Select(item => item.Id.Value));
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsDetachedMaterializedResult()
    {
        var original = Item();
        MemoryItem[] candidates = [original];
        var store = new RecordingStore { Candidates = candidates };
        var result = await CreateManager(store).ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);

        candidates[0] = original with { Content = "changed" };

        Assert.Equal(original, Assert.Single(result));
        Assert.NotSame(candidates, result);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsEmptyForEmptySnapshot()
    {
        var result = await CreateManager(new RecordingStore())
            .ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Update")]
    [InlineData("Forget")]
    [InlineData("List")]
    public async Task Operations_ObservePreCancellationBeforeCallingStore(string operation)
    {
        var store = new RecordingStore();
        var manager = CreateManager(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CallAsync(manager, operation, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, store.CallCount);
    }

    public static IEnumerable<object[]> StoreFailureCases()
    {
        foreach (var operation in new[] { "Write", "Update", "Forget", "List" })
        {
            foreach (var failure in new[] { "Storage", "Missing", "Cancelled" })
            {
                yield return [operation, failure];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StoreFailureCases))]
    public async Task Operations_PropagateStoreFailureWithoutRetry(string operation, string failure)
    {
        using var cancellation = new CancellationTokenSource();
        Exception expected = failure switch
        {
            "Storage" => new InvalidOperationException("Storage failed."),
            "Missing" => new KeyNotFoundException("Missing target."),
            "Cancelled" => new OperationCanceledException(cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var store = new RecordingStore { Failure = expected };

        var actual = await Record.ExceptionAsync(() =>
            CallAsync(CreateManager(store), operation, cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(1, store.CallCount);
        Assert.Equal(cancellation.Token, store.Token);
    }

    [Fact]
    public async Task ListRecentAsync_ObservesCancellationAfterPendingRead()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<MemoryItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingStore { PendingList = pending.Task };
        using var cancellation = new CancellationTokenSource();
        var query = CreateManager(store).ListRecentAsync(new MemoryReadRequest(Global), cancellation.Token);
        cancellation.Cancel();
        pending.SetResult([Item()]);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, store.CallCount);
    }

    private static LocalWorkingMemoryManager CreateManager(RecordingStore store) =>
        new(store, new ManualTimeProvider(Now));

    private static MemoryWriteRequest WriteRequest() =>
        new(Global, MemoryType.Fact, "content", "source", 0.8, 0.6);

    private static MemoryUpdateRequest UpdateRequest() =>
        new("updated", "tool", 0.9, 0.7, MemoryStatus.Confirmed, null);

    private static MemoryItem Item(string id = "memory-1") =>
        new(new MemoryId(id), Global, MemoryType.Fact, "content", "source", 0.8, 0.6,
            MemoryStatus.Active, Now, Now, null);

    private static Task CallAsync(IWorkingMemoryManager manager, string operation, CancellationToken token) =>
        operation switch
        {
            "Write" => manager.WriteAsync(WriteRequest(), token),
            "Update" => manager.UpdateAsync(new MemoryId("memory-1"), UpdateRequest(), token),
            "Forget" => manager.ForgetAsync(new MemoryId("memory-1"), token),
            "List" => manager.ListRecentAsync(new MemoryReadRequest(Global), token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public int ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            ReadCount++;
            return Now.AddTicks(ReadCount - 1);
        }
    }

    private sealed class RecordingStore : IWorkingMemoryStore
    {
        public int CallCount { get; private set; }
        public string? Operation { get; private set; }
        public CancellationToken Token { get; private set; }
        public MemoryItem? Added { get; private set; }
        public MemoryId MemoryId { get; private set; }
        public MemoryUpdateRequest? Update { get; private set; }
        public DateTimeOffset UpdatedAt { get; private set; }
        public MemoryOwner? Owner { get; private set; }
        public MemoryItem UpdateResult { get; init; } = Item();
        public IReadOnlyList<MemoryItem> Candidates { get; init; } = [];
        public Task<IReadOnlyList<MemoryItem>>? PendingList { get; init; }
        public Exception? Failure { get; init; }

        public Task AddAsync(MemoryItem item, CancellationToken cancellationToken)
        {
            Record("Add", cancellationToken);
            Added = item;
            return Task.CompletedTask;
        }

        public Task<MemoryItem?> GetAsync(MemoryId memoryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The manager must not perform read-modify-write updates.");

        public Task<MemoryItem> UpdateAsync(
            MemoryId memoryId, MemoryUpdateRequest update,
            DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            Record("Update", cancellationToken);
            MemoryId = memoryId;
            Update = update;
            UpdatedAt = updatedAt;
            return Task.FromResult(UpdateResult);
        }

        public Task DeleteAsync(MemoryId memoryId, CancellationToken cancellationToken)
        {
            Record("Delete", cancellationToken);
            MemoryId = memoryId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryItem>> ListByOwnerAsync(
            MemoryOwner owner, CancellationToken cancellationToken)
        {
            Record("List", cancellationToken);
            Owner = owner;
            return PendingList ?? Task.FromResult(Candidates);
        }

        private void Record(string operation, CancellationToken token)
        {
            CallCount++;
            Operation = operation;
            Token = token;
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
