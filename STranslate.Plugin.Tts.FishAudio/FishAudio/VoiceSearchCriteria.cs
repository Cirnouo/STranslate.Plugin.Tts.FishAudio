namespace STranslate.Plugin.Tts.FishAudio.FishAudio;

internal enum VoiceSearchSort { Score, TaskCount, CreatedAt }

// Session request intent only. Credentials have a separately captured lifetime.
internal sealed class VoiceSearchCriteria
{
    internal string Query { get; }
    internal string? Language { get; }
    internal VoiceSearchSort Sort { get; }
    internal string SortParameter => Sort switch
    {
        VoiceSearchSort.TaskCount => "task_count",
        VoiceSearchSort.CreatedAt => "created_at",
        _ => "score",
    };

    internal VoiceSearchCriteria(string query, string? language = null, VoiceSearchSort sort = VoiceSearchSort.Score)
    {
        if (language is not (null or "en" or "zh" or "de" or "ja" or "fr" or "es" or "ko" or "ar" or "ru" or "pt"))
            throw new ArgumentOutOfRangeException(nameof(language));
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        Query = query;
        Language = language;
        Sort = sort;
    }

    internal VoiceSearchCriteria WithQuery(string query) => new(query, Language, Sort);
    internal static VoiceSearchSort ParseSort(string sort) => sort switch
    {
        "score" => VoiceSearchSort.Score,
        "task_count" => VoiceSearchSort.TaskCount,
        "created_at" => VoiceSearchSort.CreatedAt,
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };
}
