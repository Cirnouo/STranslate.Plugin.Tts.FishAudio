using System.Diagnostics.CodeAnalysis;

namespace FishAudio.Testing.Execution;

public sealed record FrameworkExecutionEvent(string Kind, long Timestamp, long Frequency,
    string? CaseId = null, int Ordinal = 0, long QueuedTimestamp = 0, string? Outcome = null);

// These are supervised-envelope states. Raw NUnit terminal results are retained
// separately; a framework pass cannot establish an unfinished owned envelope.
public sealed record FrameworkWatchdogState(string Stage, string? FailureStage, string? FailureCaseId,
    int WarningCount, IReadOnlyList<string> PassedIds, IReadOnlyList<string> FailedIds,
    IReadOnlyList<string> TimedOutIds, IReadOnlyList<string> ActiveIds,
    IReadOnlyList<string> InterruptedIds, IReadOnlyList<string> NotRunIds, bool Complete,
    bool TerminationRequired);

/// <summary>
/// The observer of the standard framework host's execution envelope. It does not
/// discover or execute tests, fabricate framework outcomes, or extend deadlines.
/// </summary>
public sealed class FrameworkWatchdog
{
    private enum Outcome { NotRun, Active, Passed, Failed, TimedOut, Interrupted }
    private sealed record Guard(CaseWatchdogIdentity Identity, CaseWatchdog Watchdog);

    private readonly string[] _selected;
    private readonly Dictionary<string, Outcome> _outcomes;
    private readonly Dictionary<string, Guard> _guards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly long _started;
    private readonly long _frequency;
    private readonly long _startupTicks;
    private readonly long _cleanupTicks;
    private readonly int _maximumConcurrency;
    private readonly bool _requireOrder;
    private long _hostAnchor;
    private long _readyTimestamp;
    private long _latestAcceptedTimestamp;
    private long _lastCaseFinished;
    private long? _finalAcceptedTimestamp;
    private int _startedCaseCount;
    private bool _ready;
    private bool _cleanupComplete;
    private bool _finalPublished;
    private bool _exited;
    private bool _complete;
    private bool _terminationRequired;
    private string? _failureStage;
    private string? _failureCaseId;

