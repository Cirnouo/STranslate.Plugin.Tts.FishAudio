using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class FrameworkFinalizationTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/invalid-or-spent-anchor")]
    public async Task SpentAndFutureAnchorsCannotStartWorkOrCreateAnotherAllowance()
    {
        var spentClock = new ManualClock();
        var spentAnchor = spentClock.GetTimestamp();
        spentClock.Advance(TimeSpan.FromSeconds(10));
        var calls = 0;
        var spent = await FrameworkFinalization.RunAsync(spentAnchor, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, spentClock);
        Assert.That(spent.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.DeadlineExpired));
        Assert.That(calls, Is.Zero);

        var futureClock = new ManualClock();
        var future = await FrameworkFinalization.RunAsync(futureClock.GetTimestamp() + 1, _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, futureClock);
        Assert.That(future.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.Failed));
        Assert.That(calls, Is.Zero);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/complete-all-owned-phases")]
    public async Task CompletionRequiresWriteFlushAndAsyncDisposalWithinTheOriginalRemainder()
    {
        var clock = new ManualClock();
        var anchor = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(8));
        var file = new FinalizationFile();
        Task<FrameworkFinalizationResult>? pending = null;
        try
        {
            pending = FrameworkFinalization.RunAsync(anchor, _ => file.RunAsync(), clock);
            await ReachPhaseAsync(file.WriteEntered.Task, pending);
            await ReachPhaseAsync(clock.TimerCreated.Task, pending);
            clock.Advance(TimeSpan.FromSeconds(1));
            file.WriteRelease.TrySetResult();
            await ReachPhaseAsync(file.DisposeEntered.Task, pending);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.That(pending.IsCompleted, Is.False, "A completed write is not completed finalization.");
            file.DisposeRelease.TrySetResult();
            var result = await pending;
            Assert.That(result.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.Completed));
            Assert.That(file.Events, Is.EqualTo(new[] { "create", "write", "flush", "dispose" }));
            Assert.That(file.Calls, Is.EqualTo(1));
        }
        finally { await file.ReleaseAndJoinAsync(); }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/operation-exception-fails")]
    public async Task AFinalizationExceptionCannotBeReportedAsCompletion()
    {
        var clock = new ManualClock();
        var calls = 0;
        var result = await FrameworkFinalization.RunAsync(clock.GetTimestamp(), _ =>
        {
            Interlocked.Increment(ref calls);
            throw new IOException("synthetic-finalization-write-failure");
        }, clock);
        Assert.That(result.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.Failed));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/remaining-budget-and-late-completion")]
    public async Task OnlyTheRemainingThreeSecondsAreAvailableAndLateSuccessCannotRehabilitateExpiry()
    {
        var clock = new ManualClock();
        var anchor = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromSeconds(7));
        var file = new FinalizationFile();
        Task<FrameworkFinalizationResult>? pending = null;
        FrameworkFinalizationResult? expired = null;
        try
        {
            pending = FrameworkFinalization.RunAsync(anchor, _ => file.RunAsync(), clock);
            await ReachPhaseAsync(file.WriteEntered.Task, pending);
            await ReachPhaseAsync(clock.TimerCreated.Task, pending);
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Assert.That(pending.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            expired = await pending;
            Assert.That(expired.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.DeadlineExpired));
            Assert.That(file.Finished.Task.IsCompleted, Is.False, "Expiry cannot await the noncooperative worker.");
        }
        finally { await file.ReleaseAndJoinAsync(); }

        Assert.That((await pending!).Outcome, Is.EqualTo(FrameworkFinalizationOutcome.DeadlineExpired));
        Assert.That(await pending!, Is.SameAs(expired));
        Assert.That(file.Events, Is.EqualTo(new[] { "create", "write", "flush", "dispose" }));
        Assert.That(file.Calls, Is.EqualTo(1));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/synchronous-pre-task-block")]
    public async Task SynchronousWorkBeforeTheOperationReturnsATaskCannotBlockTheOwner()
    {
        var clock = new ManualClock();
        var anchor = clock.GetTimestamp();
        var callerThread = Environment.CurrentManagedThreadId;
        var returned = 0;
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        try
        {
            var pending = FrameworkFinalization.RunAsync(anchor, _ =>
            {
                Interlocked.Increment(ref calls);
                try
                {
                    // Expose an inline invocation as a controlled test failure,
                    // rather than deadlocking this test before it can release.
                    if (Environment.CurrentManagedThreadId == callerThread && Volatile.Read(ref returned) == 0)
                        throw new InvalidOperationException("finalization-operation-invoked-inline");
                    entered.TrySetResult();
                    release.Wait(); // Models a blocked FileStream constructor before any Task exists.
                    return Task.CompletedTask;
                }
                finally { finished.TrySetResult(); }
            }, clock);
            Volatile.Write(ref returned, 1);
            await ReachPhaseAsync(entered.Task, pending);
            await ReachPhaseAsync(clock.TimerCreated.Task, pending);
            clock.Advance(TimeSpan.FromSeconds(10));
            var result = await pending;
            Assert.That(result.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.DeadlineExpired));
            Assert.That(finished.Task.IsCompleted, Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }
        finally
        {
            release.Set();
            if (Volatile.Read(ref calls) != 0) await finished.Task;
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/asynchronous-write-block")]
    public Task AnUncancelableAsyncWriteCannotKeepTheOwnerWaiting() =>
        AssertBlockedPhaseExpiresAsync(blockDisposal: false);

    [Test]
    [Property("FishAudioCaseId", "infra.framework-finalization/asynchronous-dispose-block")]
    public Task AnUncancelableAsyncDisposalCannotKeepTheOwnerWaiting() =>
        AssertBlockedPhaseExpiresAsync(blockDisposal: true);

    private static async Task AssertBlockedPhaseExpiresAsync(bool blockDisposal)
    {
        var clock = new ManualClock();
        var file = new FinalizationFile();
        if (blockDisposal) file.WriteRelease.TrySetResult();
        try
        {
            var pending = FrameworkFinalization.RunAsync(clock.GetTimestamp(), _ => file.RunAsync(), clock);
            await ReachPhaseAsync(blockDisposal ? file.DisposeEntered.Task : file.WriteEntered.Task, pending);
            await ReachPhaseAsync(clock.TimerCreated.Task, pending);
            clock.Advance(TimeSpan.FromSeconds(10));
            var result = await pending;
            Assert.That(result.Outcome, Is.EqualTo(FrameworkFinalizationOutcome.DeadlineExpired));
            Assert.That(file.Finished.Task.IsCompleted, Is.False);
            Assert.That(file.Calls, Is.EqualTo(1));
        }
        finally { await file.ReleaseAndJoinAsync(); }
    }

    private static async Task ReachPhaseAsync(Task phase, Task<FrameworkFinalizationResult> owner)
    {
        var first = await Task.WhenAny(phase, owner);
        if (ReferenceEquals(first, owner) && !phase.IsCompleted)
        {
            var result = await owner;
            Assert.Fail("Finalization ended before the controlled phase: " + result.Outcome);
        }
        await phase;
    }

    // An actual await-using boundary with independently held write and disposal
    // tasks. Both intentionally ignore cancellation, like stalled external I/O.
    private sealed class FinalizationFile : IAsyncDisposable
    {
        internal readonly TaskCompletionSource WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Events = [];
        internal int Calls;

        internal async Task RunAsync()
        {
            Interlocked.Increment(ref Calls);
            try
            {
                await using var owned = this;
                Events.Add("create");
                WriteEntered.TrySetResult();
                await WriteRelease.Task;
                Events.Add("write");
                Events.Add("flush");
            }
            finally { Finished.TrySetResult(); }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult();
            await DisposeRelease.Task;
            Events.Add("dispose");
        }

        internal async Task ReleaseAndJoinAsync()
        {
            WriteRelease.TrySetResult();
            DisposeRelease.TrySetResult();
            if (Volatile.Read(ref Calls) != 0) await Finished.Task;
        }
    }

    // A test-local monotonic, one-shot timer provider; advancing fake time never
    // sleeps or supplies a new execution/cleanup allowance to the test itself.
    private sealed class ManualClock : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp = TimeSpan.TicksPerSecond * 50;
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_sync) return _timestamp; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_sync) _timers.Add(timer);
            timer.Change(dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            ManualTimer[] timers;
            lock (_sync)
            {
                _timestamp = checked(_timestamp + elapsed.Ticks);
                timers = _timers.ToArray();
            }
            foreach (var timer in timers) timer.FireIfDue();
        }

        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _due;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan || dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                lock (owner._sync)
                {
                    if (_disposed) return false;
                    _due = dueTime == Timeout.InfiniteTimeSpan ? null : checked(owner._timestamp + dueTime.Ticks);
                    return true;
                }
            }

            internal void FireIfDue()
            {
                lock (owner._sync)
                {
                    if (_disposed || _due is not { } due || due > owner._timestamp) return;
                    _due = null;
                }
                callback(state);
            }

            public void Dispose() { lock (owner._sync) { _disposed = true; _due = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
