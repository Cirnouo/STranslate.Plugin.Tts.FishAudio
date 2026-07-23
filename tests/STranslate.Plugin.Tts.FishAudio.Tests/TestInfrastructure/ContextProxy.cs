using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

public class ContextProxy : DispatchProxy
{
    public object? MetaData { get; set; }
    public ISnackbar Snackbar { get; set; } = new TestSnackbar();
    public Settings Settings { get; set; } = new();
    public IHttpService? HttpService { get; set; }
    public IAudioPlayer? AudioPlayer { get; set; }
    public ILogger Logger { get; set; } = new TestLogger();
    public int SaveCount { get; private set; }
    public int LoadCount { get; private set; }
    public Action? OnSave { get; set; }
    public Action? OnLoad { get; set; }
    public Func<Settings>? LoadSettings { get; set; }
    public Action<Settings>? SaveSettings { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == "get_Snackbar")
            return Snackbar;

        if (targetMethod.Name == "get_MetaData")
            return MetaData;

        if (targetMethod.Name == "get_HttpService")
            return HttpService;

        if (targetMethod.Name == "get_AudioPlayer")
            return AudioPlayer;

        if (targetMethod.Name == "get_Logger")
            return Logger;

        if (targetMethod.Name == nameof(IPluginContext.GetTranslation))
            return args?[0]?.ToString() ?? "";

        if (targetMethod.Name == nameof(IPluginContext.LoadSettingStorage)
            && targetMethod.IsGenericMethod
            && targetMethod.GetGenericArguments()[0] == typeof(Settings))
        {
            LoadCount++;
            OnLoad?.Invoke();
            if (LoadSettings is not null)
                Settings = LoadSettings();
            return Settings;
        }

        if (targetMethod.Name == nameof(IPluginContext.SaveSettingStorage))
        {
            SaveCount++;
            OnSave?.Invoke();
            SaveSettings?.Invoke(Settings);
            return null;
        }

        return GetDefault(targetMethod.ReturnType);
    }

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        if (type == typeof(Task))
            return Task.CompletedTask;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = type.GetGenericArguments()[0];
            var result = GetDefault(resultType);
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { result });
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    internal static IPluginContext CreateContext(
        ISnackbar? snackbar = null,
        Settings? settings = null,
        IHttpService? httpService = null,
        IAudioPlayer? audioPlayer = null,
        ILogger? logger = null,
        string? pluginCacheDirectoryPath = null)
    {
        var context = DispatchProxy.Create<IPluginContext, ContextProxy>();
        var proxy = (ContextProxy)(object)context;
        proxy.Snackbar = snackbar ?? new TestSnackbar();
        proxy.Settings = settings ?? new Settings();
        proxy.HttpService = httpService;
        proxy.AudioPlayer = audioPlayer;
        proxy.Logger = logger ?? new TestLogger();
        if (pluginCacheDirectoryPath is not null)
            proxy.MetaData = CreatePluginMetaData(pluginCacheDirectoryPath);
        return context;
    }

    internal static object CreatePluginMetaData(string pluginCacheDirectoryPath)
    {
        var metadataType = typeof(IPluginContext).GetProperty("MetaData")!.PropertyType;
        var metadata = Activator.CreateInstance(metadataType)!;
        metadataType.GetProperty("PluginCacheDirectoryPath")!.SetValue(metadata, pluginCacheDirectoryPath);
        return metadata;
    }
}

internal sealed class SharedSettingsBackingStore
{
    private readonly object _gate = new();
    private Settings _settings;

    public SharedSettingsBackingStore(Settings settings)
    {
        _settings = Clone(settings);
    }

    public Settings Load()
    {
        lock (_gate)
            return Clone(_settings);
    }

    public void Replace(Settings settings)
    {
        lock (_gate)
            _settings = Clone(settings);
    }

    public void Save(Settings settings)
    {
        lock (_gate)
            _settings = Clone(settings);
    }

    private static Settings Clone(Settings settings) => new()
    {
        ApiKey = settings.ApiKey,
        VoiceId = settings.VoiceId,
        SelectedModel = settings.SelectedModel,
        Speed = settings.Speed,
        Volume = settings.Volume,
        NormalizeLoudness = settings.NormalizeLoudness,
        Temperature = settings.Temperature,
        TopP = settings.TopP,
        Latency = settings.Latency,
        Normalize = settings.Normalize,
        Mp3Bitrate = settings.Mp3Bitrate,
        ConditionOnPreviousChunks = settings.ConditionOnPreviousChunks,
        CachedVoice = settings.CachedVoice is null
            ? null
            : new CachedVoiceInfo
            {
                Title = settings.CachedVoice.Title,
                Description = settings.CachedVoice.Description,
                CoverImage = settings.CachedVoice.CoverImage,
                AuthorName = settings.CachedVoice.AuthorName,
                TaskCount = settings.CachedVoice.TaskCount,
                SampleAudioUrl = settings.CachedVoice.SampleAudioUrl,
            },
        IsS21ProFreePromoDismissed = settings.IsS21ProFreePromoDismissed,
    };
}
