using NUnit.Framework;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RunnerCiInfrastructureCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-infrastructure/capture-uses-only-the-explicit-producer-environment")]
    public void CaptureUsesOnlyTheExplicitProducerEnvironment() =>
        RegressionCiInfrastructureSuite.CaptureUsesOnlyTheExplicitProducerEnvironment();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-infrastructure/runner-and-child-forward-the-same-immutable-owner")]
    public Task RunnerAndChildForwardTheSameImmutableOwnerAsync() =>
        RegressionCiInfrastructureSuite.RunnerAndChildForwardTheSameImmutableOwnerAsync();

    [Test]
    [Property("FishAudioCaseId", "infra.runner/ci-infrastructure/real-power-shell-helpers-preserve-evidence-and-complete-pagination")]
    public Task RealPowerShellHelpersPreserveEvidenceAndCompletePaginationAsync() =>
        RegressionCiInfrastructureSuite.RealPowerShellHelpersPreserveEvidenceAndCompletePaginationAsync();

}
