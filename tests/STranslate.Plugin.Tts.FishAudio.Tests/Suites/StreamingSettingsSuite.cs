using System.Text.Json;
using System.Xml.Linq;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

internal static class StreamingSettingsSuite
{
    private static readonly DateTimeOffset BeforeCutoff = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    internal static void MissingConfigurationLoadsPcmAndSystemProxy()
    {
        var context = CreateContext();
        ((ContextProxy)context).LoadSettings = () => null!;
        var settings = SettingsStore.Load(context, BeforeCutoff, out _);
        AssertEqual(2, settings.SchemaVersion, "Missing configuration starts at schema 2");
        AssertEqual("pcm", settings.AudioFormat, "Missing configuration starts with PCM");
        AssertEqual("system", settings.StreamingProxyMode, "Missing configuration starts with system proxy");
        AssertEqual("", settings.StreamingProxyAddress, "Missing configuration has no custom address");
    }

    internal static void LegacyFormatsMigrateAndCurrentDefaultsUsePcm()
    {
        AssertEqual(2, SettingsSchema.Current, "Streaming settings use schema 2");
        AssertEqual("pcm", new Settings().AudioFormat, "New settings use PCM");
        foreach (var schema in new[] { "", "\"SchemaVersion\":0,", "\"SchemaVersion\":1," })
        {
            var settings = Read("{" + schema + "\"ApiKey\":\"legacy-key\",\"VoiceId\":\"voice\",\"SelectedModel\":\"s2-pro\",\"Mp3Bitrate\":128}");
            AssertEqual(2, settings.SchemaVersion, "Legacy schema upgrades to 2");
            AssertEqual("mp3", settings.AudioFormat, "Missing legacy format remains MP3");
            AssertEqual(128, settings.Mp3Bitrate, "Legacy bitrate remains intact");
            AssertEqual("legacy-key", settings.ApiKey, "Migration preserves key");
            AssertEqual("voice", settings.VoiceId, "Migration preserves voice");
            AssertEqual("s2-pro", settings.SelectedModel, "Migration preserves selected model");
            AssertEqual(true, settings.NeedsCanonicalSave, "Legacy schema needs canonical save");
        }

        var current = Read("{\"SchemaVersion\":2}");
        AssertEqual("pcm", current.AudioFormat, "Missing schema 2 format uses PCM");
        AssertEqual("system", current.StreamingProxyMode, "Missing proxy mode uses system");
        AssertEqual("", current.StreamingProxyAddress, "Missing proxy address is empty");
        AssertEqual(false, current.NeedsCanonicalSave, "Current sparse settings retain existing canonical-save semantics");

        AssertEqual("pcm", Read("{\"SchemaVersion\":1,\"AudioFormat\":\"pcm\"}").AudioFormat, "Explicit valid legacy format is preserved");
        AssertEqual("mp3", Read("{\"SchemaVersion\":2,\"AudioFormat\":\"mp3\"}").AudioFormat, "Explicit current MP3 format is preserved");

        foreach (var schema in new[] { 0, 1, 2 })
        {
            foreach (var invalid in new[] { "\"wav\"", "17", "null", "\"PCM\"", "\"pcm\",\"AudioFormat\":\"mp3\"" })
            {
                var settings = Read($"{{\"SchemaVersion\":{schema},\"AudioFormat\":{invalid},\"Mp3Bitrate\":64}}");
                AssertEqual(schema < 2 ? "mp3" : "pcm", settings.AudioFormat, "Invalid or duplicate format uses its source schema default");
                AssertEqual(64, settings.Mp3Bitrate, "Invalid format preserves bitrate");
                AssertEqual(true, settings.NeedsCanonicalSave, "Invalid format requires cleanup");
            }
        }
    }

    internal static void FutureAndDuplicateSchemasRemainReadOnly()
    {
        foreach (var json in new[]
        {
            "{\"SchemaVersion\":99,\"AudioFormat\":\"future-format\",\"StreamingProxyAddress\":\"https://name:secret@proxy.test:443\",\"Unknown\":true}",
            "{\"SchemaVersion\":1,\"SchemaVersion\":2,\"AudioFormat\":\"mp3\"}",
        })
        {
            var settings = Read(json);
            AssertEqual(true, settings.IsReadOnly, "Unknown or duplicated schemas are read-only");
            var context = CreateContext(settings: settings);
            _ = SettingsStore.Load(context, BeforeCutoff);
            SettingsStore.Save(context, settings);
            AssertEqual(0, ((ContextProxy)context).SaveCount, "Read-only settings never overwrite host storage");
        }

        var future = Read("{\"SchemaVersion\":99}");
        AssertEqual(99, future.SchemaVersion, "Future schema number is retained");
    }

