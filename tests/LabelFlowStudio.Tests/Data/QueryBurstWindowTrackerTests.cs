using LabelFlowStudio.Data.Oracle.Repositories;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class QueryBurstWindowTrackerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly DateTime StartedAtUtc = new(2026, 8, 6, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Register_IncrementsCountWithinWindow_AndKeepsWindowStart()
    {
        var tracker = new QueryBurstWindowTracker(Window, capacity: 10);

        var first = tracker.Register("4462397", StartedAtUtc);
        var second = tracker.Register("4462397", StartedAtUtc.AddSeconds(30));

        Assert.Equal(new QueryBurstRegistration(StartedAtUtc, 1), first);
        Assert.Equal(new QueryBurstRegistration(StartedAtUtc, 2), second);
        Assert.Equal(1, tracker.TrackedKeyCount);
    }

    [Fact]
    public void Register_StartsNewWindowAfterPreviousWindowExpires()
    {
        var tracker = new QueryBurstWindowTracker(Window, capacity: 10);
        tracker.Register("4462397", StartedAtUtc);
        var nextWindowStartedAtUtc = StartedAtUtc.AddSeconds(30).AddTicks(1);

        var registration = tracker.Register("4462397", nextWindowStartedAtUtc);

        Assert.Equal(new QueryBurstRegistration(nextWindowStartedAtUtc, 1), registration);
        Assert.Equal(1, tracker.TrackedKeyCount);
    }

    [Fact]
    public void Register_RemovesExpiredKeysButRetainsActiveKeys()
    {
        var tracker = new QueryBurstWindowTracker(Window, capacity: 10);
        tracker.Register("expired", StartedAtUtc);
        tracker.Register("active", StartedAtUtc.AddSeconds(20));

        tracker.Register("new", StartedAtUtc.AddSeconds(31));

        Assert.Equal(2, tracker.TrackedKeyCount);
        var active = tracker.Register("active", StartedAtUtc.AddSeconds(31));
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public void Register_EvictsOldestKeyAndNeverExceedsCapacity()
    {
        var tracker = new QueryBurstWindowTracker(Window, capacity: 2);
        tracker.Register("oldest", StartedAtUtc);
        tracker.Register("middle", StartedAtUtc.AddSeconds(1));

        tracker.Register("newest", StartedAtUtc.AddSeconds(2));

        Assert.Equal(2, tracker.TrackedKeyCount);
        var evictedKey = tracker.Register("oldest", StartedAtUtc.AddSeconds(3));
        Assert.Equal(1, evictedKey.Count);
        Assert.Equal(2, tracker.TrackedKeyCount);
    }
}
