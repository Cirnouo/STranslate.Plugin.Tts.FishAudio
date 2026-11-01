using NUnit.Framework;

namespace NUnitCases.Preview;

[TestFixture]
[NonParallelizable]
public sealed class PreviewFixtureFailureCases
{
    public static IEnumerable<TestCaseData> HeldResponseRows()
    {
        yield return new TestCaseData(false)
            .SetName("BodyAndCleanupFailure_OriginalMetadataTaskRetiresAndJoins")
            .SetProperty("FishAudioCaseId", "infra.preview/owned-body-and-cleanup-failure-joins-metadata-task");
        yield return new TestCaseData(true)
            .SetName("BodyAndCleanupFailure_OriginalStreamTaskRetiresAndJoins")
            .SetProperty("FishAudioCaseId", "infra.preview/owned-body-and-cleanup-failure-joins-stream-task");
    }

    [TestCaseSource(nameof(HeldResponseRows))]
    public Task BodyAndCleanupFailureRetainsOriginalOwner(bool streamResponse)
        => PreviewSuite.FixtureBodyFailureReleasesSameHeldTaskAsync(streamResponse);

    [Test]
    [Property("FishAudioCaseId", "infra.preview/owned-sole-cleanup-failure-remains-failure")]
    public Task SoleCleanupFailureRemainsFailure()
        => PreviewSuite.FixtureSoleCleanupFailureRemainsFailureAsync();
}