    internal static void InvalidStreamingFieldTypesDefaultIndependently()
    {
        foreach (var fields in new[]
        {
            "\"AudioFormat\":false,\"StreamingProxyMode\":[],\"StreamingProxyAddress\":17",
            "\"AudioFormat\":\"mp3\",\"AudioFormat\":\"pcm\",\"StreamingProxyMode\":\"custom\",\"StreamingProxyMode\":\"direct\",\"StreamingProxyAddress\":\"one\",\"StreamingProxyAddress\":\"two\"",
        })
        {
            var settings = Read("{\"SchemaVersion\":2,\"ApiKey\":\"preserved\",\"Mp3Bitrate\":128," + fields + "}");
            AssertEqual("pcm", settings.AudioFormat, "Malformed or duplicate format uses PCM");
            AssertEqual("system", settings.StreamingProxyMode, "Malformed or duplicate mode uses system");
            AssertEqual("", settings.StreamingProxyAddress, "Malformed or duplicate address uses empty default");
            AssertEqual("preserved", settings.ApiKey, "Bad streaming fields preserve existing settings");
            AssertEqual(128, settings.Mp3Bitrate, "Bad streaming fields preserve MP3 bitrate");
            AssertEqual(true, settings.NeedsCanonicalSave, "Bad streaming fields require canonical save");
        }
    }

