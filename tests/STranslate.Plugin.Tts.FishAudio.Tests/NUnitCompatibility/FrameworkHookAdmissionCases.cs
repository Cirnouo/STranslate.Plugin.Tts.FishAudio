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
        yield return new TestCaseData("late-hybrid-generation")
            .SetName("InstalledHook_LateHybridGeneration")
            .SetProperty("FishAudioCaseId", "runner.hooks/late-hybrid-generation");
        yield return new TestCaseData("late-hybrid-attempt")
            .SetName("InstalledHook_LateHybridAttempt")
            .SetProperty("FishAudioCaseId", "runner.hooks/late-hybrid-attempt");
        yield return new TestCaseData("installed-receipt-helper-mutation")
            .SetName("InstalledHook_InstalledReceiptHelperMutation")
            .SetProperty("FishAudioCaseId", "runner.hooks/installed-receipt-helper-mutation");
        yield return new TestCaseData("atomic-install-copy-failure")
            .SetName("InstalledHook_AtomicInstallCopyFailure")
            .SetProperty("FishAudioCaseId", "runner.hooks/atomic-install-copy-failure");
        yield return new TestCaseData("protocol-upgrade")
            .SetName("InstalledHook_ProtocolUpgrade")
            .SetProperty("FishAudioCaseId", "runner.hooks/protocol-upgrade");
    }
}
