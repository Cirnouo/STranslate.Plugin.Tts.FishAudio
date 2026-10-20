namespace STranslate.Plugin.Tts.FishAudio.Streaming;

internal interface IStreamingAudioPlayer : IDisposable
{
    Task PlayAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        string format,
        Action playbackStarted,
        StreamingActivity activity,
        CancellationToken cancellationToken);
}

internal enum StreamingBackpressure
{
    EncodedQueue,
    PcmBuffer,
}

internal enum StreamingStage
{
    Request,
    Connected,
    PrefixSent,
    FirstAudio,
    FirstPcm,
    PlaybackStarted,
    Finish,
    Drained,
}

internal readonly record struct StreamingObservation(StreamingStage Stage, TimeSpan Elapsed);
