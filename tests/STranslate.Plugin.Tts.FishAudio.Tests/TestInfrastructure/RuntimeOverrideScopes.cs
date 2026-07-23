using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Runtime;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class RuntimeOverrideScopes
{
    internal static NetworkAvailabilityOverride OverrideNetworkAvailability(bool available)
    {
        var current = FishAudioRequestPolicy.NetworkAvailableOverride;
        var state = new NetworkAvailabilityOverride(current, available);
        FishAudioRequestPolicy.NetworkAvailableOverride = () => state.IsAvailable;
        return state;
    }

    internal static LocalUtcNowOverride OverrideLocalUtcNow(DateTimeOffset nowUtc)
    {
        var current = FishAudioClock.LocalUtcNowOverride;
        FishAudioClock.LocalUtcNowOverride = () => nowUtc;
        return new LocalUtcNowOverride(current);
    }
}

internal sealed class NetworkAvailabilityOverride : IDisposable
{
    private readonly Func<bool>? _previous;
    private bool _isAvailable;

    public NetworkAvailabilityOverride(Func<bool>? previous, bool isAvailable)
    {
        _previous = previous;
        _isAvailable = isAvailable;
    }

    public bool IsAvailable => _isAvailable;

    public void Set(bool isAvailable) => _isAvailable = isAvailable;

    public void Dispose() => FishAudioRequestPolicy.NetworkAvailableOverride = _previous;
}

internal sealed class LocalUtcNowOverride : IDisposable
{
    private readonly Func<DateTimeOffset>? _previous;

    public LocalUtcNowOverride(Func<DateTimeOffset>? previous)
    {
        _previous = previous;
    }

    public void Dispose() => FishAudioClock.LocalUtcNowOverride = _previous;
}
