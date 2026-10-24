namespace STranslate.Plugin.Tts.FishAudio.FishAudio;

internal enum VoiceSearchSort { Score, TaskCount, CreatedAt }

// Session request intent only. Credentials have a separately captured lifetime.
internal sealed class VoiceSearchCriteria
{
    internal string Query { get; }
    internal static IReadOnlyList<string> PresetLanguages { get; } = Array.AsReadOnly(new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" });
    internal IReadOnlyList<string> Languages { get; }
    internal VoiceSearchSort Sort { get; }
    internal string SortParameter => Sort switch
    {
        VoiceSearchSort.TaskCount => "task_count",
        VoiceSearchSort.CreatedAt => "created_at",
        _ => "score",
    };

    internal VoiceSearchCriteria(string query, IEnumerable<string>? languages = null, VoiceSearchSort sort = VoiceSearchSort.Score)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages ?? [])
        {
            if (!PresetLanguages.Contains(language, StringComparer.Ordinal))
                throw new ArgumentOutOfRangeException(nameof(languages));
            selected.Add(language);
        }
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        Query = query;
        Languages = Array.AsReadOnly(PresetLanguages.Where(selected.Contains).ToArray());
        Sort = sort;
    }

    internal VoiceSearchCriteria WithQuery(string query) => new(query, Languages, Sort);
    internal static VoiceSearchSort ParseSort(string sort) => sort switch
    {
        "score" => VoiceSearchSort.Score,
        "task_count" => VoiceSearchSort.TaskCount,
        "created_at" => VoiceSearchSort.CreatedAt,
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };
}
