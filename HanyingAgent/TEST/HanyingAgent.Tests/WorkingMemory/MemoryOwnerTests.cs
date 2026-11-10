using System.Text.Json;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Tests.WorkingMemory;

public sealed class MemoryOwnerTests
{
    [Theory]
    [InlineData(MemoryScope.Task, "session-1", "task-1", null)]
    [InlineData(MemoryScope.Session, "session-1", null, null)]
    [InlineData(MemoryScope.User, null, null, "user-1")]
    [InlineData(MemoryScope.Global, null, null, null)]
    public void Constructor_AcceptsOnlyIdsForScope(
        MemoryScope scope, string? sessionId, string? taskId, string? userId)
    {
        var owner = CreateOwner(scope, sessionId, taskId, userId);

        Assert.Equal(scope, owner.Scope);
        Assert.Equal(sessionId, owner.SessionId?.Value);
        Assert.Equal(taskId, owner.TaskId?.Value);
        Assert.Equal(userId, owner.UserId?.Value);
    }

    [Theory]
    [InlineData(MemoryScope.Task, null, "task-1", null)]
    [InlineData(MemoryScope.Task, "session-1", null, null)]
    [InlineData(MemoryScope.Task, "session-1", "task-1", "user-1")]
    [InlineData(MemoryScope.Session, null, null, null)]
    [InlineData(MemoryScope.Session, "session-1", "task-1", null)]
    [InlineData(MemoryScope.Session, "session-1", null, "user-1")]
    [InlineData(MemoryScope.User, null, null, null)]
    [InlineData(MemoryScope.User, "session-1", null, "user-1")]
    [InlineData(MemoryScope.User, null, "task-1", "user-1")]
    [InlineData(MemoryScope.Global, "session-1", null, null)]
    [InlineData(MemoryScope.Global, null, "task-1", null)]
    [InlineData(MemoryScope.Global, null, null, "user-1")]
    public void Constructor_RejectsMissingOrExtraIds(
        MemoryScope scope, string? sessionId, string? taskId, string? userId)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateOwner(scope, sessionId, taskId, userId));
    }

    [Fact]
    public void Constructor_RejectsUnknownScope()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryOwner((MemoryScope)99));
    }

    [Fact]
    public void Constructor_RejectsDefaultSessionId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new MemoryOwner(MemoryScope.Session, sessionId: default(SessionId)));
    }

    [Fact]
    public void Constructor_RejectsDefaultTaskId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new MemoryOwner(MemoryScope.Task, new SessionId("session-1"), default(TaskId)));
    }

    [Fact]
    public void Constructor_RejectsDefaultUserId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new MemoryOwner(MemoryScope.User, userId: default(UserId)));
    }

    [Fact]
    public void Equality_UsesValuesRatherThanObjectIdentity()
    {
        var first = CreateOwner(MemoryScope.Task, "session-1", "task-1", null);
        var second = CreateOwner(MemoryScope.Task, "session-1", "task-1", null);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData(MemoryScope.Task, "session-1", "task-2", null)]
    [InlineData(MemoryScope.Task, "session-2", "task-1", null)]
    [InlineData(MemoryScope.Task, "Session-1", "task-1", null)]
    [InlineData(MemoryScope.Session, "session-1", null, null)]
    [InlineData(MemoryScope.User, null, null, "user-1")]
    [InlineData(MemoryScope.Global, null, null, null)]
    public void Equality_DistinguishesScopeAndExactIds(
        MemoryScope scope, string? sessionId, string? taskId, string? userId)
    {
        var taskOwner = CreateOwner(MemoryScope.Task, "session-1", "task-1", null);

        Assert.NotEqual(taskOwner, CreateOwner(scope, sessionId, taskId, userId));
    }

    [Theory]
    [InlineData(MemoryScope.Task, "session-1", "task-1", null)]
    [InlineData(MemoryScope.Session, "session-1", null, null)]
    [InlineData(MemoryScope.User, null, null, "user-1")]
    [InlineData(MemoryScope.Global, null, null, null)]
    public void Json_RoundTripsScopeAndIds(
        MemoryScope scope, string? sessionId, string? taskId, string? userId)
    {
        var owner = CreateOwner(scope, sessionId, taskId, userId);

        var restored = JsonSerializer.Deserialize<MemoryOwner>(JsonSerializer.Serialize(owner));

        Assert.Equal(owner, restored);
    }

    private static MemoryOwner CreateOwner(
        MemoryScope scope, string? sessionId, string? taskId, string? userId)
    {
        return new MemoryOwner(
            scope,
            sessionId is null ? null : new SessionId(sessionId),
            taskId is null ? null : new TaskId(taskId),
            userId is null ? null : new UserId(userId));
    }
}
