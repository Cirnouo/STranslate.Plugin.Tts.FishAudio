using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class TestAudioPlayer : IAudioPlayer
{
    public int PlayBytesCallCount { get; private set; }
    public int PlayUrlCallCount { get; private set; }
    public byte[]? LastPlayedBytes { get; private set; }
    public string? LastPlayedUrl { get; private set; }

    public Task PlayAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        PlayBytesCallCount++;
        LastPlayedBytes = bytes;
        return Task.CompletedTask;
    }

    public Task PlayAsync(string url, CancellationToken cancellationToken)
    {
        PlayUrlCallCount++;
        LastPlayedUrl = url;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

public sealed class TestPreviewAudioPlayer : IPreviewAudioPlayer
{
    public event EventHandler? Opened;
    public event EventHandler? Ended;
    public event Action<Exception?>? Failed;

    public TimeSpan? Duration { get; set; }
    public TimeSpan Position { get; set; }
    public double Volume { get; set; }
    public Uri? LastOpenedUri { get; private set; }
    public int PlayCallCount { get; private set; }
    public int StopCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public void Open(Uri source) => LastOpenedUri = source;

    public void Play() => PlayCallCount++;

    public void Stop() => StopCallCount++;

    public void Dispose() => DisposeCallCount++;

    public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);

    public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);

    public void RaiseFailed(Exception? exception = null) => Failed?.Invoke(exception);
}
