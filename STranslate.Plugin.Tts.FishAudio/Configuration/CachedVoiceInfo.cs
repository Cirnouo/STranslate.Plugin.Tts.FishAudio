namespace STranslate.Plugin.Tts.FishAudio.Configuration;

public sealed class CachedVoiceInfo
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverImage { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
}
