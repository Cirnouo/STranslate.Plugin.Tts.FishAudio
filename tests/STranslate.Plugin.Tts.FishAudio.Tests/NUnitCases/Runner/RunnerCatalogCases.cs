using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RunnerCatalogCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/remaining-legacy-ids-and-partitions-are-complete")]
    public void RemainingLegacyIdsAndPartitionsAreComplete() =>
        RegressionCatalogSuite.RemainingLegacyIdsAndPartitionsAreComplete();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/static-card-contexts-keep-every-original-boundary")]
    public void StaticCardContextsKeepEveryOriginalBoundary() =>
        RegressionCatalogSuite.StaticCardContextsKeepEveryOriginalBoundary();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/dynamic-card-behaviors-keep-every-original-boundary")]
    public void DynamicCardBehaviorsKeepEveryOriginalBoundary() =>
        RegressionCatalogSuite.DynamicCardBehaviorsKeepEveryOriginalBoundary();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/remaining-native-aliases-keep-their-exact-ordered-owners")]
    public void RemainingNativeAliasesKeepTheirExactOrderedOwners() =>
        RegressionCatalogSuite.RemainingNativeAliasesKeepTheirExactOrderedOwners();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/offline-dispatcher-and-input-requests-remain-core")]
    public void OfflineDispatcherAndInputRequestsRemainCore() =>
        RegressionCatalogSuite.OfflineDispatcherAndInputRequestsRemainCore();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/original-legacy-argument-shapes-remain-valid")]
    public void OriginalLegacyArgumentShapesRemainValid() =>
        RegressionCatalogSuite.OriginalLegacyArgumentShapesRemainValid();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/invalid-legacy-argument-combinations-reject-before-fixture")]
    public Task InvalidLegacyArgumentCombinationsRejectBeforeFixtureAsync() =>
        RegressionCatalogSuite.InvalidLegacyArgumentCombinationsRejectBeforeFixtureAsync();

}
