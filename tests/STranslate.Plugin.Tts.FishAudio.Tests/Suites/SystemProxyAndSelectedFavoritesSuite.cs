using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static FavoriteVoicesSuite;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static class SystemProxyAndSelectedFavoritesSuite
{
    internal static async Task RetiredProxyInputsCannotBlockFormalReadingAsync()
    {
        using var network = OverrideNetworkAvailability(false);
        foreach (var mode in new[] { "direct", "custom", "unknown" })
        {
            var settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, ApiKey = AppliedKey, VoiceId = Id(1), SelectedModel = "s2-pro",
                StreamingProxyMode = mode, StreamingProxyAddress = "invalid-retired-private-address",
            }))!;
            var session = new StreamingSessionDouble();
            using var plugin = StreamingEntrySuite.CreatePlugin(() => session);
            plugin.Init(CreateContext(settings: settings));
            await plugin.PendingStartupTask.WaitAsync(TimeSpan.FromSeconds(2));
            network.Set(true);
            await plugin.PlayAudioAsync("Retired routing preferences cannot block reading.");
            AssertEqual(1, session.Calls, "Obsolete direct/custom/unknown settings cannot block the formal entry point");
            AssertEqual(Id(1), session.Request!.VoiceId, "Proxy removal must preserve the selected voice in the synthesis snapshot");
            network.Set(false);
        }
    }

    internal static void RetiredProxyInputsAreDiscardedAndSocketUsesSystemProxy()
    {
        var previous = HttpClient.DefaultProxy;
        var systemProxy = new WebProxy("http://system-fixture.invalid:3128");
        HttpClient.DefaultProxy = systemProxy;
        try
        {
            foreach (var mode in new[] { "direct", "custom", "unknown" })
            {
                var settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new
                {
                    SchemaVersion = 2, ApiKey = AppliedKey, VoiceId = Id(1), SelectedModel = "s2-pro",
                    AudioFormat = "mp3", Mp3Bitrate = 64, Speed = 1.25,
                    StreamingProxyMode = mode, StreamingProxyAddress = "invalid-retired-private-address",
                }))!;
                var context = CreateContext(settings: settings);
                var loaded = SettingsStore.Load(context, DateTimeOffset.UtcNow);
                var canonical = JsonSerializer.Serialize(loaded);
                AssertEqual(false, canonical.Contains("StreamingProxy", StringComparison.Ordinal),
                    "An ordinary canonical save must discard retired proxy settings without a schema bump");
                AssertEqual(1, ((ContextProxy)(object)context).SaveCount, "Retired fields require one canonical cleanup save");
                AssertEqual(2, loaded.SchemaVersion, "Proxy removal retains settings schema 2");
                AssertEqual(64, loaded.Mp3Bitrate, "Discarding proxy fields must preserve remembered bitrate");
                AssertEqual(1.25, loaded.Speed, "Discarding proxy fields must preserve synthesis parameters");
                using var socket = new FishAudioSocket(new SynthesisRequest(SettingsStore.CaptureSnapshot(loaded)));
                var transport = (ClientWebSocket)typeof(FishAudioSocket).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(socket)!;
                AssertEqual(true, ReferenceEquals(systemProxy, transport.Options.Proxy),
                    "Legacy direct/custom fields must not override the actual WebSocket's system proxy");
            }
        }
        finally { HttpClient.DefaultProxy = previous; }
    }

}
