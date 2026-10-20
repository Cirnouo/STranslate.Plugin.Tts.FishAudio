using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using static ContextProxy;
using static TestAssertions;

internal static class FavoriteStorageCacheSuite
{
    internal static async Task SamePathWritersKeepIdsAndCorruptFilesRemainUntouchedAsync()
    {
        var directory = CreateTempDirectory();
        using var first = new FavoriteVoiceStore(directory);
        using var second = new FavoriteVoiceStore(directory);
        await Task.WhenAll(Task.Run(() => first.SetFavorite(Id(1), true)), Task.Run(() => second.SetFavorite(Id(2), true)));
        AssertEqual(2, first.Read().Count, "Same-path additions must not lose either writer's intent");
        first.SetFavorite(Id(1), false);
        second.SetFavorite(Id(3), true);
        AssertEnumerableEqual(new[] { Id(2), Id(3) }, first.Read(), "Explicit mutations must re-read the latest ordered IDs");
        File.WriteAllText(FilePath(directory), JsonSerializer.Serialize(new[] { Id(1) + "\n", Id(1), Id(1), Id(2) }));
        AssertEnumerableEqual(new[] { Id(1), Id(2) }, first.Read(), "Canonical IDs deduplicate while final line feeds remain invalid");
        AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(Id(1) + "\n"), "Absolute ID validation must reject a final line feed");
        foreach (var corrupt in new[] { "{broken", "{}", "[17]", new string(' ', FavoriteVoiceStore.MaximumFileBytes + 1), JsonSerializer.Serialize(Enumerable.Range(1, 301).Select(Id)) })
        {
            File.WriteAllText(FilePath(directory), corrupt);
            var before = File.ReadAllBytes(FilePath(directory));
            var rejected = false;
            try { first.SetFavorite(Id(4), true); }
            catch { rejected = true; }
            AssertEqual(true, rejected, "Malformed/oversized/over-cap storage must reject mutation");
            AssertEnumerableEqual(before, File.ReadAllBytes(FilePath(directory)), "Read failure must preserve the original file bytes");
        }
    }

    internal static async Task LeaseRetirementLinearizesIdWritesWithoutBlockingSnapshotsAsync()
    {
        var directory = CreateTempDirectory();
        var context = CreateContext();
        var runtime = SettingsStore.Load(context, DateTimeOffset.UtcNow, out var lease);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var retireStarted = new ManualResetEventSlim();
        using var store = new FavoriteVoiceStore(directory, (temporary, destination) =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Test release missing");
            File.Move(temporary, destination, true);
        });
        var writer = Task.Run(() => SettingsStore.TryExecuteWithWriteLease(runtime, lease, () => store.SetFavorite(Id(1), true)));
        AssertEqual(true, started.Wait(TimeSpan.FromSeconds(2)), "The authorized file transaction should reach its commit boundary");
        try
        {
            var snapshot = await Task.Run(() => SettingsStore.CaptureSnapshot(runtime)).WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(runtime.ApiKey, snapshot.ApiKey, "A slow favorite file write must not acquire the short settings snapshot lock");
            var retirement = Task.Run(() => { retireStarted.Set(); SettingsStore.Retire(runtime, lease); });
            AssertEqual(true, retireStarted.Wait(TimeSpan.FromSeconds(2)), "Retirement should be attempting the persistence gate");
            AssertEqual(false, retirement.IsCompleted, "Retirement must wait for an already-authorized atomic commit");
            release.Set();
            AssertEqual(true, await writer.WaitAsync(TimeSpan.FromSeconds(2)), "An already-authorized write should complete before retirement");
            await retirement.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(false, SettingsStore.TryExecuteWithWriteLease(runtime, lease, () => store.SetFavorite(Id(2), true)), "Retirement that wins first must reject the next file write");
            AssertEnumerableEqual(new[] { Id(1) }, store.Read(), "Retired generation must not add another ID");
        }
        finally { release.Set(); }
    }

    internal static void MetadataCacheIsBoundedDetachedAndUsesAnIndependentClientTtl()
    {
        var clock = new MetadataTestClock();
        var cache = new VoiceMetadataCache(clock);
        var input = new CachedVoiceInfo { Title = "Detached", CoverImage = "remote-source", SampleAudioUrl = "https://platform.r2.fish.audio/sample.mp3?private=fixture" };
        cache.Put(VoiceMetadata.FromCachedVoice(Id(1), input));
        input.Title = "Later mutation";
        AssertEqual(true, cache.TryGet(Id(1), out var detached), "A fresh metadata entry is available");
        AssertEqual("Detached", detached.Title, "Cache entries must be detached from mutable selected/card metadata");
        AssertEqual("remote-source", detached.CoverImage, "Cache must retain the canonical remote cover source");
        AssertEqual(false, detached.ToString().Contains("private=fixture", StringComparison.Ordinal), "Metadata value printing must not expose signed-resource details");
        for (var number = 2; number <= VoiceMetadataCache.MaximumCount; number++) cache.Put(Metadata(number));
        cache.TryGet(Id(1), out _);
        cache.Put(Metadata(301));
        AssertEqual(true, cache.TryGet(Id(1), out _), "Recently used entries survive LRU eviction");
        AssertEqual(false, cache.TryGet(Id(2), out _), "LRU capacity should evict the least-recently-used entry");
        clock.Advance(VoiceMetadataCache.Lifetime);
        AssertEqual(false, cache.TryGet(Id(1), out _), "The explicit fifteen-minute client TTL must expire metadata independently of sample URL shape");
    }

    private static string Id(int number) => number.ToString("x32");
    private static string FilePath(string directory) => Path.Combine(directory, FavoriteVoiceStore.FileName);
    private static VoiceMetadata Metadata(int number) => VoiceMetadata.FromCachedVoice(Id(number), new() { Title = $"Favorite {number}" });
    private sealed class MetadataTestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}