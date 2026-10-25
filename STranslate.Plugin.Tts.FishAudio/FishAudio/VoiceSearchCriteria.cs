namespace STranslate.Plugin.Tts.FishAudio.FishAudio;

internal enum VoiceSearchSort { Score, TaskCount, CreatedAt }

// Session menu intent only; it has no query, credential or response lifetime.
internal sealed class VoiceSearchOptions
{
    internal static IReadOnlyList<string> PresetLanguages { get; } = Array.AsReadOnly(new[] { "en", "zh", "de", "ja", "fr", "es", "ko", "ar", "ru", "pt" });
    internal IReadOnlyList<string> Languages { get; }
    internal VoiceSearchSort Sort { get; }
    internal string SortParameter => Sort switch
    {
        VoiceSearchSort.TaskCount => "task_count",
        VoiceSearchSort.CreatedAt => "created_at",
        _ => "score",
    };

    internal VoiceSearchOptions(IEnumerable<string>? languages = null, VoiceSearchSort sort = VoiceSearchSort.Score)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languages ?? [])
        {
            if (!PresetLanguages.Contains(language, StringComparer.Ordinal))
                throw new ArgumentOutOfRangeException(nameof(languages));
            selected.Add(language);
        }
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        Languages = Array.AsReadOnly(PresetLanguages.Where(selected.Contains).ToArray());
        Sort = sort;
    }
}

// Successful results and pagination retain their complete captured criteria.
internal sealed class VoiceSearchCriteria
{
    private readonly VoiceSearchOptions _options;
    internal string Query { get; }
    internal static IReadOnlyList<string> PresetLanguages => VoiceSearchOptions.PresetLanguages;
    internal IReadOnlyList<string> Languages => _options.Languages;
    internal VoiceSearchSort Sort => _options.Sort;
    internal string SortParameter => _options.SortParameter;

    internal VoiceSearchCriteria(string query, IEnumerable<string>? languages = null, VoiceSearchSort sort = VoiceSearchSort.Score)
        : this(query, new VoiceSearchOptions(languages, sort)) { }

    internal VoiceSearchCriteria(string query, VoiceSearchOptions options)
    {
        Query = query;
        _options = options;
    }

    internal VoiceSearchCriteria WithQuery(string query) => new(query, _options);
    internal static VoiceSearchSort ParseSort(string sort) => sort switch
    {
        "score" => VoiceSearchSort.Score,
        "task_count" => VoiceSearchSort.TaskCount,
        "created_at" => VoiceSearchSort.CreatedAt,
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };
}
