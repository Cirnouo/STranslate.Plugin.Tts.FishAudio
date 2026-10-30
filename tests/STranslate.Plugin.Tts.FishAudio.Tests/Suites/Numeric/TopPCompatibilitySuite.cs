using NUnit.Framework;
using System.Text.Json;
using MessagePack;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Streaming;
using static ContextProxy;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class TopPCompatibilitySuite
{
    private static readonly DateTimeOffset PolicyTime = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Property("FishAudioCaseId", "numeric.top-p/client-floor-keeps-displayed-precision-defaults-and-temperature-zero")]
    [Order(284)]
    public static void ClientFloorKeepsDisplayedPrecisionDefaultsAndTemperatureZero()
    {
        AssertEqual(0.7, Settings.DefaultTopP, "The client compatibility floor must not change the existing default");
        foreach (var (input, stored, _) in Cases())
        {
            var settings = new Settings { SelectedModel = "s2-pro", TopP = input, Temperature = 0 };
            SettingsNormalizer.Normalize(settings, PolicyTime);
            AssertEqual(stored, settings.TopP, "TopP normalization must use the client floor, displayed precision, and invalid-value default");
            AssertEqual(0.0, settings.Temperature, "Temperature zero remains valid independently of the TopP compatibility floor");
            AssertEqual(false, SettingsNormalizer.Normalize(settings, PolicyTime), "A normalized value must not drift or repeatedly require cleanup");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "numeric.top-p/old-zero-canonically-rewrites-once-and-saved-floor-round-trips")]
    [Order(285)]
    public static void OldZeroCanonicallyRewritesOnceAndSavedFloorRoundTrips()
    {
        foreach (var schema in new[] { 1, 2 })
        {
            var legacy = JsonSerializer.Deserialize<Settings>($$"""
                {"SchemaVersion":{{schema}},"ApiKey":"{{AppliedKey}}","VoiceId":"0123456789abcdef0123456789abcdef","SelectedModel":"s2-pro","TopP":0,"Temperature":0,"Mp3Bitrate":128,"Speed":1.25}
                """)!;
            var context = CreateContext(settings: legacy);
            var loaded = SettingsStore.Load(context, PolicyTime);
            AssertEqual(0.05, loaded.TopP, "A supported old zero must load at the client compatibility floor");
            AssertEqual(1, ((ContextProxy)(object)context).SaveCount, "A changed old zero must be canonically saved once");
            AssertEqual(2, loaded.SchemaVersion, "Numeric compatibility does not advance settings schema 2");
            AssertEqual(AppliedKey, loaded.ApiKey, "Canonical numeric cleanup preserves the saved key");
            AssertEqual(1.25, loaded.Speed, "Canonical numeric cleanup preserves other synthesis parameters");
            AssertEqual(128, loaded.Mp3Bitrate, "Canonical numeric cleanup preserves remembered bitrate");
            AssertEqual(schema == 1 ? "mp3" : "pcm", loaded.AudioFormat, "The source-schema format default remains unchanged");
            var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(loaded))!;
            var restarted = CreateContext(settings: restored);
            _ = SettingsStore.Load(restarted, PolicyTime);
            AssertEqual(0.05, restored.TopP, "The saved lowest value must survive a restart");
            AssertEqual(0, ((ContextProxy)(object)restarted).SaveCount, "A saved floor value must not need another canonical rewrite");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "numeric.top-p/future-and-duplicate-schemas-keep-their-read-only-protection")]
    [Order(286)]
    public static void FutureAndDuplicateSchemasKeepTheirReadOnlyProtection()
    {
        foreach (var json in new[]
        {
            "{\"SchemaVersion\":99,\"SelectedModel\":\"s2-pro\",\"TopP\":0}",
            "{\"SchemaVersion\":1,\"SchemaVersion\":2,\"SelectedModel\":\"s2-pro\",\"TopP\":0}",
        })
        {
            var settings = JsonSerializer.Deserialize<Settings>(json)!;
            var context = CreateContext(settings: settings);
            var loaded = SettingsStore.Load(context, PolicyTime);
            SettingsStore.Save(context, loaded);
            AssertEqual(true, loaded.IsReadOnly, "Numeric compatibility must preserve future/duplicate schema read-only protection");
            AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "A local compatibility correction must never overwrite protected settings");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "numeric.top-p/raw-settings-cannot-send-zero-through-http-or-native-payloads")]
    [Order(287)]
    public static async Task RawSettingsCannotSendZeroThroughHttpOrNativePayloadsAsync()
    {
        foreach (var (input, _, wire) in Cases())
        {
            var settings = new Settings { SelectedModel = "s2-pro", ApiKey = AppliedKey, TopP = input, Temperature = 0 };
            var request = new SynthesisRequest(settings);
            using var native = JsonDocument.Parse(MessagePackSerializer.ConvertToJson(FishAudioStreamingProtocol.Start(request)));
            var nativeBody = native.RootElement.GetProperty("request");
            AssertEqual(wire, nativeBody.GetProperty("top_p").GetDouble(), "The actual native start payload must guard bounds while preserving valid raw precision");
            AssertEqual(0.0, nativeBody.GetProperty("temperature").GetDouble(), "Native temperature zero remains unchanged");
            var (service, http) = TestHttpServiceProxy.Create();
            await FishAudioApi.PostTtsAsync(CreateContext(httpService: service), settings, "Offline numeric fixture", CancellationToken.None);
            using var rest = JsonDocument.Parse(JsonSerializer.Serialize(http.LastPostBody));
            AssertEqual(wire, rest.RootElement.GetProperty("top_p").GetDouble(), "The actual HTTP body must use the same defensive compatibility policy");
            AssertEqual(0.0, rest.RootElement.GetProperty("temperature").GetDouble(), "HTTP temperature zero remains unchanged");
            AssertEqual(input, settings.TopP, "Request guards must not mutate their caller's raw Settings");
            settings.TopP = 1;
            AssertEqual(wire, request.TopP, "The native request must retain its guarded immutable snapshot");
        }
    }

    private static IEnumerable<(double Input, double Stored, double Wire)> Cases() =>
    [
        (0, 0.05, 0.05), (double.Epsilon, 0.05, 0.05), (0.001, 0.05, 0.05), (0.024, 0.05, 0.05), (0.025, 0.05, 0.05),
        (0.05, 0.05, 0.05), (0.074, 0.07, 0.074), (0.075, 0.08, 0.075), (0.7, 0.7, 0.7),
        (0.724, 0.72, 0.724), (0.726, 0.73, 0.726), (0.82, 0.82, 0.82), (1, 1, 1),
        (-0.001, 0.7, 0.7), (1.001, 0.7, 0.7), (double.NaN, 0.7, 0.7), (double.PositiveInfinity, 0.7, 0.7), (double.NegativeInfinity, 0.7, 0.7),
    ];
}
