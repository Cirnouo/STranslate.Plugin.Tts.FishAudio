namespace STranslate.Plugin.Tts.FishAudio;

public class Settings
{
    public string ApiKey { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string SelectedModel { get; set; } = "s2-pro";
    public double Speed { get; set; } = 1.0;
    public double Volume { get; set; }
    public bool NormalizeLoudness { get; set; } = true;
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 0.7;
    public string Latency { get; set; } = "normal";
    public bool Normalize { get; set; }
    public CachedModelInfo? CachedModel { get; set; }
}

public class CachedModelInfo
{
    public string Title { get; set; } = "";
    public string CoverImage { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public int TaskCount { get; set; }
    public string? SampleAudioUrl { get; set; }
}
