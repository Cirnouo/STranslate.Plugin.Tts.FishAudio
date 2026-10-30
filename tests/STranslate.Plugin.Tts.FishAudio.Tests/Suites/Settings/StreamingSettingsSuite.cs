using System.Text.Json;
using System.Xml.Linq;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

[NUnit.Framework.TestFixture]
[NUnit.Framework.NonParallelizable]
internal static class StreamingSettingsSuite
{
    private static readonly DateTimeOffset BeforeCutoff = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/missing-configuration-loads-pcm")]
    [NUnit.Framework.Order(8)]
    public static void MissingConfigurationLoadsPcm()
    {
        var context = CreateContext();
        ((ContextProxy)context).LoadSettings = () => null!;
        var settings = SettingsStore.Load(context, BeforeCutoff, out _);
        AssertEqual(2, settings.SchemaVersion, "Missing configuration starts at schema 2");
        AssertEqual("pcm", settings.AudioFormat, "Missing configuration starts with PCM");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/legacy-formats-migrate-and-current-defaults-use-pcm")]
    [NUnit.Framework.Order(9)]
    public static void LegacyFormatsMigrateAndCurrentDefaultsUsePcm()
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

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/future-and-duplicate-schemas-remain-read-only")]
    [NUnit.Framework.Order(10)]
    public static void FutureAndDuplicateSchemasRemainReadOnly()
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

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/invalid-streaming-field-types-default-independently")]
    [NUnit.Framework.Order(11)]
    public static void InvalidStreamingFieldTypesDefaultIndependently()
    {
        foreach (var fields in new[]
        {
            "\"AudioFormat\":false,\"StreamingProxyMode\":[],\"StreamingProxyAddress\":17",
            "\"AudioFormat\":\"mp3\",\"AudioFormat\":\"pcm\",\"StreamingProxyMode\":\"custom\",\"StreamingProxyMode\":\"direct\",\"StreamingProxyAddress\":\"one\",\"StreamingProxyAddress\":\"two\"",
        })
        {
            var settings = Read("{\"SchemaVersion\":2,\"ApiKey\":\"preserved\",\"Mp3Bitrate\":128," + fields + "}");
            AssertEqual("pcm", settings.AudioFormat, "Malformed or duplicate format uses PCM");
            AssertEqual("preserved", settings.ApiKey, "Bad streaming fields preserve existing settings");
            AssertEqual(128, settings.Mp3Bitrate, "Bad streaming fields preserve MP3 bitrate");
            AssertEqual(true, settings.NeedsCanonicalSave, "Bad streaming fields require canonical save");
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/new-fields-round-trip-and-snapshots-preserve-all-settings")]
    [NUnit.Framework.Order(12)]
    public static void NewFieldsRoundTripAndSnapshotsPreserveAllSettings()
    {
        var original = CreatePopulatedSettings();
        var restored = Read(JsonSerializer.Serialize(original));
        AssertEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(restored), "All persisted fields round-trip");
        AssertEqual(false, restored.NeedsCanonicalSave, "Known new fields need no cleanup");
        var snapshot = SettingsStore.CaptureSnapshot(original);
        AssertEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(snapshot), "Snapshot copies all settings");
        original.AudioFormat = "pcm";
        original.CachedVoice!.Title = "changed";
        AssertEqual("mp3", snapshot.AudioFormat, "Snapshot format is detached");
        AssertEqual("Saved voice", snapshot.CachedVoice!.Title, "Snapshot deep-copies cached metadata");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/lease-commit-and-rollback-preserve-streaming-settings")]
    [NUnit.Framework.Order(13)]
    public static void LeaseCommitAndRollbackPreserveStreamingSettings()
    {
        var storage = CreatePopulatedSettings();
        var baseline = JsonSerializer.Serialize(storage);
        var context = CreateContext(settings: storage);
        var candidate = SettingsStore.Load(context, BeforeCutoff, out var lease);
        AssertEqual(baseline, JsonSerializer.Serialize(candidate), "Initialization clone includes every setting");
        candidate.AudioFormat = "pcm";
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

        var beforeUpdate = JsonSerializer.Serialize(candidate);
        ((ContextProxy)context).OnSave = () => throw new InvalidOperationException("Expected update failure");
        try
        {
            SettingsStore.TryUpdateAndSave(context, candidate, lease, updated =>
            {
                updated.AudioFormat = "mp3";
                updated.ApiKey = "replacement";
                return true;
            });
            throw new InvalidOperationException("Expected update save to fail");
        }
        catch (InvalidOperationException error) when (error.Message == "Expected update failure") { }
        AssertEqual(beforeUpdate, JsonSerializer.Serialize(candidate), "Failed update preserves runtime snapshot");
        AssertEqual(beforeUpdate, JsonSerializer.Serialize(storage), "Failed update preserves stored snapshot");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/model-order-defaults-and-saved-choices-stay-independent")]
    [NUnit.Framework.Order(14)]
    public static void ModelOrderDefaultsAndSavedChoicesStayIndependent()
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
        AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness("drama-3"), "Drama normalization stays unavailable until its effect is officially confirmed");
        AssertEqual(false, FishAudioModelPolicy.SupportsNormalizeLoudness("s1"), "S1 remains unsupported");
        AssertEqual(true, FishAudioModelPolicy.SupportsNormalizeLoudness("s2-pro"), "Existing S2 capability remains supported");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/view-model-format-and-model-toggles-preserve-settings")]
    [NUnit.Framework.Order(15)]
    public static void ViewModelFormatAndModelTogglesPreserveSettings()
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
        vm.SelectedModel = "drama-3";
        AssertEqual(false, vm.IsNormalizeLoudnessEnabled, "Drama disables loudness switch");
        AssertEqual(true, vm.NormalizeLoudness, "Unsupported model retains stored loudness preference");
        vm.ApplyAvailableModels(FishAudioModelPolicy.FreeModelCutoffUtc);
        AssertEqual("drama-3", vm.SelectedModel, "Refreshing ranked choices preserves Drama selection");
        AssertEqual(true, vm.IsApiKeyInputEnabled, "Format changes preserve API key lock behavior");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "settings.streaming/streaming-settings-ui-resources-are-complete")]
    [NUnit.Framework.Order(16)]
    public static void StreamingSettingsUiResourcesAreComplete()
    {
        var view = File.ReadAllText(FindRepoFile("STranslate.Plugin.Tts.FishAudio/Presentation/View/SettingsView.xaml"));
        AssertEqual(true, view.Contains("SelectedItem=\"{Binding AudioFormat}\"", StringComparison.Ordinal), "Format selector binds persisted value");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var locale in UiLocales.All)
        {
            var document = XDocument.Load(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"));
            foreach (var suffix in new[] { "Format_Description", "Streaming_Failed" })
                AssertEqual(true, document.Root!.Elements().Any(e => (string?)e.Attribute(x + "Key") == "STranslate_Plugin_Tts_FishAudio_" + suffix && !string.IsNullOrWhiteSpace(e.Value)), $"{locale} translates {suffix}");
        }
    }

    private static Settings Read(string json) => JsonSerializer.Deserialize<Settings>(json)!;

    private static Settings CreatePopulatedSettings() => new()
    {
        ApiKey = AppliedKey, VoiceId = "0123456789abcdef0123456789abcdef", SelectedModel = "s2-pro",
        AudioFormat = "mp3",
        Mp3Bitrate = 64, Speed = 1.25, Volume = -1.3, NormalizeLoudness = true,
        Temperature = 0.4, TopP = 0.8, Latency = "low", Normalize = true, ConditionOnPreviousChunks = false,
        IsS21ProFreePromoDismissed = true,
        CachedVoice = new CachedVoiceInfo { Title = "Saved voice", Description = "Description", CoverImage = "", AuthorName = "Author", TaskCount = 42, SampleAudioUrl = null },
    };
}
