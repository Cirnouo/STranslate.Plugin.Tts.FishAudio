namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal sealed class StreamingActivity
{
    private readonly object _gate = new();
    private readonly long _started;
    private readonly TaskCompletionSource _firstPcm = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<StreamingStage, TimeSpan> _observations = [];
    private long _backpressureStarted;
    private TimeSpan _excluded;
    private int _backpressureLeases;

    internal StreamingActivity(TimeProvider timeProvider)
    {
        Clock = timeProvider;
        _started = Clock.GetTimestamp();
        _observations.Add(StreamingStage.Request, TimeSpan.Zero);
    }

    internal TimeProvider Clock { get; }
    internal Task FirstPlayablePcm => _firstPcm.Task;
    internal bool IsBackpressured { get { lock (_gate) return _backpressureLeases != 0; } }

    internal TimeSpan ActiveElapsed
    {
        get
        {
            lock (_gate)
            {
                var now = Clock.GetTimestamp();
                return Clock.GetElapsedTime(_started, now) - _excluded
                    - (_backpressureLeases == 0 ? TimeSpan.Zero : Clock.GetElapsedTime(_backpressureStarted, now));
            }
        }
    }

    internal IReadOnlyList<StreamingObservation> Observations
    {
        get
        {
            lock (_gate)
                return _observations.Select(pair => new StreamingObservation(pair.Key, pair.Value)).ToArray();
        }
    }

    internal void ReportFirstPlayablePcm()
    {
        ReportStage(StreamingStage.FirstPcm);
        _firstPcm.TrySetResult();
    }

    internal void ReportStage(StreamingStage stage)
    {
        lock (_gate)
            _observations.TryAdd(stage, Clock.GetElapsedTime(_started));
    }

    internal IDisposable EnterBackpressure(StreamingBackpressure stage)
    {
        lock (_gate)
        {
            if (_backpressureLeases++ == 0) _backpressureStarted = Clock.GetTimestamp();
        }
        return new BackpressureLease(this);
    }

    private void LeaveBackpressure()
    {
        lock (_gate)
        {
            if (--_backpressureLeases == 0)
                _excluded += Clock.GetElapsedTime(_backpressureStarted);
        }
    }

    private sealed class BackpressureLease(StreamingActivity owner) : IDisposable
    {
        private StreamingActivity? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.LeaveBackpressure();
    }
}
