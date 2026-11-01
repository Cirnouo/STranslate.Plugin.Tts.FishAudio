using NUnit.Framework;

namespace NUnitCases.NativeCards;

// Offline fixture-failure controls are additional framework work, outside the native profile.
[TestFixture]
[NonParallelizable]
public sealed class NativeCardsTempFixtureOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/temp-owner/body-failure-preserves-identity-and-completes-cleanup")]
    public async Task BodyFailurePreservesIdentityAndCompletesAllCleanup()
    {
        var owner = new NativeCardsTempFixtureOwner();
        var primary = new InvalidOperationException("native-card-fixture-body-failure");
        var cleanup = new IOException("native-card-fixture-resource-failure");
        var disposed = new List<int>();
        string? directory = null;
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await owner.RunAsync(() =>
        {
            directory = owner.CreateSettingsDirectory();
            File.WriteAllText(Path.Combine(directory, "favorite-voices.json"), "[]");
            owner.Own(new CleanupProbe(() =>
            {
                Assert.That(Directory.Exists(directory), Is.True, "The directory outlives every acquired resource.");
                disposed.Add(1);
            }));
            owner.Own(new CleanupProbe(() =>
            {
                Assert.That(Directory.Exists(directory), Is.True, "Resource failure cannot trigger early root deletion.");
                disposed.Add(2);
                throw cleanup;
            }));
            throw primary;
        }));
        Assert.That(error, Is.SameAs(primary));
        Assert.That(disposed, Is.EqualTo(new[] { 2, 1 }), "Every acquired resource retires in reverse acquisition order.");
        Assert.That(directory, Is.Not.Null);
        Assert.That(Directory.Exists(directory), Is.False, "Cleanup failure still attempts owned directory deletion.");
        Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Does.Contain(typeof(IOException).FullName));
        await Task.CompletedTask;
    }

    [Test]
    [Property("FishAudioCaseId", "infra.native-cards/temp-owner/construction-failure-removes-acquired-directory")]
    public async Task ConstructionFailureRemovesTheAlreadyAcquiredDirectory()
    {
        var owner = new NativeCardsTempFixtureOwner();
        var primary = new InvalidOperationException("native-card-fixture-construction-failure");
        string? directory = null;
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await owner.RunAsync(() =>
        {
            directory = owner.CreateSettingsDirectory();
            throw primary;
        }));
        Assert.That(error, Is.SameAs(primary));
        Assert.That(directory, Is.Not.Null);
        Assert.That(Directory.Exists(directory), Is.False);
        await Task.CompletedTask;
    }

    private sealed class CleanupProbe(Action cleanup) : IDisposable
    {
        public void Dispose() => cleanup();
    }
}