    internal static void NewFieldsRoundTripAndSnapshotsPreserveAllSettings()
    {
        var original = CreatePopulatedSettings();
        var restored = Read(JsonSerializer.Serialize(original));
        AssertEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(restored), "All persisted fields round-trip");
        AssertEqual(false, restored.NeedsCanonicalSave, "Known new fields need no cleanup");
        var snapshot = SettingsStore.CaptureSnapshot(original);
        AssertEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(snapshot), "Snapshot copies all settings");
        original.AudioFormat = "pcm";
        original.StreamingProxyMode = "direct";
        original.StreamingProxyAddress = "";
        original.CachedVoice!.Title = "changed";
        AssertEqual("mp3", snapshot.AudioFormat, "Snapshot format is detached");
        AssertEqual("custom", snapshot.StreamingProxyMode, "Snapshot mode is detached");
        AssertEqual("https://name:secret@proxy.test:8443/", snapshot.StreamingProxyAddress, "Snapshot preserves proxy credentials");
        AssertEqual("Saved voice", snapshot.CachedVoice!.Title, "Snapshot deep-copies cached metadata");
    }

    internal static void LeaseCommitAndRollbackPreserveStreamingSettings()
    {
        var storage = CreatePopulatedSettings();
        var baseline = JsonSerializer.Serialize(storage);
        var context = CreateContext(settings: storage);
        var candidate = SettingsStore.Load(context, BeforeCutoff, out var lease);
        AssertEqual(baseline, JsonSerializer.Serialize(candidate), "Initialization clone includes every setting");
        candidate.AudioFormat = "pcm";
        candidate.StreamingProxyMode = "direct";
        ((ContextProxy)context).OnSave = () => throw new InvalidOperationException("Expected save failure");
        try
        {
            SettingsStore.Save(context, candidate, lease);
            throw new InvalidOperationException("Expected host save to fail");
        }
        catch (InvalidOperationException error) when (error.Message == "Expected save failure") { }
        AssertEqual(baseline, JsonSerializer.Serialize(storage), "Save failure rolls back every stored setting");
        ((ContextProxy)context).OnSave = null;
        SettingsStore.Save(context, candidate, lease);
        AssertEqual("pcm", storage.AudioFormat, "Successful lease save copies format");
        AssertEqual("direct", storage.StreamingProxyMode, "Successful lease save copies mode");
        AssertEqual("https://name:secret@proxy.test:8443/", storage.StreamingProxyAddress, "Switching modes preserves custom address");

        var beforeUpdate = JsonSerializer.Serialize(candidate);
        ((ContextProxy)context).OnSave = () => throw new InvalidOperationException("Expected update failure");
        try
        {
            SettingsStore.TryUpdateAndSave(context, candidate, lease, updated =>
            {
                updated.AudioFormat = "mp3";
                updated.StreamingProxyMode = "custom";
                updated.StreamingProxyAddress = "socks5://other:1080";
                updated.ApiKey = "replacement";
                return true;
            });
            throw new InvalidOperationException("Expected update save to fail");
        }
        catch (InvalidOperationException error) when (error.Message == "Expected update failure") { }
        AssertEqual(beforeUpdate, JsonSerializer.Serialize(candidate), "Failed update preserves runtime snapshot");
        AssertEqual(beforeUpdate, JsonSerializer.Serialize(storage), "Failed update preserves stored snapshot");
    }

    internal static void ProxyValidationAndNormalizationKeepCredentialsAndModeIntent()
    {
        foreach (var address in new[]
        {
            "http://localhost:8080", "https://proxy.test:443/", "socks5://127.0.0.1:1080",
            "http://name:secret@proxy.test:80", "socks5://name:p%40ss@proxy.test:1080", "http://[::1]:8080/",
        })
            AssertEqual(true, SettingsValidation.IsValidStreamingProxyAddress(address), "Explicit supported proxy address is valid: " + address);
        foreach (var address in new[]
        {
            "", "proxy.test:8080", "http://proxy.test", "https://proxy.test/", "socks4://proxy.test:1080",
            "http://proxy.test:0", "http://proxy.test:65536", "http://proxy.test:8080/path", "http://proxy.test:8080/path/..", "http://proxy.test:8080/?q=1",
            "http://proxy.test:8080/#fragment", "http://proxy.test:8080?", "http://proxy.test:8080#", "http://:8080",
            " http://proxy.test:8080", "http://proxy.test:8080 ", "http:\\proxy.test:8080", "http://proxy.test:/",
        })
            AssertEqual(false, SettingsValidation.IsValidStreamingProxyAddress(address), "Unsupported proxy address is invalid: " + address);
        AssertEqual(true, SettingsValidation.IsValidStreamingProxyConfiguration("system", "invalid"), "System ignores retained address");
        AssertEqual(true, SettingsValidation.IsValidStreamingProxyConfiguration("direct", "invalid"), "Direct ignores retained address");
        AssertEqual(false, SettingsValidation.IsValidStreamingProxyConfiguration("custom", "invalid"), "Invalid custom address fails preflight");
        AssertEqual(false, SettingsValidation.IsValidStreamingProxyConfiguration("other", "http://proxy.test:8080"), "Unknown mode fails direct validation");
        var settings = CreatePopulatedSettings();
        settings.AudioFormat = "wav";
        settings.StreamingProxyMode = "custom";
        settings.StreamingProxyAddress = "invalid but retained";
        SettingsNormalizer.Normalize(settings, BeforeCutoff);
        AssertEqual("pcm", settings.AudioFormat, "Invalid runtime format falls back to new PCM default");
        AssertEqual("custom", settings.StreamingProxyMode, "Invalid custom address preserves explicit mode");
        AssertEqual("invalid but retained", settings.StreamingProxyAddress, "Invalid address stays editable and visible");
        settings.StreamingProxyMode = "invalid-mode";
        SettingsNormalizer.Normalize(settings, BeforeCutoff);
        AssertEqual("system", settings.StreamingProxyMode, "Unknown stored mode uses system default");
        AssertEqual("invalid but retained", settings.StreamingProxyAddress, "Mode normalization preserves retained address");
    }

    internal static void ModelOrderDefaultsAndSavedChoicesStayIndependent()
    {
        AssertEnumerableEqual(new[] { "s2.1-pro-free", "drama-3", "s2.1-pro", "s2-pro", "s1" }, FishAudioModelPolicy.GetAvailableModels(BeforeCutoff), "Free precedes capability groups");
        AssertEnumerableEqual(new[] { "drama-3", "s2.1-pro", "s2-pro", "s1" }, FishAudioModelPolicy.GetAvailableModels(FishAudioModelPolicy.FreeModelCutoffUtc), "Cutoff removes only expired free option");
        AssertEqual("s2.1-pro-free", FishAudioModelPolicy.GetDefaultModel(BeforeCutoff), "Free remains pre-cutoff default");
        AssertEqual("s2.1-pro", FishAudioModelPolicy.GetDefaultModel(FishAudioModelPolicy.FreeModelCutoffUtc), "S2.1 remains post-cutoff default independent of ranking");
        foreach (var model in new[] { "drama-3", "s2.1-pro", "s2-pro", "s1" })
        {
            var settings = new Settings { SelectedModel = model };
            AssertEqual(false, SettingsNormalizer.NormalizeSelectedModel(settings, FishAudioModelPolicy.FreeModelCutoffUtc), "Valid saved choice remains unchanged");
            AssertEqual(model, settings.SelectedModel, "Model ranking never replaces saved choice");
        }
        AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness("drama-3"), "Drama has no normalize-loudness support");
        AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness("s1"), "S1 remains unsupported");
        AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness("s2-pro"), "Existing S2 capability remains supported");
    }

    internal static void ViewModelFormatProxyAndModelTogglesPreserveSettings()
    {
        var settings = CreatePopulatedSettings();
        var context = CreateContext(settings: settings, pluginCacheDirectoryPath: CreateTempDirectory());
        using var vm = new SettingsViewModel(context, settings, BeforeCutoff);
        AssertEqual(0, ((ContextProxy)context).SaveCount, "Constructing view model does not write settings");
        AssertEqual(true, vm.IsMp3BitrateEnabled, "MP3 enables bitrate control");
        vm.AudioFormat = "pcm";
        AssertEqual(false, vm.IsMp3BitrateEnabled, "PCM disables bitrate control");
        AssertEqual(64, vm.Mp3Bitrate, "PCM retains remembered MP3 bitrate");
        vm.AudioFormat = "mp3";
        AssertEqual(64, settings.Mp3Bitrate, "Returning to MP3 restores remembered bitrate");
        AssertEqual(true, vm.IsCustomStreamingProxy, "Custom enables address editing");
        vm.StreamingProxyMode = "direct";
        AssertEqual(false, vm.IsCustomStreamingProxy, "Direct disables custom address input");
        AssertEqual("https://name:secret@proxy.test:8443/", vm.StreamingProxyAddress, "Mode changes preserve credentials");
        vm.StreamingProxyMode = "custom";
        vm.StreamingProxyAddress = "invalid";
        AssertEqual(true, vm.HasStreamingProxyValidationError, "Invalid custom setting is visible");
        vm.StreamingProxyMode = "system";
        AssertEqual(false, vm.HasStreamingProxyValidationError, "System ignores retained invalid address");
        AssertEqual("invalid", settings.StreamingProxyAddress, "Ignoring address never clears it");
        vm.SelectedModel = "drama-3";
        AssertEqual(false, vm.IsNormalizeLoudnessEnabled, "Drama disables loudness switch");
        AssertEqual(true, vm.NormalizeLoudness, "Unsupported model retains stored loudness preference");
        vm.ApplyAvailableModels(FishAudioModelPolicy.FreeModelCutoffUtc);
        AssertEqual("drama-3", vm.SelectedModel, "Refreshing ranked choices preserves Drama selection");
        AssertEqual(true, vm.IsApiKeyInputEnabled, "Format/proxy changes preserve API key lock behavior");
    }

    internal static void StreamingSettingsUiResourcesAreComplete()
    {
        var view = File.ReadAllText(FindRepoFile("STranslate.Plugin.Tts.FishAudio/Presentation/View/SettingsView.xaml"));
        AssertEqual(true, view.Contains("SelectedItem=\"{Binding AudioFormat}\"", StringComparison.Ordinal), "Format selector binds persisted value");
        AssertEqual(true, view.Contains("IsEnabled=\"{Binding IsMp3BitrateEnabled}\"", StringComparison.Ordinal), "Bitrate is enabled only for MP3");
        AssertEqual(true, view.Contains("IsEnabled=\"{Binding IsCustomStreamingProxy}\"", StringComparison.Ordinal), "Custom proxy address is enabled only in custom mode");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var locale in new[] { "en", "zh-cn", "zh-tw", "ja", "ko" })
        {
            var document = XDocument.Load(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"));
            foreach (var suffix in new[] { "Format_Description", "StreamingProxy", "StreamingProxy_Description", "StreamingProxy_System", "StreamingProxy_Direct", "StreamingProxy_Custom", "StreamingProxy_Address", "StreamingProxy_Invalid", "Streaming_Failed" })
                AssertEqual(true, document.Root!.Elements().Any(e => (string?)e.Attribute(x + "Key") == "STranslate_Plugin_Tts_FishAudio_" + suffix && !string.IsNullOrWhiteSpace(e.Value)), $"{locale} translates {suffix}");
        }
    }

    private static Settings Read(string json) => JsonSerializer.Deserialize<Settings>(json)!;

    private static Settings CreatePopulatedSettings() => new()
    {
        ApiKey = AppliedKey, VoiceId = "0123456789abcdef0123456789abcdef", SelectedModel = "s2-pro",
        AudioFormat = "mp3", StreamingProxyMode = "custom", StreamingProxyAddress = "https://name:secret@proxy.test:8443/",
        Mp3Bitrate = 64, Speed = 1.25, Volume = -1.3, NormalizeLoudness = true,
        Temperature = 0.4, TopP = 0.8, Latency = "low", Normalize = true, ConditionOnPreviousChunks = false,
        IsS21ProFreePromoDismissed = true,
        CachedVoice = new CachedVoiceInfo { Title = "Saved voice", Description = "Description", CoverImage = "", AuthorName = "Author", TaskCount = 42, SampleAudioUrl = null },
    };
}
