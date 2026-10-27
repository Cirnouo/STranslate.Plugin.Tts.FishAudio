using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
public sealed class BuildCompatibilityCases
{
    [Test]
    [Property("FishAudioCaseId", "compat.build/release-full-runs-both-catalogs")]
    public Task FullReleaseRunsLegacyAndFrameworkOnceInPowerShellCore() =>
        BuildScriptSuite.FullReleaseRunsLegacyAndFrameworkOnceAsync();

    [Test]
    [Property("FishAudioCaseId", "compat.build/framework-failure-keeps-package-and-cleanup")]
    public Task FrameworkFailurePreservesExitCodeAndInspectedPackageAfterCleanup() =>
        BuildScriptSuite.FrameworkFailureKeepsExitCodePackageAndCleanupAsync();

    [Test]
    [Property("FishAudioCaseId", "compat.build/legacy-only-skips-nunit")]
    public Task LegacyOnlyLeavesFrameworkExecutionWithTheOuterCoordinator() =>
        BuildScriptSuite.LegacyOnlyCoordinatorSkipsFrameworkAsync();
}
