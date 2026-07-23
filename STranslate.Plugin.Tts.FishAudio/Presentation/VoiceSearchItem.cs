using CommunityToolkit.Mvvm.ComponentModel;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class VoiceSearchItem : ObservableObject
{
    public string Id { get; set; } = "";
    [ObservableProperty]
    public partial string Title { get; set; }
    [ObservableProperty]
    public partial string Description { get; set; }
    [ObservableProperty]
    public partial string AuthorName { get; set; }
    [ObservableProperty]
    public partial string CoverUrl { get; set; }
    [ObservableProperty]
    public partial int TaskCount { get; set; }
    [ObservableProperty]
    public partial string? SampleAudioUrl { get; set; }
    [ObservableProperty]
    public partial string CoverImage { get; set; }

    [ObservableProperty]
    public partial bool IsBeingPreviewed { get; set; }

    [ObservableProperty]
    public partial double PreviewProgress { get; set; }
}
