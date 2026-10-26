using System.Reflection;
using System.Windows.Threading;
using static TestAssertions;

internal static class RegressionExecutionSuite
{
    internal static async Task RunAsync()
    {
        // Inspect Lazy state without asking it to construct a native dispatcher.
        var shared = (Lazy<Task<Dispatcher>>)typeof(StaTestHost).GetField("SharedDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var wasCreated = shared.IsValueCreated;
        var calls = 0;
        foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1) })
        {
            Exception? caught = null;
            try { await StaTestHost.RunOnStaThreadAsync((Func<Task>)(() => { calls++; return Task.CompletedTask; }), invalid); }
            catch (Exception error) { caught = error; }
            AssertEqual(true, caught is ArgumentOutOfRangeException { ParamName: "executionTimeout" }, "The async host rejects a nonpositive guard before dispatch");
            AssertEqual(wasCreated, shared.IsValueCreated, "Invalid async arguments cannot initialize the shared native dispatcher");
            caught = null;
            try { await RegressionExecution.WaitAsync(Task.CompletedTask, invalid); }
            catch (Exception error) { caught = error; }
            AssertEqual(true, caught is ArgumentOutOfRangeException, "The actual nongeneric execution primitive validates nonpositive guards");
            caught = null;
            try { await RegressionExecution.WaitAsync(Task.FromResult(42), invalid); }
            catch (Exception error) { caught = error; }
            AssertEqual(true, caught is ArgumentOutOfRangeException, "The actual generic execution primitive validates nonpositive guards");
        }
        AssertEqual(0, calls, "Invalid guards never execute their action");
        await RegressionExecution.WaitAsync(Task.CompletedTask, TimeSpan.FromTicks(1));
        AssertEqual(42, await RegressionExecution.WaitAsync(Task.FromResult(42), TimeSpan.FromTicks(1)), "A positive explicit API guard accepts completed generic work");
        AssertEqual(TimeSpan.FromSeconds(20), RegressionExecution.Timeout, "The shared regression execution and startup guard is twenty seconds");

        foreach (var generic in new[] { false, true })
        {
            var clock = new ManualStreamingTimeProvider();
            var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task guarded = generic ? RegressionExecution.WaitAsync(work.Task, clock) : RegressionExecution.WaitAsync((Task)work.Task, clock);
            clock.Advance(TimeSpan.FromSeconds(5));
            AssertEqual(false, guarded.IsCompleted, "The actual execution primitive cannot expire at the historical five-second guard");
            clock.Advance(TimeSpan.FromMilliseconds(14999));
            AssertEqual(false, guarded.IsCompleted, "The actual execution primitive remains incomplete at 19999ms");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            AssertEqual(true, guarded.IsCompleted, "The actual execution primitive expires at exactly 20000ms");
            Exception? deadline = null;
            try { await guarded; }
            catch (Exception error) { deadline = error; }
            AssertEqual(true, deadline is TimeoutException, "The actual generic and nongeneric guards report their execution deadline");
            AssertEqual(false, work.Task.IsCompleted, "Execution expiry cannot cancel, replace or complete the original work");
            work.SetResult(42);
            AssertEqual(42, await work.Task, "The same original work may complete once after its guard expires");
            Exception? late = null;
            try { await guarded; }
            catch (Exception error) { late = error; }
            AssertEqual(true, ReferenceEquals(deadline, late), "Late original completion must retain the same failed guard and exception");
        }

        await RegressionExecution.WaitAsync(Task.CompletedTask);
        AssertEqual(42, await RegressionExecution.WaitAsync(Task.FromResult(42)), "Immediate generic completion keeps its value");
        var owned = new IOException("Synthetic owned failure");
        foreach (var generic in new[] { false, true })
        {
            Task guarded = generic ? RegressionExecution.WaitAsync(Task.FromException<int>(owned)) : RegressionExecution.WaitAsync(Task.FromException(owned));
            Exception? caught = null;
            try { await guarded; }
            catch (Exception error) { caught = error; }
            AssertEqual(true, ReferenceEquals(owned, caught), "Completed owned faults preserve exception identity in both actual wait paths");
        }
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        async Task WorkAsync() { await release.Task; completed = true; }
        var asyncWork = WorkAsync();
        var acrossAwait = RegressionExecution.WaitAsync(asyncWork);
        AssertEqual(false, acrossAwait.IsCompleted, "The actual execution guard cannot report success before awaited work completes");
        release.SetResult();
        await acrossAwait;
        AssertEqual(true, completed, "The guard follows the original action across await through completion");
        AssertEqual(wasCreated, shared.IsValueCreated, "All execution contracts remain pure and do not warm the shared STA");
    }
}
