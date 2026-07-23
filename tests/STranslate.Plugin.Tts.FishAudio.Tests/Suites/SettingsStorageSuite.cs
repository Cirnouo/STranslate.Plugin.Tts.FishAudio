using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
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

internal static class SettingsStorageSuite
{
    internal static void NewSettingsSerializeCurrentSchemaFirst()
    {
        var settings = new Settings();
        var json = JsonSerializer.Serialize(settings);

        AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "New settings should use the current schema");
        AssertEqual(
            true,
            json.StartsWith($"{{\"SchemaVersion\":{SettingsSchema.Current},", StringComparison.Ordinal),
            "SchemaVersion should be the first serialized settings property");
    }

    internal static void Version100SettingsMigrate()
    {
        var settings = DeserializeSettings(
            """
            {
              "ApiKey": "v100-key",
              "ReferenceId": "00112233445566778899aabbccddeeff",
              "SelectedModel": "s2-pro",
              "Speed": 1.2,
              "Volume": -1.5,
              "NormalizeLoudness": false,
              "Temperature": 0.4,
              "TopP": 0.8,
              "Latency": "low",
              "Normalize": true,
              "CachedModel": {
                "Id": "deleted-id",
                "Title": "Version 1.0.0 Voice",
                "CoverImage": "cover/v100",
                "AuthorName": "Author 100",
                "AuthorAvatar": "deleted-avatar",
                "SampleAudioUrl": "https://platform.r2.fish.audio/v100.mp3",
                "SampleText": "deleted sample text"
              }
            }
            """);

        AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "v1.0.0 settings should migrate to the current schema");
        AssertEqual("00112233445566778899aabbccddeeff", settings.VoiceId, "v1.0.0 ReferenceId should migrate to VoiceId");
        AssertEqual("Version 1.0.0 Voice", settings.CachedVoice?.Title, "v1.0.0 CachedModel should migrate to CachedVoice");
        AssertEqual("", settings.CachedVoice?.Description, "Missing v1.0.0 cached voice description should use its default");
        AssertEqual(0, settings.CachedVoice?.TaskCount, "Missing v1.0.0 task count should use its default");
        AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Missing v1.0.0 bitrate should use its default");
        AssertEqual(Settings.DefaultConditionOnPreviousChunks, settings.ConditionOnPreviousChunks, "Missing v1.0.0 context setting should use its default");
    }

    internal static void Version101SettingsMigrate()
    {
        var settings = DeserializeSettings(
            """
            {
              "ApiKey": "v101-key",
              "ReferenceId": "11223344556677889900aabbccddeeff",
              "SelectedModel": "s2-pro",
              "Speed": 0.85,
              "Volume": 2.1,
              "NormalizeLoudness": true,
              "Temperature": 0.65,
              "TopP": 0.55,
              "Latency": "balanced",
              "Normalize": false,
              "CachedModel": {
                "Title": "Version 1.0.1 Voice",
                "CoverImage": "cover/v101",
                "AuthorName": "Author 101",
                "TaskCount": 31,
                "SampleAudioUrl": "https://platform.r2.fish.audio/v101.mp3"
              }
            }
            """);

        AssertEqual("11223344556677889900aabbccddeeff", settings.VoiceId, "v1.0.1 ReferenceId should migrate to VoiceId");
        AssertEqual("Version 1.0.1 Voice", settings.CachedVoice?.Title, "v1.0.1 CachedModel should migrate to CachedVoice");
        AssertEqual(31, settings.CachedVoice?.TaskCount, "v1.0.1 cached task count should be preserved");
        AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Missing v1.0.1 bitrate should use its default");
    }

    internal static void Version102UnversionedSettingsMigrate()
    {
        var settings = DeserializeSettings(
            """
            {
              "ApiKey": "v102-key",
              "VoiceId": "22334455667788990011aabbccddeeff",
              "SelectedModel": "s2-pro",
              "Speed": 1.35,
              "Volume": -0.7,
              "NormalizeLoudness": false,
              "Temperature": 0.5,
              "TopP": 0.9,
              "Latency": "normal",
              "Normalize": true,
              "Mp3Bitrate": 128,
              "ConditionOnPreviousChunks": false,
              "CachedVoice": {
                "Title": "Version 1.0.2 Voice",
                "Description": "Current voice metadata",
                "CoverImage": "cover/v102",
                "AuthorName": "Author 102",
                "TaskCount": 52,
                "SampleAudioUrl": "https://platform.r2.fish.audio/v102.mp3"
              }
            }
            """);

        AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "Unversioned v1.0.2 settings should migrate to the current schema");
        AssertEqual("22334455667788990011aabbccddeeff", settings.VoiceId, "Unversioned current VoiceId should be preserved");
        AssertEqual("Current voice metadata", settings.CachedVoice?.Description, "Unversioned current CachedVoice should be preserved");
        AssertEqual(128, settings.Mp3Bitrate, "v1.0.2 bitrate should be preserved");
        AssertEqual(false, settings.ConditionOnPreviousChunks, "v1.0.2 context setting should be preserved");
    }

    internal static void CurrentVoiceFieldsWinRegardlessOfPropertyOrder()
    {
        AssertCurrentWins(
            """
            {
              "ReferenceId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "VoiceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "CachedModel": { "Title": "Legacy" },
              "CachedVoice": { "Title": "Current" }
            }
            """);
        AssertCurrentWins(
            """
            {
              "CachedVoice": { "Title": "Current" },
              "CachedModel": { "Title": "Legacy" },
              "VoiceId": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "ReferenceId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            }
            """);

        static void AssertCurrentWins(string json)
        {
            var settings = DeserializeSettings(json);
            AssertEqual("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", settings.VoiceId, "Valid current VoiceId should win over ReferenceId regardless of order");
            AssertEqual("Current", settings.CachedVoice?.Title, "Valid current CachedVoice should win over CachedModel regardless of order");
        }
    }

    internal static void InvalidCurrentVoiceFieldsFallBackToLegacy()
    {
        var settings = DeserializeSettings(
            """
            {
              "VoiceId": 123,
              "ReferenceId": "cccccccccccccccccccccccccccccccc",
              "CachedVoice": ["wrong"],
              "CachedModel": { "Title": "Legacy Fallback", "TaskCount": 8 }
            }
            """);

        AssertEqual("cccccccccccccccccccccccccccccccc", settings.VoiceId, "Wrong-type VoiceId should fall back to valid ReferenceId");
        AssertEqual("Legacy Fallback", settings.CachedVoice?.Title, "Wrong-type CachedVoice should fall back to valid CachedModel");
        AssertEqual(8, settings.CachedVoice?.TaskCount, "Fallback cached voice metadata should be preserved");
    }

    internal static void WrongJsonTypeDefaultsOnlyThatField()
    {
        var settings = DeserializeSettings(
            """
            {
              "SchemaVersion": 1,
              "ApiKey": { "wrong": true },
              "VoiceId": "malformed but intentionally preserved",
              "SelectedModel": "s1",
              "Speed": 1.25,
              "Volume": -2.0,
              "NormalizeLoudness": false
            }
            """);

        AssertEqual("", settings.ApiKey, "Wrong-type API Key should use only the API Key default");
        AssertEqual("malformed but intentionally preserved", settings.VoiceId, "Malformed string VoiceId should remain unchanged");
        AssertEqual("s1", settings.SelectedModel, "A neighboring valid model should remain unchanged");
        AssertEqual(1.25, settings.Speed, "A neighboring valid speed should remain unchanged");
        AssertEqual(-2.0, settings.Volume, "A neighboring valid volume should remain unchanged");
        AssertEqual(false, settings.NormalizeLoudness, "A neighboring valid boolean should remain unchanged");
    }

    internal static void WrongSchemaTypeDefaultsOnlySchemaField()
    {
        var settings = DeserializeSettings(
            """
            {
              "SchemaVersion": "wrong",
              "ApiKey": "preserved-key-text",
              "VoiceId": "preserved-voice-text",
              "SelectedModel": "s2-pro"
            }
            """);
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;

        SettingsStore.Load(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddTicks(-1));

        AssertEqual(SettingsSchema.Current, settings.SchemaVersion, "Wrong-type SchemaVersion should default through legacy migration to current");
        AssertEqual(false, settings.IsReadOnly, "A single wrong-type SchemaVersion should not make settings read-only");
        AssertEqual("preserved-key-text", settings.ApiKey, "Wrong-type SchemaVersion should not reset API Key");
        AssertEqual("preserved-voice-text", settings.VoiceId, "Wrong-type SchemaVersion should not reset VoiceId");
        AssertEqual("s2-pro", settings.SelectedModel, "Wrong-type SchemaVersion should not reset model");
        AssertEqual(1, proxy.SaveCount, "Wrong-type SchemaVersion should trigger canonical save");
    }

    internal static void UnknownAndDeletedPropertiesAreCleanedUp()
    {
        var settings = DeserializeSettings(
            """
            {
              "ReferenceId": "dddddddddddddddddddddddddddddddd",
              "DeletedTopLevel": "remove me",
              "CachedModel": {
                "Id": "remove me",
                "Title": "Migrated",
                "AuthorAvatar": "remove me",
                "SampleText": "remove me"
              }
            }
            """);
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;

        var loaded = SettingsStore.Load(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddTicks(-1));
        var canonicalJson = JsonSerializer.Serialize(loaded);

        AssertEqual(1, proxy.SaveCount, "Supported settings with legacy or unknown properties should be canonically saved once");
        AssertEqual(false, canonicalJson.Contains("ReferenceId", StringComparison.Ordinal), "Canonical settings should drop ReferenceId");
        AssertEqual(false, canonicalJson.Contains("CachedModel", StringComparison.Ordinal), "Canonical settings should drop CachedModel");
        AssertEqual(false, canonicalJson.Contains("DeletedTopLevel", StringComparison.Ordinal), "Canonical settings should drop unknown top-level properties");
        AssertEqual(false, canonicalJson.Contains("AuthorAvatar", StringComparison.Ordinal), "Canonical settings should drop deleted cached voice properties");
    }

    internal static void NumericSettingsNormalizeRangesAndGranularity()
    {
        var settings = new Settings
        {
            SelectedModel = "removed-model",
            Latency = "turbo",
            Mp3Bitrate = 320,
            Speed = 1.075,
            Volume = -0.05,
            Temperature = 0.724,
            TopP = 0.726,
        };

        AssertEqual(true, SettingsNormalizer.Normalize(settings, FishAudioModelPolicy.FreeModelCutoffUtc), "Invalid or off-step structured settings should normalize");
        AssertEqual(FishAudioModelPolicy.S21ProModel, settings.SelectedModel, "Unavailable model should normalize through runtime policy");
        AssertEqual(Settings.DefaultLatency, settings.Latency, "Invalid latency should normalize to default");
        AssertEqual(Settings.DefaultMp3Bitrate, settings.Mp3Bitrate, "Invalid bitrate should normalize to default");
        AssertEqual(1.1, settings.Speed, "Speed midpoint should snap to 0.05 away from zero");
        AssertEqual(-0.1, settings.Volume, "Negative volume midpoint should snap to 0.1 away from zero");
        AssertEqual(0.7, settings.Temperature, "Temperature should snap to the nearest 0.05");
        AssertEqual(0.75, settings.TopP, "TopP should snap to the nearest 0.05");

        settings.Speed = double.NaN;
        settings.Volume = double.PositiveInfinity;
        settings.Temperature = -0.01;
        settings.TopP = 1.01;
        SettingsNormalizer.Normalize(settings, FishAudioModelPolicy.FreeModelCutoffUtc);

        AssertEqual(Settings.DefaultSpeed, settings.Speed, "Non-finite speed should normalize to default");
        AssertEqual(Settings.DefaultVolume, settings.Volume, "Non-finite volume should normalize to default");
        AssertEqual(Settings.DefaultTemperature, settings.Temperature, "Out-of-range temperature should normalize to default");
        AssertEqual(Settings.DefaultTopP, settings.TopP, "Out-of-range TopP should normalize to default");
    }

    internal static void OrdinaryDuplicateDefaultsOnlyThatField()
    {
        var settings = DeserializeSettings(
            """
            {
              "SchemaVersion": 1,
              "ApiKey": "first",
              "VoiceId": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
              "ApiKey": "second",
              "SelectedModel": "s2-pro"
            }
            """);
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;

        SettingsStore.Load(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddTicks(-1));

        AssertEqual("", settings.ApiKey, "A duplicated ordinary field should use only that field's default");
        AssertEqual("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", settings.VoiceId, "A neighboring non-duplicate field should remain unchanged");
        AssertEqual("s2-pro", settings.SelectedModel, "A neighboring model should remain unchanged");
        AssertEqual(1, proxy.SaveCount, "An ordinary duplicate should trigger one canonical save");
    }

    internal static void DuplicateAndFutureSchemasAreReadOnly()
    {
        var duplicate = DeserializeSettings(
            """
            {
              "SchemaVersion": 1,
              "ApiKey": "duplicate-schema-key",
              "SchemaVersion": 1,
              "VoiceId": "ffffffffffffffffffffffffffffffff",
              "SelectedModel": "s2-pro"
            }
            """);
        var duplicateLogger = new TestLogger();
        var duplicateContext = CreateContext(settings: duplicate, logger: duplicateLogger);
        var duplicateProxy = (ContextProxy)(object)duplicateContext;

        var loadedDuplicate = SettingsStore.Load(duplicateContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddTicks(-1));
        SettingsStore.Save(duplicateContext, loadedDuplicate);

        AssertEqual(true, loadedDuplicate.IsReadOnly, "Duplicate SchemaVersion should make settings read-only");
        AssertEqual("duplicate-schema-key", loadedDuplicate.ApiKey, "Duplicate SchemaVersion should still parse compatible current fields");
        AssertEqual(0, duplicateProxy.SaveCount, "Duplicate SchemaVersion should block all host saves");
        AssertEqual(true, duplicateLogger.Contains("read-only"), "Blocked duplicate-schema saves should be logged");

        var future = DeserializeSettings(
            """
            {
              "SchemaVersion": 99,
              "ApiKey": "future-key",
              "VoiceId": "0123456789abcdef0123456789abcdef",
              "SelectedModel": "s1"
            }
            """);
        var futureLogger = new TestLogger();
        var futureContext = CreateContext(settings: future, logger: futureLogger);
        var futureProxy = (ContextProxy)(object)futureContext;

        var loadedFuture = SettingsStore.Load(futureContext, FishAudioModelPolicy.FreeModelCutoffUtc.AddTicks(-1));
        SettingsStore.Save(futureContext, loadedFuture);

        AssertEqual(99, loadedFuture.SchemaVersion, "Future schema number should be retained in memory");
        AssertEqual(true, loadedFuture.IsReadOnly, "Future schemas should be read-only");
        AssertEqual("future-key", loadedFuture.ApiKey, "Future schemas should still parse compatible current fields");
        AssertEqual("s1", loadedFuture.SelectedModel, "Future schemas should preserve compatible current model fields");
        AssertEqual(0, futureProxy.SaveCount, "Future schemas should block all host saves");
        AssertEqual(true, futureLogger.Contains("read-only"), "Blocked future-schema saves should be logged");
    }

    internal static void CurrentSettingsRoundTrip()
    {
        var original = new Settings
        {
            ApiKey = "malformed key remains text",
            VoiceId = "malformed voice remains text",
            SelectedModel = "s2-pro",
            Speed = 1.35,
            Volume = -2.4,
            NormalizeLoudness = false,
            Temperature = 0.55,
            TopP = 0.85,
            Latency = "balanced",
            Normalize = true,
            Mp3Bitrate = 128,
            ConditionOnPreviousChunks = false,
            CachedVoice = new CachedVoiceInfo
            {
                Title = "Round Trip",
                Description = "Description",
                CoverImage = "cover/round-trip",
                AuthorName = "Author",
                TaskCount = 77,
                SampleAudioUrl = "https://platform.r2.fish.audio/round-trip.mp3",
            },
            IsS21ProFreePromoDismissed = true,
        };

        var roundTripped = DeserializeSettings(JsonSerializer.Serialize(original));

        AssertEqual(SettingsSchema.Current, roundTripped.SchemaVersion, "Current round trip should retain schema version");
        AssertEqual(original.ApiKey, roundTripped.ApiKey, "Current round trip should preserve API Key text");
        AssertEqual(original.VoiceId, roundTripped.VoiceId, "Current round trip should preserve VoiceId text");
        AssertEqual(original.SelectedModel, roundTripped.SelectedModel, "Current round trip should preserve model");
        AssertEqual(original.Speed, roundTripped.Speed, "Current round trip should preserve speed");
        AssertEqual(original.Volume, roundTripped.Volume, "Current round trip should preserve volume");
        AssertEqual(original.NormalizeLoudness, roundTripped.NormalizeLoudness, "Current round trip should preserve loudness normalization");
        AssertEqual(original.Temperature, roundTripped.Temperature, "Current round trip should preserve temperature");
        AssertEqual(original.TopP, roundTripped.TopP, "Current round trip should preserve TopP");
        AssertEqual(original.Latency, roundTripped.Latency, "Current round trip should preserve latency");
        AssertEqual(original.Normalize, roundTripped.Normalize, "Current round trip should preserve text normalization");
        AssertEqual(original.Mp3Bitrate, roundTripped.Mp3Bitrate, "Current round trip should preserve bitrate");
        AssertEqual(original.ConditionOnPreviousChunks, roundTripped.ConditionOnPreviousChunks, "Current round trip should preserve context setting");
        AssertEqual(original.CachedVoice?.Title, roundTripped.CachedVoice?.Title, "Current round trip should preserve cached voice metadata");
        AssertEqual(original.IsS21ProFreePromoDismissed, roundTripped.IsS21ProFreePromoDismissed, "Current round trip should preserve promo dismissal");
    }

    internal static void ProductionUsesSettingsStoreForContextStorage()
    {
        var productionRoot = FindRepoPath("STranslate.Plugin.Tts.FishAudio");
        var offenders = Directory
            .GetFiles(productionRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "SettingsStore.cs", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"\.\s*(?:Load|Save)SettingStorage\s*<\s*Settings\s*>",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(productionRoot, path))
            .ToArray();

        AssertEqual(0, offenders.Length, $"Only SettingsStore may call context settings storage; found: {string.Join(", ", offenders)}");
        AssertEqual(
            false,
            File.Exists(Path.Combine(productionRoot, "Settings.cs")),
            "Settings should live in the focused Configuration directory");
    }

    internal static void ApiKeyEditingPersistsImmediately()
    {
        var settings = new Settings { ApiKey = AppliedKey };
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(context, settings);

        viewModel.ApiKey = DraftKey;

        AssertEqual(DraftKey, settings.ApiKey, "Editing API Key should immediately update settings");
        AssertEqual(1, proxy.SaveCount, "Editing API Key should immediately save settings");
    }

    internal static Settings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize<Settings>(json) ?? throw new InvalidOperationException("Settings JSON returned null");
}
