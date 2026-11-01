using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class OrdinaryPopupProofDirectoryCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/proof-directory/body-failure")]
    public Task BodyFailureRetainsItsIdentityAfterTheActualOwnedOutputTreeIsRemoved() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("ordinary-popup-proof-body-failure");
            var owner = new OrdinaryPopupProofDirectory();
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    await owner.RunAsync(directory =>
                    {
                        var bytes = new byte[] { 0, 1, 2, 13, 10, 255 };
                        var path = Path.Combine(directory, "proof-bytes.bin");
                        File.WriteAllBytes(path, bytes);
                        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                        File.WriteAllText(Path.Combine(directory, "proof-assembly.json"), "{}");
                        throw primary;
                    });
                }
                catch (Exception error) { observed = error; }
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(owner.DirectoryPath, Is.Not.Null);
                Assert.That(Directory.Exists(owner.DirectoryPath), Is.False);
                Assert.That(owner.CleanupComplete, Is.True);
                Assert.That(owner.CleanupFailure, Is.Null);
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null);
            }, owner.DeleteOwnedDirectoryAsync);
        }));

    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/proof-directory/cleanup-failure-preserves-primary")]
    public Task ActualReadOnlyOutputCleanupFailureRemainsSecondaryBeforeIndependentRescue() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var primary = new InvalidOperationException("ordinary-popup-proof-body-with-read-only-output");
            var owner = new OrdinaryPopupProofDirectory();
            string? output = null;
            var bytes = new byte[] { 0, 13, 10, 128, 255 };
            await RegressionFixtureOwner.RunAsync(async () =>
            {
                Exception? observed = null;
                try
                {
                    await owner.RunAsync(directory =>
                    {
                        output = Path.Combine(directory, "read-only-proof-bytes.bin");
                        File.WriteAllBytes(output, bytes);
                        File.SetAttributes(output, FileAttributes.ReadOnly);
                        throw primary;
                    });
                }
                catch (Exception error) { observed = error; }
                // The real storage failure and original bytes precede rescue.
                Assert.That(observed, Is.SameAs(primary));
                Assert.That(owner.CleanupFailure, Is.Not.Null);
                Assert.That(owner.CleanupComplete, Is.False);
                Assert.That(Directory.Exists(owner.DirectoryPath), Is.True);
                Assert.That(output, Is.Not.Null);
                Assert.That(File.ReadAllBytes(output!), Is.EqualTo(bytes));
                Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey],
                    Is.EqualTo(new[] { owner.CleanupFailure!.GetType().FullName }));
            },
            () =>
            {
                if (output is not null && File.Exists(output)) File.SetAttributes(output, FileAttributes.Normal);
                return Task.CompletedTask;
            }, owner.DeleteOwnedDirectoryAsync);
        }));
}
