using System.Globalization;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Converter;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static TestAssertions;

[NUnit.Framework.TestFixture]
[NUnit.Framework.NonParallelizable]
internal static class VoiceCreationDateSuite
{
    internal static readonly DateTimeOffset Date = DateTimeOffset.Parse("2026-09-18T01:02:03.1234567+08:00", CultureInfo.InvariantCulture);

    internal static Task RunAllAsync()
    {
        ProviderDateIsOptionalAndPropertyScoped();
        SettingsMigrationCopiesLeasesAndRollback();
        LocalPresentationPreservesFullInstant();
        return Task.CompletedTask;
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "cards.creation-date/provider-date-is-optional-and-property-scoped")]
    [NUnit.Framework.Property("OriginalOrdinal", 525)]
    public static void ProviderDateIsOptionalAndPropertyScopedCase() =>
        ProviderDateIsOptionalAndPropertyScoped();

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "cards.creation-date/settings-migration-copies-leases-and-rollback")]
    [NUnit.Framework.Property("OriginalOrdinal", 525)]
    public static void SettingsMigrationCopiesLeasesAndRollbackCase() =>
        SettingsMigrationCopiesLeasesAndRollback();

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "cards.creation-date/local-presentation-preserves-full-instant")]
    [NUnit.Framework.Property("OriginalOrdinal", 525)]
    public static void LocalPresentationPreservesFullInstantCase() =>
        LocalPresentationPreservesFullInstant();

    private static void ProviderDateIsOptionalAndPropertyScoped()
    {
        var model = JsonSerializer.Deserialize<ModelEntity>("""{"title":"Voice","created_at":"2026-09-18T01:02:03.1234567+08:00"}""")!;
        using var mapped = JsonDocument.Parse(JsonSerializer.Serialize(model));
        AssertEqual(true, mapped.RootElement.TryGetProperty("created_at", out var date), "Provider creation date must map into voice metadata");
        AssertEqual(Date.ToUniversalTime(), date.GetDateTimeOffset(), "Creation date normalizes offsets without losing precision");
        AssertEqual(TimeSpan.Zero, model.CreatedAt!.Value.Offset, "DTO retains UTC, independent of the provider offset");
        foreach (var value in new[] { "null", "42", "false", "[]", "[1,{\"bad\":[2,3]}]", "{\"bad\":[1,{}]}", "\"invalid\"", "\"2026-09-18T01:02:03\"", "\"2026-99-99T01:02:03Z\"" })
        {
            var parsed = JsonSerializer.Deserialize<ModelEntity>("{\"created_at\":" + value + ",\"title\":\"After\",\"task_count\":123}")!;
            AssertEqual<DateTimeOffset?>(null, parsed.CreatedAt, "Malformed date metadata safely becomes null");
            AssertEqual("After", parsed.Title, "Tolerant parsing consumes the whole invalid date value");
            AssertEqual(123, parsed.TaskCount, "Fields after malformed nested date values remain available");
        }
        AssertEqual<DateTimeOffset?>(null, JsonSerializer.Deserialize<ModelEntity>("{}")!.CreatedAt, "Absent date is authoritative null");
        var truncated = false;
        try { JsonSerializer.Deserialize<ModelEntity>("{\"created_at\":[1,{\"nested\":"); }
        catch (JsonException) { truncated = true; }
        AssertEqual(true, truncated, "Date tolerance cannot hide truncated JSON");
        var unrelated = false;
        try { JsonSerializer.Deserialize<ModelEntity>("{\"created_at\":null,\"task_count\":{}}"); }
        catch (JsonException) { unrelated = true; }
        AssertEqual(true, unrelated, "Tolerant conversion applies only to the date property");
    }

    private static void SettingsMigrationCopiesLeasesAndRollback()
    {
        var cached = new CachedVoiceInfo { Title = "Voice", CreatedAt = Date };
        var direct = new VoiceMetadata(AppliedKey, "Voice", "", "", "", 0, null, [], Date);
        var metadata = VoiceMetadata.FromModel(new ModelEntity { Id = AppliedKey, Title = "Voice", CreatedAt = Date });
        var settings = new Settings { VoiceId = AppliedKey, CachedVoice = cached };
        var snapshot = SettingsStore.CaptureSnapshot(settings);
        cached.CreatedAt = null;
        AssertEqual(Date.ToUniversalTime(), snapshot.CachedVoice!.CreatedAt, "Settings snapshot retains independent date metadata");
        AssertEqual(TimeSpan.Zero, direct.CreatedAt!.Value.Offset, "Programmatic cache constructor normalizes UTC");
        metadata.ToCachedVoice().CreatedAt = null;
        AssertEqual(Date.ToUniversalTime(), metadata.ToCachedVoice().CreatedAt, "Mutating a cache output cannot mutate the retained date");
        AssertEqual(TimeSpan.Zero, new VoiceSearchItem { CreatedAt = Date }.CreatedAt!.Value.Offset, "Programmatic card date normalizes UTC");
        var emptyTitleSettings = new Settings { VoiceId = AppliedKey, CachedVoice = new() { Title = "", CreatedAt = Date } };
        using (var emptyTitle = new SettingsViewModel(CreateContext(settings: emptyTitleSettings), emptyTitleSettings))
            AssertEqual<DateTimeOffset?>(null, emptyTitle.CachedVoiceCreatedAt, "Selected no-title placeholder clears stale date metadata");
        foreach (var field in new[] { "", ",\"CreatedAt\":null", ",\"CreatedAt\":{}", ",\"CreatedAt\":[1,{}]", ",\"CreatedAt\":42", ",\"CreatedAt\":\"bad\"", ",\"CreatedAt\":null,\"CreatedAt\":\"2026-01-01T00:00:00Z\"" })
        {
            var parsed = JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":2,\"CachedVoice\":{\"Title\":\"Old\"" + field + "}}")!;
            AssertEqual<DateTimeOffset?>(null, parsed.CachedVoice!.CreatedAt, "Old schema-2, null, wrong-type and duplicate dates clear independently");
            AssertEqual("Old", parsed.CachedVoice.Title, "Bad dates preserve the rest of selected metadata");
            if (field.Length != 0 && field != ",\"CreatedAt\":null") AssertEqual(true, parsed.NeedsCanonicalSave, "Malformed or duplicate dates request canonical cleanup");
            AssertEqual(2, JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(parsed))!.SchemaVersion, "No new settings schema is introduced");
        }
        var parsedDate = JsonSerializer.Deserialize<Settings>("""{"SchemaVersion":2,"CachedVoice":{"Title":"Voice","CreatedAt":"2026-09-18T01:02:03.1234567+08:00"}}""")!;
        AssertEqual(Date.ToUniversalTime(), parsedDate.CachedVoice!.CreatedAt, "Manual settings migration maps the date");
        AssertEqual(TimeSpan.Zero, parsedDate.CachedVoice.CreatedAt!.Value.Offset, "Migrated date retains UTC");
        AssertEqual(Date.ToUniversalTime(), JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(parsedDate))!.CachedVoice!.CreatedAt, "Date survives a schema-2 round trip");
        foreach (var schema in new[] { "99", "2,\"SchemaVersion\":2" })
        {
            var readOnly = JsonSerializer.Deserialize<Settings>("{\"SchemaVersion\":" + schema + ",\"CachedVoice\":{\"Title\":\"Voice\",\"CreatedAt\":\"2026-09-18T01:02:03.1234567+08:00\"}}")!;
            var readOnlyContext = CreateContext(settings: readOnly);
            SettingsStore.Save(readOnlyContext, readOnly);
            AssertEqual(true, readOnly.IsReadOnly, "Future and duplicate schemas remain read-only with dates");
            AssertEqual(0, ((ContextProxy)readOnlyContext).SaveCount, "Read-only date snapshots cannot be saved");
            AssertEqual(Date.ToUniversalTime(), readOnly.CachedVoice!.CreatedAt, "Compatible dates remain usable in read-only settings");
        }
        settings.CachedVoice = metadata.ToCachedVoice();
        var context = CreateContext(settings: settings);
        var runtime = SettingsStore.Load(context, FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1), out var lease);
        AssertEqual(Date.ToUniversalTime(), runtime.CachedVoice!.CreatedAt, "Leased clone preserves date");
        runtime.CachedVoice.CreatedAt = Date.AddDays(1);
        ((ContextProxy)context).OnSave = () => throw new InvalidOperationException("Expected date save failure");
        try { SettingsStore.Save(context, runtime, lease); throw new InvalidOperationException("Date save must fail"); }
        catch (InvalidOperationException error) when (error.Message == "Expected date save failure") { }
        AssertEqual(Date.ToUniversalTime(), settings.CachedVoice!.CreatedAt, "Failed leased save restores stored date");
        ((ContextProxy)context).OnSave = null;
        SettingsStore.Save(context, runtime, lease);
        AssertEqual(Date.AddDays(1).ToUniversalTime(), settings.CachedVoice.CreatedAt, "Successful leased save copies date");
        ((ContextProxy)context).OnSave = () => throw new InvalidOperationException("Expected date update failure");
        try { SettingsStore.TryUpdateAndSave(context, runtime, lease, candidate => { candidate.CachedVoice!.CreatedAt = null; return true; }); throw new InvalidOperationException("Date update must fail"); }
        catch (InvalidOperationException error) when (error.Message == "Expected date update failure") { }
        AssertEqual(Date.AddDays(1).ToUniversalTime(), runtime.CachedVoice.CreatedAt, "Failed conditional update preserves runtime date");
        AssertEqual(runtime.CachedVoice.CreatedAt, settings.CachedVoice.CreatedAt, "Failed conditional update preserves stored date");
    }

    private static void LocalPresentationPreservesFullInstant()
    {
        var instant = DateTimeOffset.Parse("2026-01-01T00:30:01.1234567Z", CultureInfo.InvariantCulture);
        var west = TimeZoneInfo.CreateCustomTimeZone("TestWest", TimeSpan.FromHours(-8), "West", "West");
        var east = TimeZoneInfo.CreateCustomTimeZone("TestEast", TimeSpan.FromHours(14), "East", "East");
        AssertEqual("2025-12-31", VoiceCreationDateConverter.Format(instant, west), "Display uses the converted local calendar day");
        AssertEqual("2026-01-01", VoiceCreationDateConverter.Format(instant, east), "Positive local offset uses the corresponding day");
        AssertEqual("2025-12-31T16:30:01.1234567-08:00", VoiceCreationDateConverter.Format(instant, west, true), "Tooltip retains seconds, fractional precision and explicit local offset");
        AssertEqual("—", VoiceCreationDateConverter.Format(null, west), "Missing dates show an em dash");
        var converter = new VoiceCreationDateConverter();
        AssertEqual(VoiceCreationDateConverter.Format(instant, TimeZoneInfo.Local), (string)converter.Convert(instant, typeof(string), null!, CultureInfo.GetCultureInfo("ar-SA")), "Date display is invariant Gregorian in every UI culture");
    }
}
