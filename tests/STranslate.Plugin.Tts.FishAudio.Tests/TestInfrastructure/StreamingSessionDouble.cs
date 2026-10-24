using STranslate.Plugin.Tts.FishAudio.Streaming;

internal sealed class StreamingSessionDouble : IStreamingTtsSession
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<SynthesisRequest, string, CancellationToken, Task> Handler { get; set; } = (_, _, _) => Task.CompletedTask;
    internal SynthesisRequest? Request { get; private set; }
    internal string? Text { get; private set; }
    internal int Calls { get; private set; }
    internal int DisposeCount { get; private set; }
    internal bool CancellationObserved { get; private set; }

    public async Task PlayAsync(SynthesisRequest request, string text, CancellationToken cancellationToken)
    {
        ++Calls;
        Request = request;
        Text = text;
        using var registration = cancellationToken.Register(() => CancellationObserved = true);
        Entered.TrySetResult();
        try { await Handler(request, text, cancellationToken).ConfigureAwait(false); }
        finally
        {
            // A synchronous handler continuation can dispose the earlier callback
            // registration before cancellation reaches it. Observe only while active,
            // and never overwrite a callback's sticky true with an uncanceled false.
            if (cancellationToken.IsCancellationRequested) CancellationObserved = true;
        }
    }

    public void Dispose() => ++DisposeCount;
}
