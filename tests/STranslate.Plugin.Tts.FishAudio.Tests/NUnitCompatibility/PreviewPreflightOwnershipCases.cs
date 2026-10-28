using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class PreviewPreflightOwnershipCases
{
    private static IEnumerable<TestCaseData> PreflightFailureCases()
    {
        yield return new TestCaseData(false)
            .SetName("InvalidSearchUrlBodyFailureRetiresOriginalPreflightOwner")
            .SetProperty("FishAudioCaseId", "infra.preview/preflight-invalid-url-body-failure-retires-owned-vm");
        yield return new TestCaseData(true)
            .SetName("OfflineSearchPreviewBodyFailureRetiresOriginalPreflightOwner")
            .SetProperty("FishAudioCaseId", "infra.preview/preflight-offline-body-failure-retires-owned-vm");
    }

    [TestCaseSource(nameof(PreflightFailureCases))]
    public async Task BodyFailureRetiresOriginalPreflightOwner(bool offline)
    {
        using var network = OverrideNetworkAvailability(!offline);
        var marker = new InvalidOperationException("preview-preflight-owned-body-failure");
        var logger = new TestLogger();
        var snackbar = new TestSnackbar();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer();
        var factoryCalls = 0;
        SettingsViewModel? viewModel = null;
        Task? ownedCommandTask = null;
        ExceptionDispatchInfo? testFailure = null;
        ExceptionDispatchInfo? rescueFailure = null;
        var ownershipVerified = false;
        var voiceId = offline ? "fedcba9876543210fedcba9876543210" : "0123456789abcdef0123456789abcdef";
        try
        {
            // These are the original two preflight inputs. No held HTTP reply,
            // audio buffer, player Open or artificial incomplete Task is added.
            var context = offline ? CreateContext(snackbar, httpService: httpService) : CreateContext(logger: logger);
            viewModel = new SettingsViewModel(context, new Settings(),
                clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => { factoryCalls++; return player; });
            var item = new VoiceSearchItem
            {
                Id = voiceId,
                SampleAudioUrl = offline ? "https://platform.r2.fish.audio/audio/search.mp3" : "file:///C:/Windows/win.ini",
            };
            if (offline)
            {
                if (!viewModel.SearchResults.Contains(item)) viewModel.SearchResults.Add(item);
            }
            else viewModel.SearchResults = [item];
            try { viewModel.ToggleSearchItemPreviewCommand.Execute(item); }
            finally { ownedCommandTask = viewModel.ToggleSearchItemPreviewCommand.ExecutionTask; }

            Assert.That(ownedCommandTask, Is.Not.Null, "preflight-owner-must-capture-the-original-command-task");
            Assert.That(ownedCommandTask!.IsCompletedSuccessfully, Is.True,
                "These actual preflight paths complete before their first await; this is not a pending-response test.");
            Assert.That(viewModel.ToggleSearchItemPreviewCommand.ExecutionTask, Is.SameAs(ownedCommandTask));
            Assert.That(viewModel.CopyVoiceIdCommand.CanExecute(voiceId), Is.True,
                "preflight-owner-must-still-be-live-before-its-body-failure");
            Assert.That(factoryCalls, Is.Zero, "preflight-must-not-acquire-a-player");
            Assert.That(player.PlayCallCount, Is.Zero);
            Assert.That(player.DisposeCallCount, Is.Zero);
            Assert.That(viewModel.PreviewingVoiceId, Is.Null);
            if (offline)
            {
                Assert.That(snackbar.LastError, Is.EqualTo(FishAudioRequestPolicy.NetworkUnavailableKey));
                Assert.That(http.GetCallCount, Is.Zero);
                Assert.That(http.GetAsStreamCallCount, Is.Zero);
            }
            else
            {
                Assert.That(logger.Contains(LogLevel.Warning, "Rejected preview audio URL"), Is.True);
            }

            ExceptionDispatchInfo? primaryFailure = null;
            try { throw marker; }
            catch (InvalidOperationException error) { primaryFailure = ExceptionDispatchInfo.Capture(error); }
            Exception? observed = null;
            try { await PreviewSuite.FinishSearchPreflightAsync(viewModel, ownedCommandTask, primaryFailure); }
            catch (InvalidOperationException error) { observed = error; }

            // All observations precede rescue. A NotImplementedException passes
            // through to finally and remains the direct RED failure, not a hang.
            Assert.That(observed, Is.SameAs(marker), "preflight-cleanup-must-preserve-the-original-body-failure");
            Assert.That(viewModel.ToggleSearchItemPreviewCommand.ExecutionTask, Is.SameAs(ownedCommandTask));
            Assert.That(ownedCommandTask!.IsCompletedSuccessfully, Is.True);
            Assert.That(viewModel.CopyVoiceIdCommand.CanExecute(voiceId), Is.False,
                "preflight-cleanup-must-retire-the-same-real-viewmodel");
            Assert.That(factoryCalls, Is.Zero, "cleanup-cannot-create-a-player-the-preflight-never-acquired");
            Assert.That(player.PlayCallCount, Is.Zero);
            Assert.That(player.StopCallCount, Is.Zero);
            Assert.That(player.DisposeCallCount, Is.Zero, "An unused factory result is not an owned player to dispose.");
            ownershipVerified = true;
        }
        catch (Exception error) { testFailure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            if (!ownershipVerified)
            {
                // Retain the same actual Task and VM on RED or assertion failure.
                // No replacement task, retry, new budget or player disposal is used.
                try
                {
                    if (ownedCommandTask is not null) await ownedCommandTask;
                }
                catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
                try { viewModel?.Dispose(); }
                catch (Exception error) { rescueFailure ??= ExceptionDispatchInfo.Capture(error); }
            }
        }
        testFailure?.Throw();
        rescueFailure?.Throw();
    }
}
