using FishAudio.Testing.Execution;

// The legacy protocol retains its existing entry point. The framework envelope
// uses the same case/action deadline transitions, including child timestamps and
// the separate Dispatcher startup guard.
internal sealed class RegressionProcessWatchdog
{
    private readonly CaseWatchdog _inner;

    internal RegressionProcessWatchdog(RegressionChildRequest request, long startedTimestamp,
        long frequency = 0, TimeSpan? warning = null, TimeSpan? hard = null, TimeSpan? startup = null)
    {
        _inner = new(new(request.RequestId, request.RunId, request.Partition, request.CaseIds),
            startedTimestamp, frequency, warning, hard, startup);
    }

    internal string Stage => _inner.Stage;
    internal string? FailureStage => _inner.FailureStage;
    internal string? FailureCaseId => _inner.FailureCaseId;
    internal string? CurrentCaseId => _inner.CurrentCaseId;
    internal int CurrentOrdinal => _inner.CurrentOrdinal;
    internal int WarningCount => _inner.WarningCount;
    internal bool Ready => _inner.Ready;
    internal bool Final => _inner.Final;
    internal int CompletedCaseCount => _inner.CompletedCaseCount;
    internal bool CaseFinishedWithIncompleteRoot => _inner.CaseFinishedWithIncompleteRoot;
    internal long? EntryElapsedMilliseconds => _inner.EntryElapsedMilliseconds;

    internal void Tick(long timestamp) => _inner.Tick(timestamp);

    internal void Observe(RegressionProcessEvent record) => _inner.Observe(new(
        record.Version, record.RequestId, record.RunId, record.Partition, record.Kind,
        record.CaseId, record.Ordinal, record.Timestamp, record.Frequency, record.QueuedTimestamp));
}
