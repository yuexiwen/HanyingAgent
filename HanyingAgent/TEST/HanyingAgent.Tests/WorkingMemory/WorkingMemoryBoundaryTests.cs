using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;
using HanyingAgent.Infrastructure.Persistence;
using HanyingAgent.Tests.Support;

namespace HanyingAgent.Tests.WorkingMemory;

public sealed class WorkingMemoryBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkingMemoryStore _store = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly IWorkingMemoryManager _manager;

    public WorkingMemoryBoundaryTests()
    {
        _manager = new LocalWorkingMemoryManager(_store, _clock);
    }

    private static MemoryOwner Global => new(MemoryScope.Global);

    [Fact]
    public async Task Mutations_AreVisibleThroughManagerAndPersistTheReturnedRecord()
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        Assert.Equal(original, Assert.Single(await ReadAsync()));
        Assert.Equal(original, await _store.GetAsync(original.Id, CancellationToken.None));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var update = UpdateRequest("revised") with { ExpiresAt = Now.AddHours(1) };
        var updated = await _manager.UpdateAsync(original.Id, update, CancellationToken.None);
        Assert.Equal(original with
        {
            Content = update.Content,
            Source = update.Source,
            Confidence = update.Confidence,
            Importance = update.Importance,
            Status = update.Status,
            UpdatedAt = Now.AddMinutes(1),
            ExpiresAt = update.ExpiresAt
        }, updated);
        Assert.Equal(updated, Assert.Single(await ReadAsync()));
        Assert.Equal(updated, await _store.GetAsync(original.Id, CancellationToken.None));

        await _manager.ForgetAsync(original.Id, CancellationToken.None);

        Assert.Empty(await ReadAsync());
        Assert.Null(await _store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task OwnerReads_IsolateExactScopeAndIdsWithoutIncludingRelatedScopes()
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
            items.Add(await _manager.WriteAsync(
                WriteRequest() with { Owner = owner }, CancellationToken.None));
        }

        foreach (var expected in items)
        {
            var equivalentOwner = new MemoryOwner(
                expected.Owner.Scope, expected.Owner.SessionId, expected.Owner.TaskId, expected.Owner.UserId);
            var result = await _manager.ListRecentAsync(new MemoryReadRequest(equivalentOwner), CancellationToken.None);

            Assert.Equal(expected, Assert.Single(result));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ExpirationInput_IsSavedEvenWhenAlreadyExpired(int tickOffset)
    {
        var expiration = Now.AddTicks(tickOffset);
        var item = await _manager.WriteAsync(
            WriteRequest() with { ExpiresAt = expiration }, CancellationToken.None);

        Assert.Equal(expiration, item.ExpiresAt);
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
        Assert.Equal(tickOffset > 0 ? 1 : 0, (await ReadAsync()).Count);
    }

    [Fact]
    public async Task ExpirationBoundary_ChangesVisibilityWithoutChangingStoredRecord()
    {
        var item = await _manager.WriteAsync(
            WriteRequest() with { ExpiresAt = Now.AddTicks(1) }, CancellationToken.None);
        var noExpiration = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        Assert.Equal(2, (await ReadAsync()).Count);

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(noExpiration, Assert.Single(await ReadAsync()));
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(noExpiration, Assert.Single(await ReadAsync()));
        Assert.Equal(item, await _store.GetAsync(item.Id, CancellationToken.None));
        Assert.Equal(2, (await _store.ListByOwnerAsync(Global, CancellationToken.None)).Count);
    }

    public static IEnumerable<object[]> StateTransitions()
    {
        foreach (var before in Enum.GetValues<MemoryStatus>())
        {
            foreach (var after in Enum.GetValues<MemoryStatus>())
            {
                yield return [before, after];
            }
        }
    }

    [Theory]
    [MemberData(nameof(StateTransitions))]
    public async Task StatusTransitions_AcceptAllDefinedPairsAndControlVisibility(
        MemoryStatus before, MemoryStatus after)
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var first = await _manager.UpdateAsync(original.Id,
            UpdateRequest("first") with { Status = before }, CancellationToken.None);
        Assert.Equal(IsReadable(before) ? 1 : 0, (await ReadAsync()).Count);
        Assert.Equal(first, await _store.GetAsync(original.Id, CancellationToken.None));

        var second = await _manager.UpdateAsync(original.Id,
            UpdateRequest("second") with { Status = after }, CancellationToken.None);

        var result = await ReadAsync();
        Assert.Equal(IsReadable(after) ? 1 : 0, result.Count);
        if (IsReadable(after))
        {
            Assert.Equal(second, Assert.Single(result));
        }
        Assert.Equal(second, await _store.GetAsync(original.Id, CancellationToken.None));
        Assert.Equal(original.Id, second.Id);
        Assert.Equal(original.Owner, second.Owner);
        Assert.Equal(original.Type, second.Type);
        Assert.Equal(original.CreatedAt, second.CreatedAt);
    }

    [Theory]
    [InlineData(MemoryStatus.Active)]
    [InlineData(MemoryStatus.Confirmed)]
    public async Task Reactivation_DoesNotBypassExpirationAndNullUpdateCanClearIt(MemoryStatus status)
    {
        var item = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        await _manager.UpdateAsync(item.Id, UpdateRequest() with
        {
            Status = MemoryStatus.Expired,
            ExpiresAt = Now
        }, CancellationToken.None);

        var reactivated = await _manager.UpdateAsync(item.Id, UpdateRequest() with
        {
            Status = status,
            ExpiresAt = Now
        }, CancellationToken.None);

        Assert.Empty(await ReadAsync());
        Assert.Equal(reactivated, await _store.GetAsync(item.Id, CancellationToken.None));

        var cleared = await _manager.UpdateAsync(item.Id,
            UpdateRequest() with { Status = status, ExpiresAt = null }, CancellationToken.None);

        Assert.Null(cleared.ExpiresAt);
        Assert.Equal(cleared, Assert.Single(await ReadAsync()));
    }

    [Fact]
    public async Task UpdatingExpiration_CanHideAndRestoreARecord()
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var expired = await _manager.UpdateAsync(original.Id,
            UpdateRequest() with { ExpiresAt = Now.AddTicks(-1) }, CancellationToken.None);
        Assert.Empty(await ReadAsync());
        Assert.Equal(expired, await _store.GetAsync(original.Id, CancellationToken.None));

        var extended = await _manager.UpdateAsync(original.Id,
            UpdateRequest() with { ExpiresAt = Now.AddTicks(1) }, CancellationToken.None);

        Assert.Equal(extended, Assert.Single(await ReadAsync()));
    }

    [Fact]
    public async Task ClockRollback_LastAppliedContentWinsButUpdatedAtDoesNotRegress()
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var newer = await _manager.UpdateAsync(original.Id,
            UpdateRequest("newer-time"), CancellationToken.None);
        _clock.SetUtcNow(Now.AddMinutes(1));

        var lastApplied = await _manager.UpdateAsync(original.Id,
            UpdateRequest("last-applied"), CancellationToken.None);

        Assert.Equal(newer with { Content = "last-applied" }, lastApplied);
        Assert.Equal(Now.AddMinutes(2), lastApplied.UpdatedAt);
        Assert.Equal(original.CreatedAt, lastApplied.CreatedAt);
        Assert.Equal(lastApplied, Assert.Single(await ReadAsync()));
        Assert.Equal(lastApplied, await _store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task FilteringAndOrdering_HappenBeforeDefaultAndExplicitLimits()
    {
        var facts = new List<MemoryItem>();
        for (var index = 0; index < 25; index++)
        {
            facts.Add(await _manager.WriteAsync(
                WriteRequest() with { Content = $"fact-{index}" }, CancellationToken.None));
        }
        _clock.Advance(TimeSpan.FromMinutes(1));
        var newest = await _manager.UpdateAsync(facts[0].Id,
            UpdateRequest("newest") with { Importance = 0 }, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _manager.WriteAsync(WriteRequest() with { Type = MemoryType.Preference }, CancellationToken.None);
        await _manager.WriteAsync(WriteRequest() with { ExpiresAt = _clock.GetUtcNow() }, CancellationToken.None);
        var inactive = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        await _manager.UpdateAsync(inactive.Id, UpdateRequest() with
        {
            Status = MemoryStatus.Superseded
        }, CancellationToken.None);
        var expected = new[] { newest }.Concat(
            facts.Skip(1).OrderBy(item => item.Id.Value, StringComparer.Ordinal)).ToArray();

        var defaultPage = await _manager.ListRecentAsync(
            new MemoryReadRequest(Global, MemoryType.Fact), CancellationToken.None);
        var smallPage = await _manager.ListRecentAsync(
            new MemoryReadRequest(Global, MemoryType.Fact, Limit: 3), CancellationToken.None);
        var all = await _manager.ListRecentAsync(
            new MemoryReadRequest(Global, MemoryType.Fact, Limit: 100), CancellationToken.None);

        Assert.Equal(expected.Take(20), defaultPage);
        Assert.Equal(expected.Take(3), smallPage);
        Assert.Equal(expected, all);
    }

    [Fact]
    public async Task ReturnedSnapshots_DoNotChangeAfterUpdatesAddsOrDeletes()
    {
        var first = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var second = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var before = await ReadAsync();
        var expectedBefore = before.ToArray();
        var rawBefore = await _store.ListByOwnerAsync(Global, CancellationToken.None);
        var expectedRaw = rawBefore.ToArray();

        _clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await _manager.UpdateAsync(first.Id, UpdateRequest(), CancellationToken.None);
        await _manager.ForgetAsync(second.Id, CancellationToken.None);
        var added = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);

        Assert.Equal(expectedBefore, before);
        Assert.Equal(expectedRaw, rawBefore);
        Assert.Contains(first, before);
        Assert.Contains(second, before);
        var current = await ReadAsync();
        Assert.Equal(2, current.Count);
        Assert.Contains(updated, current);
        Assert.Contains(added, current);
        Assert.DoesNotContain(second, current);
    }

    [Theory]
    [InlineData("Content")]
    [InlineData("Score")]
    [InlineData("Status")]
    public async Task InvalidUpdate_DoesNotPartiallyChangeStoredRecord(string invalidField)
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var update = invalidField switch
        {
            "Content" => UpdateRequest() with { Content = " " },
            "Score" => UpdateRequest() with { Confidence = double.NaN },
            "Status" => UpdateRequest() with { Status = (MemoryStatus)99 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };
        _clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _manager.UpdateAsync(original.Id, update, CancellationToken.None));

        Assert.Equal(original, await _store.GetAsync(original.Id, CancellationToken.None));
        Assert.Equal(original, Assert.Single(await ReadAsync()));
    }

    [Fact]
    public async Task UnknownAndDeletedTargets_FailWithoutRecreatingRecords()
    {
        var keeper = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var target = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        await _manager.ForgetAsync(target.Id, CancellationToken.None);

        foreach (var id in new[] { new MemoryId("unknown"), target.Id })
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _manager.UpdateAsync(id, UpdateRequest(), CancellationToken.None));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                _manager.ForgetAsync(id, CancellationToken.None));
            Assert.Null(await _store.GetAsync(id, CancellationToken.None));
        }
        Assert.Equal(keeper, Assert.Single(await ReadAsync()));
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Update")]
    [InlineData("Forget")]
    [InlineData("List")]
    public async Task PreCancellation_LeavesTheRealStoreUnchanged(string operation)
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        Func<Task> call = operation switch
        {
            "Write" => () => _manager.WriteAsync(WriteRequest(), token),
            "Update" => () => _manager.UpdateAsync(original.Id, UpdateRequest(), token),
            "Forget" => () => _manager.ForgetAsync(original.Id, token),
            "List" => () => _manager.ListRecentAsync(new MemoryReadRequest(Global), token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(call);

        Assert.Equal(token, error.CancellationToken);
        Assert.Equal(original, Assert.Single(await ReadAsync()));
        Assert.Equal(original, Assert.Single(await _store.ListByOwnerAsync(Global, CancellationToken.None)));
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Update")]
    public async Task CancellationBetweenValidationAndStorage_PreventsMutation(string operation)
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        IWorkingMemoryManager manager = new LocalWorkingMemoryManager(
            _store, new CancellingTimeProvider(cancellation));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation == "Write"
            ? manager.WriteAsync(WriteRequest(), cancellation.Token)
            : manager.UpdateAsync(original.Id, UpdateRequest(), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(original, Assert.Single(await ReadAsync()));
        Assert.Equal(original, Assert.Single(await _store.ListByOwnerAsync(Global, CancellationToken.None)));
    }

    [Fact]
    public async Task ConcurrentWritesAndQueries_DoNotLoseRecordsOrMixOwners()
    {
        var owners = new[]
        {
            new MemoryOwner(MemoryScope.Session, new SessionId("session-1")),
            new MemoryOwner(MemoryScope.Session, new SessionId("session-2"))
        };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 96).Select(index => Task.Run(async () =>
        {
            await start.Task;
            var owner = owners[index % owners.Length];
            var item = await _manager.WriteAsync(WriteRequest() with
            {
                Owner = owner,
                Content = $"record-{index}"
            }, CancellationToken.None);
            var page = await _manager.ListRecentAsync(new MemoryReadRequest(owner, Limit: 1000), CancellationToken.None);
            Assert.Contains(item, page);
            Assert.All(page, candidate => Assert.Equal(owner, candidate.Owner));
            Assert.Equal(page.Count, page.Select(candidate => candidate.Id).Distinct().Count());
            return item;
        })).ToArray();

        start.SetResult();
        var written = await Task.WhenAll(tasks);

        Assert.Equal(written.Length, written.Select(item => item.Id).Distinct().Count());
        foreach (var owner in owners)
        {
            var expected = written.Where(item => item.Owner == owner)
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal);
            var actual = await _manager.ListRecentAsync(
                new MemoryReadRequest(owner, Limit: 1000), CancellationToken.None);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task ConcurrentUpdatesAndQueries_ExposeWholePayloadsAndMonotonicUpdatedAt()
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        IWorkingMemoryManager manager = new LocalWorkingMemoryManager(_store, new TickingTimeProvider());
        var updates = Enumerable.Range(1, 64).Select(index => new MemoryUpdateRequest(
            $"content-{index}", $"source-{index}", index / 64d, 1 - index / 64d,
            index % 2 == 0 ? MemoryStatus.Active : MemoryStatus.Confirmed, Now.AddMinutes(index))).ToArray();
        var payloads = updates.Select(update => ApplyPayload(original, update)).ToArray();
        var readablePayloads = payloads.Append(original).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = updates.Select(update => Task.Run(async () =>
        {
            await start.Task;
            var result = await manager.UpdateAsync(original.Id, update, CancellationToken.None);
            Assert.Equal(ApplyPayload(original, update) with { UpdatedAt = result.UpdatedAt }, result);
            return result;
        })).ToArray();
        var reader = Task.Run(async () =>
        {
            await start.Task;
            var previousUpdatedAt = original.UpdatedAt;
            for (var index = 0; index < 128; index++)
            {
                var current = Assert.Single(await manager.ListRecentAsync(
                    new MemoryReadRequest(Global), CancellationToken.None));
                Assert.Contains(current with { UpdatedAt = original.UpdatedAt }, readablePayloads);
                Assert.True(current.UpdatedAt >= previousUpdatedAt);
                previousUpdatedAt = current.UpdatedAt;
            }
        });

        start.SetResult();
        await Task.WhenAll(writers.Append<Task>(reader));
        var results = await Task.WhenAll(writers);
        var final = Assert.Single(await ReadAsync());

        Assert.Equal(results.Max(item => item.UpdatedAt), final.UpdatedAt);
        Assert.Contains(final with { UpdatedAt = original.UpdatedAt }, payloads);
        Assert.Equal(final, await _store.GetAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentForgetAndUpdate_DoNotResurrectDeletedRecord()
    {
        var original = await _manager.WriteAsync(WriteRequest(), CancellationToken.None);
        var update = UpdateRequest();
        MemoryItem? updateResult = null;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updater = Task.Run(async () =>
        {
            await start.Task;
            return await Record.ExceptionAsync(async () =>
                updateResult = await _manager.UpdateAsync(original.Id, update, CancellationToken.None));
        });
        var deleter = Task.Run(async () =>
        {
            await start.Task;
            await _manager.ForgetAsync(original.Id, CancellationToken.None);
        });

        start.SetResult();
        await Task.WhenAll(updater, deleter);
        var updateError = await updater;

        if (updateError is null)
        {
            Assert.Equal(ApplyPayload(original, update), updateResult);
        }
        else
        {
            Assert.IsType<KeyNotFoundException>(updateError);
            Assert.Null(updateResult);
        }
        Assert.Empty(await ReadAsync());
        Assert.Null(await _store.GetAsync(original.Id, CancellationToken.None));
    }

    private Task<IReadOnlyList<MemoryItem>> ReadAsync() =>
        _manager.ListRecentAsync(new MemoryReadRequest(Global), CancellationToken.None);

    private static MemoryWriteRequest WriteRequest() =>
        new(Global, MemoryType.Fact, "original", "test", 0.8, 0.6);

    private static MemoryUpdateRequest UpdateRequest(string content = "updated") =>
        new(content, "tool", 0.9, 0.7, MemoryStatus.Confirmed, null);

    private static bool IsReadable(MemoryStatus status) =>
        status is MemoryStatus.Active or MemoryStatus.Confirmed;

    private static MemoryItem ApplyPayload(MemoryItem original, MemoryUpdateRequest update) =>
        original with
        {
            Content = update.Content,
            Source = update.Source,
            Confidence = update.Confidence,
            Importance = update.Importance,
            Status = update.Status,
            ExpiresAt = update.ExpiresAt
        };

    private sealed class CancellingTimeProvider(CancellationTokenSource cancellation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            cancellation.Cancel();
            return Now;
        }
    }

    private sealed class TickingTimeProvider : TimeProvider
    {
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => Now.AddTicks(Interlocked.Increment(ref _ticks));
    }
}
