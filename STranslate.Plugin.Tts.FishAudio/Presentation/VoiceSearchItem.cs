using CommunityToolkit.Mvvm.ComponentModel;
using STranslate.Plugin.Tts.FishAudio.Configuration;

namespace STranslate.Plugin.Tts.FishAudio.ViewModel;

public partial class VoiceSearchItem : ObservableObject
{
    private IReadOnlyList<string> _languages = Array.Empty<string>();

    public IReadOnlyList<string> Languages
    {
        get => _languages;
        set => SetProperty(ref _languages, VoiceLanguageMetadata.Normalize(value));
    }

    private DateTimeOffset? _createdAt;

    public DateTimeOffset? CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value?.ToUniversalTime());
    }

    public string Id { get; set; } = "";
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSelection))]
    public partial bool IsApplied { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleSelection))]
    public partial bool HasMetadata { get; set; } = true;

    public bool CanToggleSelection => IsApplied || HasMetadata;
    [ObservableProperty]
    public partial bool IsLoadingMetadata { get; set; }
    [ObservableProperty]
    public partial string? MetadataError { get; set; }
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
    [NotifyPropertyChangedFor(nameof(IsPreviewActive))]
    public partial bool IsBeingPreviewed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewActive))]
    public partial bool IsPreparingPreview { get; set; }

    public bool IsPreviewActive => IsPreparingPreview || IsBeingPreviewed;

    [ObservableProperty]
    public partial double PreviewProgress { get; set; }
}
