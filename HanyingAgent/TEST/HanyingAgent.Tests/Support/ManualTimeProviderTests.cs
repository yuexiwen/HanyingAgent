namespace HanyingAgent.Tests.Support;

public sealed class ManualTimeProviderTests
{
    [Fact]
    public void GetUtcNow_NormalizesOffsetAndDoesNotAdvanceAutomatically()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        TimeProvider clock = new ManualTimeProvider(initial);

        Assert.Equal(initial.ToUniversalTime(), clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, clock.GetUtcNow().Offset);
        Assert.Equal(clock.GetUtcNow(), clock.GetUtcNow());
    }

    [Fact]
    public void Advance_MovesForwardByExactDuration()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(initial);

        clock.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(initial.AddTicks(1), clock.GetUtcNow());
    }

    [Fact]
    public void Advance_AllowsZeroDuration()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(initial);

        clock.Advance(TimeSpan.Zero);

        Assert.Equal(initial, clock.GetUtcNow());
    }

    [Fact]
    public void Advance_RejectsNegativeDurationWithoutChangingTime()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(initial);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)));
        Assert.Equal(initial, clock.GetUtcNow());
    }

    [Fact]
    public void SetUtcNow_AllowsClockRollback()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(initial);

        clock.SetUtcNow(initial.AddMinutes(-1));

        Assert.Equal(initial.AddMinutes(-1), clock.GetUtcNow());
    }

    [Fact]
    public void SetUtcNow_NormalizesOffset()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var next = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));

        clock.SetUtcNow(next);

        Assert.Equal(next.ToUniversalTime(), clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, clock.GetUtcNow().Offset);
    }
}
