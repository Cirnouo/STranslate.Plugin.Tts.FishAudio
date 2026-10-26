using System.Diagnostics;

// Only the child process publishes these fixed, input-free protocol records. The
// root is installed in the actual dispatched callback, so async continuations and
// nested off-STA dispatch retain the original queued deadline.
internal sealed record RegressionActionEvent(string Kind, string CaseId, int Ordinal,
    long Timestamp, long Frequency, long QueuedTimestamp);

internal static class RegressionActionScope
{
    private static readonly AsyncLocal<CaseScope?> CurrentCase = new();
    private static readonly AsyncLocal<RegressionRootAction?> CurrentRoot = new();

    internal static IDisposable BeginCase(string caseId, Action<RegressionActionEvent> publish)
    {
        var previous = CurrentCase.Value;
        CurrentCase.Value = new CaseScope(caseId, publish);
        return new RestoreScope(() => CurrentCase.Value = previous);
    }

    internal static RegressionRootAction? QueueRoot()
    {
        if (CurrentRoot.Value is { IsCompleted: false }) return null;
        var owner = CurrentCase.Value;
        if (owner is null) return null;
        var root = new RegressionRootAction(owner.CaseId, Interlocked.Increment(ref owner.Ordinal), owner.Publish);
        root.Publish("queued");
        return root;
    }

    internal static void DispatcherStarting() => PublishDispatcher("dispatcher-starting");
    internal static void DispatcherReady() => PublishDispatcher("dispatcher-ready");

    private static void PublishDispatcher(string kind)
    {
        var owner = CurrentCase.Value;
        if (owner is null) return;
        try { owner.Publish(new(kind, owner.CaseId, 0, Stopwatch.GetTimestamp(), Stopwatch.Frequency, 0)); }
        catch { /* Missing protocol evidence fails in the parent; it cannot replace a case failure. */ }
    }

    internal sealed class RegressionRootAction
    {
        private readonly string _caseId;
        private readonly Action<RegressionActionEvent> _publish;
        private int _completed;
        internal int Ordinal { get; }
        internal long QueuedTimestamp { get; } = Stopwatch.GetTimestamp();
        internal bool IsCompleted => Volatile.Read(ref _completed) != 0;

        internal RegressionRootAction(string caseId, int ordinal, Action<RegressionActionEvent> publish)
        {
            _caseId = caseId;
            Ordinal = ordinal;
            _publish = publish;
        }

        internal IDisposable Enter()
        {
            var previous = CurrentRoot.Value;
            CurrentRoot.Value = this;
            Publish("entered");
            return new RestoreScope(() => CurrentRoot.Value = previous);
        }

        internal void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0) Publish("completed");
        }

        internal void Publish(string kind)
        {
            try { _publish(new(kind, _caseId, Ordinal, Stopwatch.GetTimestamp(), Stopwatch.Frequency, QueuedTimestamp)); }
            catch { /* The independently supervised child cannot turn a protocol fault into a pass. */ }
        }
    }

    private sealed class CaseScope(string caseId, Action<RegressionActionEvent> publish)
    {
        internal string CaseId { get; } = caseId;
        internal Action<RegressionActionEvent> Publish { get; } = publish;
        internal int Ordinal;
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
