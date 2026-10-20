namespace FishAudioProbe;

internal sealed class WsSynthesisLifetime : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _linked;

    internal WsSynthesisLifetime(TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        _timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();
        var remaining = ProbePolicy.CutoffUtc - now;
        var delay = remaining <= TimeSpan.Zero ? TimeSpan.Zero
            : remaining < ProbePolicy.CaseTimeout ? remaining : ProbePolicy.CaseTimeout;
        DeadlineUtc = now + delay;
        _deadline = new CancellationTokenSource(delay, timeProvider);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
    }

    internal DateTimeOffset DeadlineUtc { get; }
    internal CancellationToken Token => _linked.Token;
    internal bool IsCancellationRequested => _linked.IsCancellationRequested;

    internal void EnsureAllowed()
    {
        ProbePolicy.EnsureSynthesisAllowed(ProbePolicy.FreeModel, _timeProvider.GetUtcNow());
        Token.ThrowIfCancellationRequested();
    }

    internal ValueTask SendAsync(Func<CancellationToken, ValueTask> send)
    {
        EnsureAllowed();
        return send(Token);
    }

    internal void Cancel() => _linked.Cancel();
    public void Dispose() { _linked.Dispose(); _deadline.Dispose(); }
}
