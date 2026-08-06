namespace LabelFlowStudio.Data.Oracle.Repositories;

/// <summary>
/// Tracks per-key query counts in fixed time windows while retaining only active windows.
/// </summary>
internal sealed class QueryBurstWindowTracker
{
    private readonly TimeSpan _window;
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<string, QueryBurstRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly PriorityQueue<WindowMarker, long> _oldestWindows = new();

    public QueryBurstWindowTracker(TimeSpan window, int capacity)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be greater than zero.");
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _window = window;
        _capacity = capacity;
    }

    internal int TrackedKeyCount
    {
        get
        {
            lock (_sync)
            {
                return _registrations.Count;
            }
        }
    }

    public QueryBurstRegistration Register(string key, DateTime startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            RemoveExpiredWindows(startedAtUtc);

            if (_registrations.TryGetValue(key, out var current))
            {
                var updated = current with { Count = current.Count + 1 };
                _registrations[key] = updated;
                return updated;
            }

            EnsureCapacityForNewKey();

            var registration = new QueryBurstRegistration(startedAtUtc, 1);
            _registrations.Add(key, registration);
            _oldestWindows.Enqueue(new WindowMarker(key, startedAtUtc), startedAtUtc.Ticks);

            return registration;
        }
    }

    private void RemoveExpiredWindows(DateTime startedAtUtc)
    {
        while (_oldestWindows.TryPeek(out var marker, out _))
        {
            if ((startedAtUtc - marker.WindowStartedAtUtc) <= _window)
            {
                return;
            }

            _oldestWindows.Dequeue();

            if (_registrations.TryGetValue(marker.Key, out var current)
                && current.WindowStartedAtUtc == marker.WindowStartedAtUtc)
            {
                _registrations.Remove(marker.Key);
            }
        }
    }

    private void EnsureCapacityForNewKey()
    {
        while (_registrations.Count >= _capacity
               && _oldestWindows.TryDequeue(out var marker, out _))
        {
            if (_registrations.TryGetValue(marker.Key, out var current)
                && current.WindowStartedAtUtc == marker.WindowStartedAtUtc)
            {
                _registrations.Remove(marker.Key);
            }
        }
    }

    private readonly record struct WindowMarker(string Key, DateTime WindowStartedAtUtc);
}

internal readonly record struct QueryBurstRegistration(DateTime WindowStartedAtUtc, int Count);
