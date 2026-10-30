using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Lifecycle;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

namespace NUnitCompatibility;

// These cases observe the actual scenario before their independent rescue.
// They create no replacement completion source, delay, timer or retry.
[TestFixture]
[NonParallelizable]
public sealed class HeldWorkOwnershipFaultCases
{
    private const string ValidVoiceId = "11111111111111111111111111111111";

    [Test]
    [Property("FishAudioCaseId", "infra.voice-discovery/pending-lookup-body-failure-joins-original-work")]
    public async Task PendingLookupBodyFailureJoinsOriginalWorkBeforeRescue()
    {
        var marker = new InvalidOperationException("pending-lookup-owned-body-failure");
        PendingLookupOwnershipObservation? ownership = null;
        Task? scenario = null;
        var callbacks = 0;
        var pendingObserved = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            scenario = VoiceDiscoverySuite.RunEditsAndNewByIdLookupsClearErrorsImmediatelyAsync(observed =>
            {
                ownership = observed;
                callbacks++;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Operation, Is.SameAs(observed.Owner.SubmitVoiceIdCommand.ExecutionTask),
                        "pending-lookup-must-be-the-original-command-task");
                    Assert.That(observed.Operation.IsCompleted, Is.False,
                        "pending-lookup-fixture-must-observe-real-pending-work");
                    Assert.That(observed.Response.IsCompleted, Is.False);
                    Assert.That(observed.HttpCalls, Is.EqualTo(2),
                        "pending-lookup-fixture-must-enter-the-original-http-handler");
                    Assert.That(observed.Owner.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.True,
                        "pending-lookup-owner-must-be-live-before-fault");
                });
                pendingObserved = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            Assert.That(pendingObserved, Is.True,
                "pending-lookup-callback-must-have-observed-started-work");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            var completedScenario = scenario!;
            Assert.Multiple(() =>
            {
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(completedScenario.IsCompleted, Is.True);
                Assert.That(completedScenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker),
                    "pending-lookup-cleanup-must-preserve-primary-identity");
                Assert.That(completed.Operation.IsCompleted, Is.True,
                    "pending-lookup-task-must-complete-before-rescue");
                Assert.That(completed.Operation.IsCompletedSuccessfully, Is.True,
                    "pending-lookup-task-must-complete-before-rescue");
                Assert.That(completed.Response.IsCompletedSuccessfully, Is.True,
                    "pending-lookup-real-response-must-be-released-before-rescue");
                Assert.That(completed.Owner.CopyVoiceIdCommand.CanExecute(ValidVoiceId), Is.False,
                    "pending-lookup-actual-owner-must-retire-before-rescue");
                Assert.That(HasOnlyExpectedCleanupFailures(marker), Is.True,
                    "pending-lookup-unknown-cleanup-fault-must-remain-visible");
            });
        },
        () => { ownership?.Owner.Dispose(); return Task.CompletedTask; },
        () => { ownership?.Release(); return Task.CompletedTask; },
        () => ownership?.Operation ?? Task.CompletedTask);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.streaming/supersession-body-failure-joins-three-original-calls")]
    public async Task SupersessionBodyFailureJoinsThreeOriginalCallsBeforeRescue()
    {
        var marker = new InvalidOperationException("supersession-owned-body-failure");
        SupersessionOwnershipObservation? ownership = null;
        Task? scenario = null;
        var callbacks = 0;
        var threePendingCallsObserved = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            scenario = StreamingEntrySuite.RunSupersessionWaitsForOutputCleanupAcrossThreeCallsAsync(observed =>
            {
                // Retain each real acquisition immediately, so an unexpected
                // earlier scenario failure still leaves rescue its actual work.
                var previous = ownership;
                ownership = observed;
                callbacks++;
                if (previous is not null)
                {
                    Assert.That(observed.First, Is.SameAs(previous.First));
                    Assert.That(observed.Owner, Is.SameAs(previous.Owner));
                    Assert.That(observed.CleanupRelease, Is.SameAs(previous.CleanupRelease));
                    Assert.That(observed.InitializationWork, Is.SameAs(previous.InitializationWork));
                }
                if (observed.Second is null || observed.Third is null) return;
                Assert.Multiple(() =>
                {
                    Assert.That(observed.Startup, Is.SameAs(observed.InitializationWork.StartupTask));
                    Assert.That(observed.InitializationWork.StartupTask.IsCompletedSuccessfully, Is.True);
                    Assert.That(observed.InitializationWork.RetirementTasks, Is.Empty);
                    Assert.That(observed.CleanupEntered.IsCompletedSuccessfully, Is.True,
                        "supersession-fixture-must-enter-the-original-cleanup-barrier");
                    Assert.That(observed.CleanupRelease.IsCompleted, Is.False,
                        "supersession-fixture-must-retain-the-real-cleanup-barrier");
                    Assert.That(observed.First.IsCompleted, Is.False);
                    Assert.That(observed.Second!.IsCompleted, Is.False);
                    Assert.That(observed.Third!.IsCompleted, Is.False);
                    Assert.That(observed.First, Is.Not.SameAs(observed.Second));
                    Assert.That(observed.Second, Is.Not.SameAs(observed.Third));
                    Assert.That(ReadMainRetirementRequest(observed.Owner), Is.False);
                    Assert.That(observed.FirstSessionDisposals(), Is.Zero);
                    Assert.That(observed.CreatedSessions(), Is.EqualTo(1));
                });
                threePendingCallsObserved = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }

            Assert.That(threePendingCallsObserved, Is.True,
                "supersession-callback-must-have-observed-three-real-pending-calls");
            Assert.That(ownership, Is.Not.Null);
            var completed = ownership!;
            var completedScenario = scenario!;
            Assert.Multiple(() =>
            {
                Assert.That(callbacks, Is.EqualTo(3));
                Assert.That(completedScenario.IsCompleted, Is.True);
                Assert.That(completedScenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker),
                    "supersession-cleanup-must-preserve-primary-identity");
                Assert.That(completed.First.IsCompleted, Is.True,
                    "supersession-original-calls-must-complete-before-rescue");
                Assert.That(completed.Second!.IsCompleted, Is.True,
                    "supersession-original-calls-must-complete-before-rescue");
                Assert.That(completed.Third!.IsCompleted, Is.True,
                    "supersession-original-calls-must-complete-before-rescue");
                Assert.That(completed.First.IsCanceled, Is.True);
                Assert.That(completed.Second!.IsCanceled, Is.True);
                Assert.That(completed.Third!.IsCanceled, Is.True);
                Assert.That(completed.CleanupRelease.IsCompletedSuccessfully, Is.True);
                Assert.That(ReadMainRetirementRequest(completed.Owner), Is.True,
                    "supersession-actual-owner-must-retire-before-rescue");
                Assert.That(completed.InitializationWork.StartupTask, Is.SameAs(completed.Startup));
                Assert.That(completed.InitializationWork.StartupTask.IsCompletedSuccessfully, Is.True);
                var retirementTasks = completed.InitializationWork.RetirementTasks;
                Assert.That(retirementTasks, Is.Not.Empty,
                    "supersession-real-retirement-must-be-recorded-before-rescue");
                Assert.That(retirementTasks.All(task => task.IsCompletedSuccessfully), Is.True,
                    "supersession-real-retirement-tasks-must-complete-before-rescue");
                Assert.That(completed.FirstSessionDisposals(), Is.EqualTo(1));
                Assert.That(completed.CreatedSessions(), Is.EqualTo(1),
                    "failure-cleanup-must-not-create-another-synthesis-session");
                Assert.That(HasOnlyExpectedCleanupFailures(marker, typeof(TaskCanceledException).FullName, 3), Is.True,
                    "supersession-unknown-cleanup-fault-must-remain-visible");
            });
            // The request flag is supplementary. The real initialization handle
            // above also proves its original startup and recorded CTS-retirement
            // continuation tasks completed before independent rescue.
        },
        () => { ownership?.Owner.Dispose(); return Task.CompletedTask; },
        () => { ownership?.Release(); return Task.CompletedTask; },
        () => ownership?.Startup ?? Task.CompletedTask,
        () => JoinRetiredPlaybackAsync(ownership?.First),
        () => JoinRetiredPlaybackAsync(ownership?.Second),
        () => JoinRetiredPlaybackAsync(ownership?.Third),
        () => JoinInitializationRetirementAsync(ownership?.InitializationWork));
    }

    private static bool HasOnlyExpectedCleanupFailures(
        Exception primary, string? allowedType = null, int expectedCount = 0)
    {
        var value = primary.Data[RegressionFixtureOwner.CleanupTypesKey];
        if (value is null) return expectedCount == 0;
        return value is List<string> types && types.Count == expectedCount && allowedType is not null &&
            types.All(type => string.Equals(type, allowedType, StringComparison.Ordinal));
    }

    private static bool ReadMainRetirementRequest(Main owner)
    {
        var field = typeof(Main).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null || field.FieldType != typeof(bool))
            throw new InvalidOperationException("supersession-retirement-observation-unavailable");
        return field.GetValue(owner) is true;
    }

    private static async Task JoinInitializationRetirementAsync(
        PluginInitializationCoordinator.InitializationWorkHandle? work)
    {
        if (work is null) return;
        var retirementTasks = work.RetirementTasks;
        if (retirementTasks.Count == 0)
            throw new InvalidOperationException("supersession-rescue-has-no-retirement-task");
        await Task.WhenAll(retirementTasks);
    }

    private static async Task JoinRetiredPlaybackAsync(Task? operation)
    {
        if (operation is null) return;
        try { await operation; }
        catch (TaskCanceledException) when (operation.IsCanceled)
        {
            // Main was explicitly retired before releasing the original barrier.
            // An unknown fault, including a faulted Task throwing this type,
            // remains a rescue failure and is preserved by the existing owner.
        }
    }
}

