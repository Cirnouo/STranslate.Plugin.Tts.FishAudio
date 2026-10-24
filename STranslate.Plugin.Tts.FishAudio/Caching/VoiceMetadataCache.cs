using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;

namespace STranslate.Plugin.Tts.FishAudio.Caching;

internal sealed record VoiceMetadata(string VoiceId, string Title, string Description, string CoverImage,
    string AuthorName, int TaskCount, string? SampleAudioUrl, IReadOnlyList<string> Languages, DateTimeOffset? CreatedAt = null)
{
    public DateTimeOffset? CreatedAt { get; } = CreatedAt?.ToUniversalTime();
    public IReadOnlyList<string> Languages { get; } = VoiceLanguageMetadata.Normalize(Languages);

    public override string ToString() => $"VoiceMetadata({VoiceId})";
    internal static VoiceMetadata FromModel(ModelEntity model, string? voiceId = null) => FromCachedVoice(
        voiceId ?? model.Id,
        new CachedVoiceInfo { Title = model.Title, Description = model.Description, CoverImage = model.CoverImage,
            AuthorName = model.Author?.Nickname ?? "", TaskCount = model.TaskCount, Languages = model.Languages, CreatedAt = model.CreatedAt, SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio });

    internal static VoiceMetadata FromCachedVoice(string id, CachedVoiceInfo voice) => new(id,
        Bound(voice.Title, 512), Bound(voice.Description, 8192), Bound(voice.CoverImage, 4096),
        Bound(voice.AuthorName, 256), Math.Max(0, voice.TaskCount), voice.SampleAudioUrl is null ? null : Bound(voice.SampleAudioUrl, 8192),
        voice.Languages, voice.CreatedAt);

    internal CachedVoiceInfo ToCachedVoice() => new() { Title = Title, Description = Description,
        CoverImage = CoverImage, AuthorName = AuthorName, TaskCount = TaskCount, SampleAudioUrl = SampleAudioUrl, Languages = Languages, CreatedAt = CreatedAt };

    private static string Bound(string? value, int maximum) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= maximum ? value : value[..maximum];
}

internal sealed class VoiceMetadataCache(TimeProvider? timeProvider = null)
{
    internal const int MaximumCount = 300;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly LinkedList<string> _recency = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal static string CredentialScope(string? apiKey) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey)));

    internal event Action<string, VoiceMetadata>? MetadataUpdated;

    internal bool TryGet(string voiceId, out VoiceMetadata metadata, string? credentialScope = null)
    {
        var key = (credentialScope ?? CredentialScope(null)) + ":" + voiceId;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                if (_timeProvider.GetElapsedTime(entry.Timestamp) < Lifetime)
                {
                    _recency.Remove(entry.Node);
                    _recency.AddLast(entry.Node);
                    metadata = entry.Metadata;
                    return true;
                }
                _entries.Remove(key);
                _recency.Remove(entry.Node);
            }
        }
        metadata = null!;
        return false;
    }

    internal void Put(VoiceMetadata metadata, string? credentialScope = null)
    {
        if (!SettingsValidation.IsValidVoiceIdFormat(metadata.VoiceId))
            return;
        var scope = credentialScope ?? CredentialScope(null);
        var key = scope + ":" + metadata.VoiceId;
        lock (_gate)
        {
            if (_entries.Remove(key, out var previous))
                _recency.Remove(previous.Node);
            var node = _recency.AddLast(key);
            _entries.Add(key, new Entry(metadata, _timeProvider.GetTimestamp(), node));
            if (_entries.Count > MaximumCount)
            {
                _entries.Remove(_recency.First!.Value);
                _recency.RemoveFirst();
            }
        }
        // Subscribers marshal presentation work themselves; never notify under the cache gate.
        foreach (var subscriber in MetadataUpdated?.GetInvocationList() ?? [])
        {
            try { ((Action<string, VoiceMetadata>)subscriber)(scope, metadata); }
            catch { /* A transient view cannot invalidate a successful metadata cache update. */ }
        }
    }

    private sealed record Entry(VoiceMetadata Metadata, long Timestamp, LinkedListNode<string> Node);
}
