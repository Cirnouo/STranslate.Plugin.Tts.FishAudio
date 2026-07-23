using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class FishAudioApiSuite
{
    internal static async Task PostTtsRequestHonorsModelSpecificProsodyAndTimeoutAsync()
    {
        foreach (var model in new[] { FishAudioModelPolicy.S21ProFreeModel, FishAudioModelPolicy.S21ProModel, FishAudioModelPolicy.S2ProModel })
        {
            var supportedSettings = CreateTtsSettings(model);
            var (supportedHttpService, supportedHttp) = TestHttpServiceProxy.Create();

            await FishAudioApi.PostTtsAsync(CreateContext(settings: supportedSettings, httpService: supportedHttpService), supportedSettings, "hello", CancellationToken.None);

            var supportedBody = AssertDictionary(supportedHttp.LastPostBody, $"{model} TTS request body should be a dictionary");
            var supportedProsody = AssertDictionary(supportedBody["prosody"], $"{model} TTS prosody should be a dictionary");
            var supportedHeaders = AssertHeaders(supportedHttp.LastPostOptions, $"{model} TTS request should include headers");

            AssertEqual(true, supportedProsody.ContainsKey("normalize_loudness"), $"{model} TTS request should include normalize_loudness");
            AssertEqual(false, supportedProsody["normalize_loudness"], $"{model} TTS request should preserve normalize_loudness setting");
            AssertEqual(model, supportedHeaders["model"], $"{model} TTS request should include selected model header");
        }

        var settings = CreateTtsSettings(FishAudioModelPolicy.S2ProModel);
        var (httpService, http) = TestHttpServiceProxy.Create();

        await FishAudioApi.PostTtsAsync(CreateContext(settings: settings, httpService: httpService), settings, "hello", CancellationToken.None);

        var body = AssertDictionary(http.LastPostBody, "TTS request body should be a dictionary");
        var prosody = AssertDictionary(body["prosody"], "TTS prosody should be a dictionary");

        AssertEqual("hello", body["text"], "TTS request should include text");
        AssertEqual("mp3", body["format"], "TTS request should request mp3 output");
        AssertEqual(128, body["mp3_bitrate"], "TTS request should preserve mp3 bitrate setting");
        AssertEqual(0.41, body["temperature"], "TTS request should preserve temperature setting");
        AssertEqual(0.82, body["top_p"], "TTS request should preserve top_p setting");
        AssertEqual(true, body["normalize"], "TTS request should preserve text normalization setting");
        AssertEqual("low", body["latency"], "TTS request should preserve latency setting");
        AssertEqual(false, body["condition_on_previous_chunks"], "TTS request should preserve context conditioning setting");
        AssertEqual("fedcba9876543210fedcba9876543210", body["reference_id"], "TTS request should include non-empty voice ID");
        AssertEqual(1.25, prosody["speed"], "TTS request should preserve speed setting");
        AssertEqual(-2.5, prosody["volume"], "TTS request should preserve volume setting");
        var headers = AssertHeaders(http.LastPostOptions, "TTS request should include headers");
        AssertEqual($"Bearer {AppliedKey}", headers["Authorization"], "TTS request should include API Key bearer token");
        AssertEqual(FishAudioModelPolicy.S2ProModel, headers["model"], "TTS request should include selected model header");
        AssertEqual(TimeSpan.FromSeconds(60), http.LastPostOptions?.Timeout, "TTS request should set an explicit timeout");

        settings = CreateTtsSettings(FishAudioModelPolicy.S1Model);
        settings.NormalizeLoudness = true;
        (httpService, http) = TestHttpServiceProxy.Create();

        await FishAudioApi.PostTtsAsync(CreateContext(settings: settings, httpService: httpService), settings, "hello", CancellationToken.None);

        prosody = AssertDictionary(
            AssertDictionary(http.LastPostBody, "s1 TTS request body should be a dictionary")["prosody"],
            "s1 TTS prosody should be a dictionary");

        headers = AssertHeaders(http.LastPostOptions, "s1 TTS request should include headers");
        AssertEqual(FishAudioModelPolicy.S1Model, headers["model"], "s1 TTS request should include selected model header");
        AssertEqual(false, prosody.ContainsKey("normalize_loudness"), "s1 TTS request should omit unsupported normalize_loudness");
        AssertEqual(1.25, prosody["speed"], "s1 TTS request should still include speed");
        AssertEqual(-2.5, prosody["volume"], "s1 TTS request should still include volume");
    }
}
