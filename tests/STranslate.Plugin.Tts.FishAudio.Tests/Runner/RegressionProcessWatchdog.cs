using System.Diagnostics;

// A pure state machine: child timestamps, never event receipt or phase chatter,
// own the deadline. Tests advance a fake monotonic clock without real delays.
internal sealed class RegressionProcessWatchdog
{
    private readonly RegressionChildRequest _request;
    private readonly long _frequency;
    private readonly long _warningTicks;
    private readonly long _hardTicks;
    private readonly long _startupTicks;
    private long _anchor;
    private long _lastTimestamp;
    private long _queued;
    private int _ordinal;
    private int _caseIndex = -1;
    private bool _entered;
    private bool _warned;
    private bool _dispatcherStarted;
    private bool _dispatcherReady;
    internal string Stage { get; private set; } = "child-startup";
    internal string? FailureStage { get; private set; }
    internal string? FailureCaseId { get; private set; }
    internal string? CurrentCaseId => _caseIndex >= 0 ? _request.CaseIds[_caseIndex] : null;
    internal int CurrentOrdinal => _ordinal;
    internal int WarningCount { get; private set; }
    internal bool Ready { get; private set; }
    internal bool Final { get; private set; }
    internal int CompletedCaseCount { get; private set; }
    internal bool CaseFinishedWithIncompleteRoot { get; private set; }
    internal long? EntryElapsedMilliseconds { get; private set; }

    internal RegressionProcessWatchdog(RegressionChildRequest request, long startedTimestamp,
        long frequency = 0, TimeSpan? warning = null, TimeSpan? hard = null, TimeSpan? startup = null)
    {
        _request = request;
        _frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        _anchor = _lastTimestamp = startedTimestamp;
        _warningTicks = checked((long)((warning ?? TimeSpan.FromSeconds(20)).TotalSeconds * _frequency));
        _hardTicks = checked((long)((hard ?? TimeSpan.FromSeconds(120)).TotalSeconds * _frequency));
        _startupTicks = checked((long)((startup ?? TimeSpan.FromSeconds(120)).TotalSeconds * _frequency));
    }

    internal void Tick(long timestamp)
    {
        if (FailureStage is not null || Final) return;
        if (timestamp - _anchor >= _warningTicks && !_warned && Stage is "action" or "gap")
        {
            _warned = true;
            WarningCount++;
        }
        if (timestamp - _anchor >= (Stage is "child-startup" or "dispatcher-startup" ? _startupTicks : _hardTicks))
        {
            FailureStage = Stage;
            FailureCaseId = Stage == "idle" && _caseIndex + 1 < _request.CaseIds.Length ? _request.CaseIds[_caseIndex + 1] : CurrentCaseId;
        }
    }

    internal void Observe(RegressionProcessEvent record)
    {
        if (record.Version != 1 || record.RequestId != _request.RequestId || record.RunId != _request.RunId ||
            record.Partition != _request.Partition || record.Frequency != _frequency || record.Timestamp < _lastTimestamp ||
            record.QueuedTimestamp < 0 || record.QueuedTimestamp > record.Timestamp || Final)
            throw new InvalidDataException("invalid-child-event");
        Tick(record.Timestamp);
        _lastTimestamp = record.Timestamp;
        // Once hard expiry is observed, later completion cannot rehabilitate it.
        if (FailureStage is not null) return;
        if (record.Kind == "ready")
        {
            Require(!Ready && record.CaseId.Length == 0 && record.Ordinal == 0);
            Ready = true;
            Reset("idle", record.Timestamp);
        }
        else if (record.Kind == "case-start")
        {
            Require(Ready && Stage == "idle" && _caseIndex + 1 < _request.CaseIds.Length &&
                record.CaseId == _request.CaseIds[_caseIndex + 1] && record.Ordinal == 0);
            _caseIndex++;
            _ordinal = 0;
            CaseFinishedWithIncompleteRoot = false;
            Reset("gap", record.Timestamp);
        }
        else if (record.Kind == "dispatcher-starting")
        {
            Require(record.CaseId == CurrentCaseId && !_dispatcherStarted && Stage == "gap" && record.Ordinal == 0);
            _dispatcherStarted = true;
            Reset("dispatcher-startup", record.Timestamp);
        }
        else if (record.Kind == "dispatcher-ready")
        {
            Require(record.CaseId == CurrentCaseId && _dispatcherStarted && !_dispatcherReady && Stage == "dispatcher-startup" && record.Ordinal == 0);
            _dispatcherReady = true;
            Reset("gap", record.Timestamp);
        }
        else if (record.Kind == "queued")
        {
            Require(record.CaseId == CurrentCaseId && Stage == "gap" && record.Ordinal == _ordinal + 1 &&
                record.QueuedTimestamp >= _anchor && record.QueuedTimestamp <= record.Timestamp);
            _ordinal = record.Ordinal;
            _queued = record.QueuedTimestamp;
            _entered = false;
            EntryElapsedMilliseconds = null;
            Reset("action", _queued);
            Tick(record.Timestamp);
        }
        else if (record.Kind is "entered" or "completed")
        {
            Require(record.CaseId == CurrentCaseId && Stage == "action" && record.Ordinal == _ordinal && record.QueuedTimestamp == _queued);
            if (record.Kind == "entered")
            {
                Require(!_entered);
                _entered = true;
                EntryElapsedMilliseconds = (record.Timestamp - _queued) * 1000 / _frequency;
            }
            else
            {
                Require(_entered);
                Reset("gap", record.Timestamp);
            }
        }
        else if (record.Kind == "case-finished")
        {
            Require(record.CaseId == CurrentCaseId && Stage is "gap" or "action" && record.Ordinal == 0);
            CaseFinishedWithIncompleteRoot = Stage == "action";
            CompletedCaseCount++;
            Reset("idle", record.Timestamp);
        }
        else if (record.Kind == "final")
        {
            Require(Ready && Stage == "idle" && record.CaseId.Length == 0 && record.Ordinal == 0);
            Final = true;
        }
        else throw new InvalidDataException("unknown-child-event");
    }

    private void Reset(string stage, long anchor)
    {
        Stage = stage;
        _anchor = anchor;
        _warned = false;
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("invalid-child-event-order");
    }
}
