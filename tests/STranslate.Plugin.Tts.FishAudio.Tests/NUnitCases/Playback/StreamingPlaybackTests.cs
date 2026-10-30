using NUnit.Framework;

namespace NUnitCases.Playback;

[TestFixture]
[NonParallelizable]
public sealed class StreamingPlaybackTests
{
    [Test]
    [Property("FishAudioCaseId", "playback.streaming/waveout-provides-200ms-in-four-50ms-buffers")]
    [Order(17)]
    public Task WaveOutProvidesTwoHundredMillisecondsInFourFiftyMillisecondBuffers() =>
        StreamingPlaybackSuite.RunOwnedAsync(() =>
        {
            StreamingPlaybackSuite.WaveOutProvidesTwoHundredMillisecondsInFourFiftyMillisecondBuffers();
            return Task.CompletedTask;
        });

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/short-pcm-starts-after-100ms-with-blocked-input")]
    [Order(18)]
    public Task StartsShortPcmAfter100MillisecondsWhileNextChunkBlocksAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.StartsShortPcmAfter100MillisecondsWhileNextChunkBlocksAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/starts-at-250ms-without-timer")]
    [Order(19)]
    public Task StartsAt250MillisecondsWithoutWaitingForTimerAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.StartsAt250MillisecondsWithoutWaitingForTimerAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/pcm-byte-carry-and-odd-tail")]
    [Order(20)]
    public Task CarriesSinglePcmBytesAndRejectsOddTailAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CarriesSinglePcmBytesAndRejectsOddTailAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/device-position-drain-including-underrun-silence")]
    [Order(21)]
    public Task DrainsActualDevicePositionIncludingUnderrunSilenceAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.DrainsActualDevicePositionIncludingUnderrunSilenceAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/large-pcm-bounds-and-low-water-resume")]
    [Order(22)]
    public Task BoundsLargePcmAndResumesOnlyAtLowWaterAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.BoundsLargePcmAndResumesOnlyAtLowWaterAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/device-failure-interrupts-waiting-phases")]
    [Order(23)]
    public Task DeviceFailureInterruptsAllWaitingPhasesAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.DeviceFailureInterruptsAllWaitingPhasesAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/cancellation-and-disposal-interrupt-waiting-phases")]
    [Order(24)]
    public Task CancellationAndDisposalInterruptAllWaitingPhasesAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CancellationAndDisposalInterruptAllWaitingPhasesAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/startup-failures-never-notify")]
    [Order(25)]
    public Task StartupFailuresNeverNotifyAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.StartupFailuresNeverNotifyAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/stalled-real-data-and-idle-input")]
    [Order(26)]
    public Task DetectsStalledRealDataWithoutTimingOutIdleInputAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.DetectsStalledRealDataWithoutTimingOutIdleInputAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/waveout-position-wrap-and-unexpected-stops")]
    [Order(27)]
    public Task WaveOutTracks32BitPositionWrapAndUnexpectedStops() =>
        StreamingPlaybackSuite.RunOwnedAsync(() =>
        {
            StreamingPlaybackSuite.WaveOutTracks32BitPositionWrapAndUnexpectedStops();
            return Task.CompletedTask;
        });

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/mp3-split-frames-and-vbr-metadata")]
    [Order(28)]
    public Task Mp3DecodesSplitFramesAndVbrMetadataIncrementally() =>
        StreamingPlaybackSuite.RunOwnedAsync(() =>
        {
            StreamingPlaybackSuite.Mp3DecodesSplitFramesAndVbrMetadataIncrementally();
            return Task.CompletedTask;
        });

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/mp3-truncation-and-oversized-metadata")]
    [Order(29)]
    public Task Mp3RejectsTruncationAndOversizedMetadata() =>
        StreamingPlaybackSuite.RunOwnedAsync(() =>
        {
            StreamingPlaybackSuite.Mp3RejectsTruncationAndOversizedMetadata();
            return Task.CompletedTask;
        });

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/mp3-frame-backpressure")]
    [Order(30)]
    public Task StopsDecodingLargeMp3EventAtPcmBackpressureAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.StopsDecodingLargeMp3EventAtPcmBackpressureAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/uncooperative-input-interruption")]
    [Order(31)]
    public Task CancellationInterruptsUncooperativeInputAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CancellationInterruptsUncooperativeInputAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/pcm-ring-cap-and-wraparound")]
    [Order(32)]
    public Task PcmRingCapsFiveSecondsAndTracksWraparound() =>
        StreamingPlaybackSuite.RunOwnedAsync(() =>
        {
            StreamingPlaybackSuite.PcmRingCapsFiveSecondsAndTracksWraparound();
            return Task.CompletedTask;
        });

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/cancellation-during-play-never-notifies")]
    [Order(33)]
    public Task CancellationDuringPlayNeverPublishesStartedAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CancellationDuringPlayNeverPublishesStartedAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/cleanup-preserves-primary-and-attempts-all-resources")]
    [Order(34)]
    public Task CleanupPreservesOriginalFailureAndAttemptsEveryResourceAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CleanupPreservesOriginalFailureAndAttemptsEveryResourceAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/cleanup-failure-after-success")]
    [Order(35)]
    public Task CleanupFailureSurfacesWhenPlaybackSucceedsAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CleanupFailureSurfacesWhenPlaybackSucceedsAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/input-cleanup-preserves-decoder-failure")]
    [Order(36)]
    public Task InputCleanupCannotReplaceDecoderFailureAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.InputCleanupCannotReplaceDecoderFailureAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/synchronous-large-input-starts-before-further-production")]
    [Order(37)]
    public Task SynchronousLargeInputStartsBeforeFurtherProductionAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.SynchronousLargeInputStartsBeforeFurtherProductionAsync);

    [Test]
    [Property("FishAudioCaseId", "playback.streaming/cancellation-callback-preserves-playback-failure")]
    [Order(38)]
    public Task CancellationCallbackCannotReplacePlaybackFailureAsync() =>
        StreamingPlaybackSuite.RunOwnedAsync(StreamingPlaybackSuite.CancellationCallbackCannotReplacePlaybackFailureAsync);
}
