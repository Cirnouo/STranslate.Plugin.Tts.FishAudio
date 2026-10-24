internal sealed class InterceptingTimeProvider : TimeProvider, IDisposable
{
    private readonly ManualStreamingTimeProvider _clock = new();
    private readonly List<TrackedTimer> _timers = [];
    private Action? _beforeNextTimerReturns;

    internal Action? BeforeNextTimerReturns { set => _beforeNextTimerReturns = value; }
    internal TrackedTimer[] Timers { get { lock (_timers) return _timers.ToArray(); } }
    public override long TimestampFrequency => _clock.TimestampFrequency;
    public override long GetTimestamp() => _clock.GetTimestamp();
    public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TrackedTimer(_clock.CreateTimer(callback, state, dueTime, period));
        lock (_timers) _timers.Add(timer);
        Interlocked.Exchange(ref _beforeNextTimerReturns, null)?.Invoke();
        return timer;
    }

    internal void Advance(TimeSpan duration) => _clock.Advance(duration);

    public void Dispose()
    {
        foreach (var timer in Timers)
            timer.Dispose();
    }

    internal sealed class TrackedTimer(ITimer inner) : ITimer
    {
        private int _disposed;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);
        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            inner.Dispose();
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
