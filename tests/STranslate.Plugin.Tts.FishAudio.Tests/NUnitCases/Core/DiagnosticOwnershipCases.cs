using NUnit.Framework;

namespace NUnitCases.Core;

// Complete original scenario responsibilities use framework discovery and execution.
[TestFixture]
[NonParallelizable]
public sealed class DiagnosticOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry")]
    public Task DeadlineDiagnosticsPreserveOriginalFailureWithoutRetry()
        => OperationActivityDispatcherSuite.DeadlineDiagnosticsPreserveOriginalFailureWithoutRetryAsync();

    [Test]
    [Property("FishAudioCaseId", "input.settings/native-tool-tip-diagnostics-preserve-failures-without-retry")]
    public Task NativeToolTipDiagnosticsPreserveFailuresWithoutRetry()
        => SettingsInputInteractionSuite.NativeToolTipDiagnosticsPreserveFailuresWithoutRetryAsync();

    [Test]
    [Property("FishAudioCaseId", "popup.search-adjustment/matrix-diagnostics-preserve-failures-without-retry")]
    public Task MatrixDiagnosticsPreserveFailuresWithoutRetry()
        => SearchAdjustmentControlSuite.MatrixDiagnosticsPreserveFailuresWithoutRetryAsync();

    [Test]
    [Property("FishAudioCaseId", "popup.search-adjustment/viewport-diagnostics-preserve-async-completion-and-failures")]
    public Task ViewportDiagnosticsPreserveAsyncCompletionAndFailures()
        => SearchAdjustmentControlSuite.ViewportDiagnosticsPreserveAsyncCompletionAndFailuresAsync();
}
