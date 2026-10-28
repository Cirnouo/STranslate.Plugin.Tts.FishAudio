using NUnit.Framework;

namespace NUnitCompatibility;

// These new boundary checks supplement the original legacy hook scenarios.
// The shared owner retains process exit, both drains and private result checks.
[TestFixture]
[NonParallelizable]
public sealed class FrameworkHookAdmissionCases
{
    [TestCaseSource(nameof(AdmissionRows))]
    public Task InstalledHookRejectsInvalidFrameworkEvidence(string scenario) =>
        HookAcceptanceSuite.RunAsync(scenario);

    private static IEnumerable<TestCaseData> AdmissionRows()
    {
        yield return new TestCaseData("platform-command-failure")
            .SetName("InstalledHook_PlatformCommandFailure")
            .SetProperty("FishAudioCaseId", "runner.hooks/platform-command-failure");
        yield return new TestCaseData("platform-missing-terminal")
            .SetName("InstalledHook_PlatformMissingTerminal")
            .SetProperty("FishAudioCaseId", "runner.hooks/platform-missing-terminal");
        yield return new TestCaseData("platform-wrong-case")
            .SetName("InstalledHook_PlatformWrongCase")
            .SetProperty("FishAudioCaseId", "runner.hooks/platform-wrong-case");
        yield return new TestCaseData("late-framework-evidence-mutation")
            .SetName("InstalledHook_LateFrameworkEvidenceMutation")
            .SetProperty("FishAudioCaseId", "runner.hooks/late-framework-evidence-mutation");
    }
}
