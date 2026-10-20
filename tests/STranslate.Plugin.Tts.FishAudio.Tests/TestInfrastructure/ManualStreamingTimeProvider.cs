internal sealed class ManualStreamingTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_gate) return _ticks; }
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate) { _timers.Add(timer); timer.Change(dueTime, period); }
        return timer;
    }

    internal void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        long target;
        lock (_gate) target = checked(_ticks + duration.Ticks);
        while (true)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                var next = _timers.Where(t => !t.Disposed).Select(t => t.Next).DefaultIfEmpty(long.MaxValue).Min();
                if (next > target || next == long.MaxValue) { _ticks = target; return; }
                _ticks = next;
                due = _timers.Where(t => !t.Disposed && t.Next == next).ToArray();
                foreach (var timer in due)
                    timer.Next = timer.Period > 0 ? checked(next + timer.Period) : long.MaxValue;
            }
            foreach (var timer in due) if (!timer.Disposed) timer.Callback(timer.State);
        }
    }

    private sealed class ManualTimer(ManualStreamingTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        internal TimerCallback Callback { get; } = callback;
        internal object? State { get; } = state;
        internal long Next { get; set; } = long.MaxValue;
        internal long Period { get; private set; }
        internal bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (Disposed) return false;
                Next = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(owner._ticks + dueTime.Ticks);
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }
        public void Dispose() { lock (owner._gate) { Disposed = true; owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