internal sealed class PendingLookupOwnershipObservation(
    Task operation, Task response, Action release, SettingsViewModel owner, int httpCalls)
{
    internal Task Operation { get; } = operation;
    internal Task Response { get; } = response;
    internal Action Release { get; } = release;
    internal SettingsViewModel Owner { get; } = owner;
    internal int HttpCalls { get; } = httpCalls;
}

internal sealed class SupersessionOwnershipObservation(
    Task first, Task? second, Task? third, Task startup,
    PluginInitializationCoordinator.InitializationWorkHandle initializationWork,
    Task cleanupEntered, Task cleanupRelease,
    Action release, Main owner, Func<int> firstSessionDisposals, Func<int> createdSessions)
{
    internal Task First { get; } = first;
    internal Task? Second { get; } = second;
    internal Task? Third { get; } = third;
    internal Task Startup { get; } = startup;
    internal PluginInitializationCoordinator.InitializationWorkHandle InitializationWork { get; } = initializationWork;
    internal Task CleanupEntered { get; } = cleanupEntered;
    internal Task CleanupRelease { get; } = cleanupRelease;
    internal Action Release { get; } = release;
    internal Main Owner { get; } = owner;
    internal Func<int> FirstSessionDisposals { get; } = firstSessionDisposals;
    internal Func<int> CreatedSessions { get; } = createdSessions;
}