    public FrameworkWatchdog(IReadOnlyList<string> selectedCaseIds, long startedTimestamp,
        long frequency, int maximumConcurrency = 1, bool requireOrder = false)
    {
        if (selectedCaseIds is null || selectedCaseIds.Count is < 1 or > 100_000 ||
            selectedCaseIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 4096 || id.Any(char.IsControl)) ||
            selectedCaseIds.Distinct(StringComparer.Ordinal).Count() != selectedCaseIds.Count ||
            startedTimestamp < 0 || frequency is < 1 or > 1_000_000_000_000 ||
            maximumConcurrency is < 1 or > 4 || requireOrder && maximumConcurrency != 1)
            throw new ArgumentException("invalid-framework-supervision-plan");
        _selected = selectedCaseIds.ToArray();
        _outcomes = _selected.ToDictionary(id => id, _ => Outcome.NotRun, StringComparer.Ordinal);
        _started = _hostAnchor = _latestAcceptedTimestamp = startedTimestamp;
        _frequency = frequency;
        _startupTicks = checked(120 * frequency);
        _cleanupTicks = checked(10 * frequency);
        _maximumConcurrency = maximumConcurrency;
        _requireOrder = requireOrder;
    }

    public FrameworkWatchdogState State => new(Stage, _failureStage, _failureCaseId,
        _guards.Values.Sum(guard => guard.Watchdog.WarningCount), Ids(Outcome.Passed), Ids(Outcome.Failed),
        Ids(Outcome.TimedOut), Ids(Outcome.Active), Ids(Outcome.Interrupted), Ids(Outcome.NotRun),
        _complete, _terminationRequired);

    private string Stage => !_ready ? "child-startup" : _active.Count switch
    {
        1 => _guards[_active.Single()].Watchdog.Stage,
        > 1 => "parallel",
        _ => _finalAcceptedTimestamp is not null ? "child-exit" : _finalPublished ? "final-verification" :
            AllCasesFinished || _failureStage is not null ? "framework-cleanup" : "idle",
    };

    private bool AllCasesFinished => _outcomes.Values.All(outcome => outcome is Outcome.Passed or Outcome.Failed);

    private IReadOnlyList<string> Ids(Outcome outcome)
        => Array.AsReadOnly(_selected.Where(id => _outcomes[id] == outcome).ToArray());

    public void Observe(FrameworkExecutionEvent record)
    {
        Require(record is not null && !_exited && record.Frequency == _frequency &&
            record.Timestamp >= _started && record.QueuedTimestamp >= 0 &&
            record.QueuedTimestamp <= record.Timestamp, "invalid-framework-event");
        EvaluateAt(record.Timestamp);
        if (record.Kind is "ready" or "cleanup-complete" or "final-published")
        {
            Require(record.CaseId is null && record.Ordinal == 0 && record.QueuedTimestamp == 0 &&
                record.Outcome is null, "invalid-framework-host-event");
            if (_failureStage is not null && !_ready) return;
            if (record.Kind == "ready")
            {
                Require(!_ready, "duplicate-framework-ready");
                _ready = true;
                _hostAnchor = _readyTimestamp = record.Timestamp;
            }
            else if (record.Kind == "cleanup-complete")
            {
                Require(_ready && !_cleanupComplete && _active.Count == 0 &&
                    (AllCasesFinished || _failureStage is not null) && record.Timestamp >= _latestAcceptedTimestamp,
                    "invalid-framework-cleanup-order");
                _cleanupComplete = true;
            }
            else
            {
                // This event belongs to the independently owned execution
                // driver after framework result publication, not to a fixture
                // cleanup callback. The parent still must validate the evidence.
                Require(_ready && !_finalPublished && _cleanupComplete && _active.Count == 0 &&
                    record.Timestamp >= _latestAcceptedTimestamp &&
                    (AllCasesFinished || _failureStage == "case-failed"), "invalid-framework-final-publication");
                if (!_terminationRequired) _finalPublished = true;
            }
            _latestAcceptedTimestamp = Math.Max(_latestAcceptedTimestamp, record.Timestamp);
            return;
        }

        Require(_ready && record.CaseId is not null && _outcomes.ContainsKey(record.CaseId),
            "unknown-framework-case");
        var id = record.CaseId!;
        if (record.Kind == "case-start")
        {
            Require(_failureStage is null && _outcomes[id] == Outcome.NotRun &&
                _active.Count < _maximumConcurrency && record.Timestamp >= _readyTimestamp &&
                (_maximumConcurrency != 1 || record.Timestamp >= _hostAnchor) &&
                record.Ordinal == 0 && record.QueuedTimestamp == 0 &&
                record.Outcome is null && (!_requireOrder || _selected[_startedCaseCount] == id),
                "invalid-framework-case-start");
            var identity = new CaseWatchdogIdentity("case-" + _startedCaseCount, "framework", "framework", [id]);
            var watchdog = new CaseWatchdog(identity, record.Timestamp, _frequency);
            var guard = new Guard(identity, watchdog);
            watchdog.Observe(ToCaseEvent(guard, new("ready", record.Timestamp, _frequency)));
            watchdog.Observe(ToCaseEvent(guard, record));
            _guards.Add(id, guard);
            _active.Add(id);
            _outcomes[id] = Outcome.Active;
            _startedCaseCount++;
            _latestAcceptedTimestamp = Math.Max(_latestAcceptedTimestamp, record.Timestamp);
            return;
        }

        Require(record.Kind is "case-finished" or "dispatcher-starting" or "dispatcher-ready" or
            "queued" or "entered" or "completed", "unknown-framework-event-kind");
        // A result arriving after hard expiry is retained by the transport, but
        // cannot turn the expired execution envelope into a passing execution.
        if (_outcomes[id] == Outcome.TimedOut) return;
        Require(_active.Contains(id), "inactive-framework-case");
        var current = _guards[id];
        if (record.Kind == "case-finished")
        {
            Require(record.Ordinal == 0 && record.QueuedTimestamp == 0 &&
                record.Outcome is "passed" or "failed", "invalid-framework-case-result");
            current.Watchdog.Observe(ToCaseEvent(current, record));
            if (current.Watchdog.FailureStage is not null) { MarkTimeout(id, record.Timestamp); return; }
            current.Watchdog.Observe(ToCaseEvent(current, new("final", record.Timestamp, _frequency)));
            _active.Remove(id);
            _outcomes[id] = record.Outcome == "passed" ? Outcome.Passed : Outcome.Failed;
            _lastCaseFinished = Math.Max(_lastCaseFinished, record.Timestamp);
            _hostAnchor = Math.Max(_hostAnchor, record.Timestamp);
            if (record.Outcome == "failed") Fail("case-failed", id, record.Timestamp, immediate: false);
            else if (current.Watchdog.CaseFinishedWithIncompleteRoot)
            {
                _outcomes[id] = Outcome.Interrupted;
                Fail("incomplete-root-action", id, record.Timestamp, immediate: true);
            }
        }
        else
        {
            Require(record.Outcome is null, "invalid-framework-action-outcome");
            current.Watchdog.Observe(ToCaseEvent(current, record));
            if (current.Watchdog.FailureStage is not null) MarkTimeout(id, record.Timestamp);
        }
        _latestAcceptedTimestamp = Math.Max(_latestAcceptedTimestamp, record.Timestamp);
    }

    public void Tick(long timestamp)
    {
        Require(timestamp >= _started, "invalid-framework-clock");
        if (!_exited) EvaluateAt(timestamp);
    }

    // The caller supplies its own acceptance time after verifying the required
    // lifecycle and result-publication barrier. A child cleanup callback alone
    // cannot establish that barrier. Hard failure cleanup remains owned by the
    // external process supervisor and never receives a new allowance here.
    public void AcceptFinal(long parentAcceptedTimestamp)
    {
        Require(!_exited && _finalAcceptedTimestamp is null && parentAcceptedTimestamp >= _latestAcceptedTimestamp,
            "invalid-framework-final-acceptance");
        EvaluateAt(parentAcceptedTimestamp);
        if (_terminationRequired) return;
        Require(_ready && _cleanupComplete && _finalPublished && _active.Count == 0 &&
            (AllCasesFinished || _failureStage == "case-failed"), "incomplete-framework-final-evidence");
        _finalAcceptedTimestamp = _latestAcceptedTimestamp = parentAcceptedTimestamp;
    }

    public void HostExited(long timestamp, bool successfulExit, bool stdoutDrained, bool stderrDrained)
    {
        Require(!_exited && timestamp >= _latestAcceptedTimestamp, "invalid-framework-host-exit");
        EvaluateAt(timestamp);
        if (_failureStage is null)
        {
            if (!_ready || !AllCasesFinished) Fail("missing-case-results", null, timestamp, immediate: true);
            else if (!_cleanupComplete) Fail("cleanup-incomplete", null, timestamp, immediate: true);
            else if (_finalAcceptedTimestamp is null) Fail("final-evidence-missing", null, timestamp, immediate: true);
            else if (!successfulExit) Fail("host-exit", null, timestamp, immediate: true);
            else if (!stdoutDrained || !stderrDrained) Fail("host-drain", null, timestamp, immediate: true);
        }
        foreach (var id in _active) _outcomes[id] = Outcome.Interrupted;
        _active.Clear();
        _exited = true;
        _complete = _failureStage is null && _ready && _cleanupComplete && _finalAcceptedTimestamp is not null &&
            _outcomes.Values.All(outcome => outcome == Outcome.Passed);
    }

    private void EvaluateAt(long timestamp)
    {
        foreach (var id in _active.ToArray())
        {
            _guards[id].Watchdog.Tick(timestamp);
            if (_guards[id].Watchdog.FailureStage is not null) MarkTimeout(id, timestamp);
        }
        if (_terminationRequired) return;
        if (_finalAcceptedTimestamp is { } finalAccepted)
        {
            // Exit and both drains share the parent's original allowance;
            // neither stream completion nor process exit renews it.
            if (timestamp - finalAccepted >= _cleanupTicks)
                Fail("child-exit", null, timestamp, immediate: true);
            return;
        }
        // The old supervisor observes final before validating its manifest.
        // A timely publication closes the child-timestamped interval; the
        // parent's separate acceptance later starts its exit/drain allowance.
        if (_finalPublished) return;
        if (!_ready)
        {
            if (timestamp - _started >= _startupTicks) Fail("child-startup", null, timestamp, immediate: true);
        }
        else if (_active.Count == 0)
        {
            if (AllCasesFinished || _failureStage == "case-failed")
            {
                // Preserve the old idle/pre-final lifecycle guard. Ordinary
                // assertion failure does not turn it into hard-failure cleanup.
                if (timestamp - _lastCaseFinished >= _startupTicks)
                    Fail("framework-cleanup", null, timestamp, immediate: true);
            }
            else if (timestamp - _hostAnchor >= _startupTicks)
                Fail("idle", _selected.First(id => _outcomes[id] == Outcome.NotRun), timestamp, immediate: true);
        }
    }

    private void MarkTimeout(string id, long timestamp)
    {
        _outcomes[id] = Outcome.TimedOut;
        _active.Remove(id);
        Fail(_guards[id].Watchdog.FailureStage!, id, timestamp, immediate: true);
    }

    private void Fail(string stage, string? id, long timestamp, bool immediate)
    {
        if (_failureStage is null)
        {
            _failureStage = stage;
            _failureCaseId = id;
        }
        _terminationRequired |= immediate;
    }

    private static CaseWatchdogEvent ToCaseEvent(Guard guard, FrameworkExecutionEvent record) => new(
        1, guard.Identity.RequestId, guard.Identity.RunId, guard.Identity.Partition, record.Kind,
        record.CaseId ?? "", record.Ordinal, record.Timestamp, record.Frequency, record.QueuedTimestamp);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
