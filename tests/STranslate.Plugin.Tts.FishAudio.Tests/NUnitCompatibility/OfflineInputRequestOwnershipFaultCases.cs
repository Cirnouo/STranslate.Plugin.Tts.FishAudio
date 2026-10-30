using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class OfflineInputRequestOwnershipFaultCases
{
    private const string VoiceId = "11111111111111111111111111111111";

    [Test]
    [Property("FishAudioCaseId", "infra.input-clear/early-api-lock-assertion-failure-joins-original-request")]
    public Task EarlyApiLockAssertionFailureJoinsTheActualPendingSearch() => AssertFailureAsync(
        acquired => InputClearSuite.RunApiClearUsesAcceptedSettingsOwnerOwnedAsync(false, acquired),
        owner =>
        {
            var view = owner.Views.Single();
            Assert.That(view.IsApiKeyInputLocked, Is.True);
            Assert.That(view.ApiKey, Is.EqualTo("accepted"));
            Assert.That(owner.Commands, Does.Contain(view.SearchVoicesCommand.ExecutionTask));
        }, cleanupFault: false);

    [Test]
    [Property("FishAudioCaseId", "infra.input-clear/by-id-clear-assertion-failure-joins-cancellation-ignoring-request")]
    public Task ByIdClearAssertionFailureJoinsTheActualCancellationIgnoringLookup() => AssertFailureAsync(
        acquired => InputClearSuite.RunByIdClearRetiresItsInputOwnerOwnedAsync(true, acquired),
        owner =>
        {
            var view = owner.Views.Single();
            Assert.That(view.VoiceIdInput, Is.Empty);
            Assert.That(view.IsSubmittingVoiceId, Is.False);
            Assert.That(owner.Commands, Does.Contain(view.SubmitVoiceIdCommand.ExecutionTask));
        }, cleanupFault: false);

    [Test]
    [Property("FishAudioCaseId", "infra.search-adjustment/body-failure-joins-both-original-held-requests")]
    public Task ReplacementAssertionFailureJoinsBothActualHeldRequests() => AssertFailureAsync(
        acquired => SearchAdjustmentSuite.RunPendingReplacementsCannotRollBackIntentOwnedAsync("failure", acquired),
        VerifyBothPendingRequests, cleanupFault: false);

    [Test]
    [Property("FishAudioCaseId", "infra.search-adjustment/body-and-cleanup-failure-preserves-primary-after-real-request-joins")]
    public Task BodyAndCleanupFailurePreservesPrimaryAfterJoiningBothActualRequests() => AssertFailureAsync(
        acquired => SearchAdjustmentSuite.RunPendingReplacementsCannotRollBackIntentOwnedAsync("failure", acquired),
        VerifyBothPendingRequests, cleanupFault: true);

    private static void VerifyBothPendingRequests(OfflineInputRequestFixtureOwner owner)
    {
        Assert.That(owner.Replies, Has.Count.EqualTo(2));
        Assert.That(owner.Commands.Count(task => !task.IsCompleted), Is.EqualTo(2));
        Assert.That(owner.Views.Single().SearchLanguages, Is.EqualTo(new[] { "de" }));
        Assert.That(owner.Views.Single().IsSearching, Is.True);
    }

    private static async Task AssertFailureAsync(Func<Action<OfflineInputRequestFixtureOwner>, Task> run,
        Action<OfflineInputRequestFixtureOwner> verify, bool cleanupFault)
    {
        var marker = new InvalidOperationException("offline-input-owned-body-failure");
        OfflineInputRequestFixtureOwner? owned = null;
        CleanupFault? failingCleanup = null;
        var injected = false;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var scenario = run(owner =>
            {
                owned = owner;
                Assert.That(owner.Views, Has.Count.EqualTo(1));
                Assert.That(owner.Replies, Is.Not.Empty);
                Assert.That(owner.Replies.All(reply => reply.ObservedCalls == 1 && !reply.Task.IsCompleted), Is.True,
                    "Observe the actual raw HTTP Tasks while they remain pending.");
                Assert.That(owner.Commands.Any(task => !task.IsCompleted), Is.True);
                Assert.That(owner.Views[0].CopyVoiceIdCommand.CanExecute(VoiceId), Is.True);
                verify(owner);
                if (cleanupFault) owner.Track(failingCleanup = new CleanupFault());
                injected = true;
                throw marker;
            });
            Exception? failure = null;
            try { await scenario; }
            catch (Exception error) { failure = error; }
            Assert.That(injected, Is.True);
            Assert.That(owned, Is.Not.Null);
            var completed = owned!;
            // These are the authoritative ownership assertions, before rescue.
            Assert.Multiple(() =>
            {
                Assert.That(scenario.IsFaulted, Is.True);
                Assert.That(failure, Is.SameAs(marker));
                Assert.That(completed.Commands.All(task => task.IsCompletedSuccessfully), Is.True,
                    "Join the actual original command/coordinator Tasks, not WaitAsync guards.");
                Assert.That(completed.Replies.All(reply => reply.Task.IsCompletedSuccessfully), Is.True,
                    "The same actual raw HTTP Tasks must already be terminal before rescue.");
                Assert.That(completed.Views.All(view => !view.CopyVoiceIdCommand.CanExecute(VoiceId)), Is.True);
                if (cleanupFault)
                {
                    Assert.That(failingCleanup!.Attempts, Is.EqualTo(1));
                    Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey],
                        Is.EqualTo(new[] { typeof(IOException).FullName }));
                }
                else Assert.That(marker.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
            });
        }, () => RescueAsync(owned));
    }

    private sealed class CleanupFault : IDisposable
    {
        internal int Attempts { get; private set; }
        public void Dispose()
        {
            Attempts++;
            throw new IOException("offline-input-owned-cleanup-failure");
        }
    }

    private static Task RescueAsync(OfflineInputRequestFixtureOwner? owner)
    {
        if (owner is null) return Task.CompletedTask;
        var stages = new List<Func<Task>>();
        foreach (var reply in owner.Replies)
            stages.Add(() =>
            {
                // Independently use the original source rather than candidate Release.
                reply.Source.TrySetResult(reply.CleanupResponse);
                return Task.CompletedTask;
            });
        foreach (var command in owner.Commands) stages.Add(() => command);
        foreach (var reply in owner.Replies)
            stages.Add(async () =>
            {
                try { await reply.Task; }
                catch (Exception error) when (ReferenceEquals(error, reply.ExpectedFailure)) { }
            });
        foreach (var resource in owner.Resources.Reverse())
        {
            if (resource is CleanupFault) continue;
            stages.Add(() =>
            {
                if (resource is STranslate.Plugin.Tts.FishAudio.ViewModel.SettingsViewModel view
                    && !view.CopyVoiceIdCommand.CanExecute(VoiceId)) return Task.CompletedTask;
                resource.Dispose();
                return Task.CompletedTask;
            });
        }
        return RegressionFixtureOwner.RunAsync(() => Task.CompletedTask, stages.ToArray());
    }
}
