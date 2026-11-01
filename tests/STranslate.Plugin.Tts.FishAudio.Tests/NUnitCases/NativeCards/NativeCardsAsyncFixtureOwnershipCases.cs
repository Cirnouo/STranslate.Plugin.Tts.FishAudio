using NUnit.Framework;

namespace NUnitCases.NativeCards;

// Additional offline failure controls; no member belongs to the ordered native profile.
[TestFixture]
[NonParallelizable]
public sealed class NativeCardsAsyncFixtureOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/async-owner/failure-joins-disposal-created-successor-before-directory-cleanup")]
    public async Task BodyFailureJoinsDisposalCreatedSuccessorBeforeDirectoryCleanup()
    {
        var owner = new NativeCardsAsyncFixtureOwner();
        var primary = new InvalidOperationException("native-card-async-body-failure");
        var cleanup = new IOException("native-card-async-facade-cleanup-failure");
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? directory = null;
        var disposed = new List<int>();
        var joined = false;
        Exception? observed = null;
        async Task SuccessorAsync()
        {
            try { await response.Task; }
            catch (OperationCanceledException) { }
            Assert.That(Directory.Exists(directory), Is.True, "A disposal-created successor completes before root deletion.");
            joined = true;
        }
        try
        {
            await owner.RunAsync(() =>
            {
                directory = owner.CreateSettingsDirectory();
                owner.OwnResponse(response);
                owner.OwnFacade(new CleanupProbe(() =>
                {
                    disposed.Add(1);
                    owner.TrackOperation(SuccessorAsync());
                    throw cleanup;
                }));
                owner.OwnFacade(new CleanupProbe(() => disposed.Add(2)));
                throw primary;
            });
        }
        catch (Exception error) { observed = error; }
        Assert.That(observed, Is.SameAs(primary));
        Assert.That(disposed, Is.EqualTo(new[] { 2, 1 }));
        Assert.That(joined, Is.True);
        Assert.That(response.Task.IsCompleted, Is.True);
        Assert.That(Directory.Exists(directory), Is.False);
        Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Does.Contain(typeof(IOException).FullName));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/async-owner/constructor-failure-retires-acquired-facade-and-directory")]
    public async Task ConstructorFailureRetiresTheAcquiredFacadeAndDirectory()
    {
        var owner = new NativeCardsAsyncFixtureOwner();
        var primary = new InvalidOperationException("native-card-async-construction-failure");
        string? directory = null;
        var disposed = false;
        Exception? observed = null;
        try
        {
            await owner.RunAsync(() =>
            {
                directory = owner.CreateSettingsDirectory();
                owner.OwnFacade(new CleanupProbe(() =>
                {
                    Assert.That(Directory.Exists(directory), Is.True);
                    disposed = true;
                }));
                throw primary;
            });
        }
        catch (Exception error) { observed = error; }
        Assert.That(observed, Is.SameAs(primary));
        Assert.That(disposed, Is.True);
        Assert.That(Directory.Exists(directory), Is.False);
    }

    private sealed class CleanupProbe(Action cleanup) : IDisposable
    {
        public void Dispose() => cleanup();
    }
}
