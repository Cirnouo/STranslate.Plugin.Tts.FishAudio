using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;

namespace STranslate.Plugin.Tts.FishAudio.Caching;

internal sealed record VoiceMetadata(string VoiceId, string Title, string Description, string CoverImage,
    string AuthorName, int TaskCount, string? SampleAudioUrl)
{
    public override string ToString() => $"VoiceMetadata({VoiceId})";
    internal static VoiceMetadata FromModel(ModelEntity model, string? voiceId = null) => FromCachedVoice(
        voiceId ?? model.Id,
        new CachedVoiceInfo { Title = model.Title, Description = model.Description, CoverImage = model.CoverImage,
            AuthorName = model.Author?.Nickname ?? "", TaskCount = model.TaskCount, SampleAudioUrl = model.Samples.FirstOrDefault()?.Audio });

    internal static VoiceMetadata FromCachedVoice(string id, CachedVoiceInfo voice) => new(id,
        Bound(voice.Title, 512), Bound(voice.Description, 8192), Bound(voice.CoverImage, 4096),
        Bound(voice.AuthorName, 256), Math.Max(0, voice.TaskCount), voice.SampleAudioUrl is null ? null : Bound(voice.SampleAudioUrl, 8192));

    internal CachedVoiceInfo ToCachedVoice() => new() { Title = Title, Description = Description,
        CoverImage = CoverImage, AuthorName = AuthorName, TaskCount = TaskCount, SampleAudioUrl = SampleAudioUrl };

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

    internal event Action<VoiceMetadata>? MetadataUpdated;

    internal bool TryGet(string voiceId, out VoiceMetadata metadata)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(voiceId, out var entry))
            {
                if (_timeProvider.GetElapsedTime(entry.Timestamp) < Lifetime)
                {
                    _recency.Remove(entry.Node);
                    _recency.AddLast(entry.Node);
                    metadata = entry.Metadata;
                    return true;
                }
                _entries.Remove(voiceId);
                _recency.Remove(entry.Node);
            }
        }
        metadata = null!;
        return false;
    }

    internal void Put(VoiceMetadata metadata)
    {
        if (!SettingsValidation.IsValidVoiceIdFormat(metadata.VoiceId))
            return;
        lock (_gate)
        {
            if (_entries.Remove(metadata.VoiceId, out var previous))
                _recency.Remove(previous.Node);
            var node = _recency.AddLast(metadata.VoiceId);
            _entries.Add(metadata.VoiceId, new Entry(metadata, _timeProvider.GetTimestamp(), node));
            if (_entries.Count > MaximumCount)
            {
                _entries.Remove(_recency.First!.Value);
                _recency.RemoveFirst();
            }
        }
        // Subscribers marshal presentation work themselves; never notify under the cache gate.
        foreach (var subscriber in MetadataUpdated?.GetInvocationList() ?? [])
        {
            try { ((Action<VoiceMetadata>)subscriber)(metadata); }
            catch { /* A transient view cannot invalidate a successful public metadata cache update. */ }
        }
    }

    private sealed record Entry(VoiceMetadata Metadata, long Timestamp, LinkedListNode<string> Node);
}
