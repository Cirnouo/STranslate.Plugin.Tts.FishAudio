namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public sealed class CachedVoiceInfo
{
    private IReadOnlyList<string> _languages = Array.Empty<string>();

    public IReadOnlyList<string> Languages
    {
        get => _languages;
        set => _languages = VoiceLanguageMetadata.Normalize(value);
    }

    private DateTimeOffset? _createdAt;

    public DateTimeOffset? CreatedAt
    {
        get => _createdAt;
        set => _createdAt = value?.ToUniversalTime();
    }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverImage { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
}
