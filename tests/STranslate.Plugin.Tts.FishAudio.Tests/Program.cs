using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Service;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Reflection;

const string AppliedKey = "0123456789abcdef0123456789abcdef";
const string DraftKey = "abcdef0123456789abcdef0123456789";

StartupValidationShowsAppliedStatus();
EditingDraftApiKeyDoesNotClearAppliedStatus();
CoverImageCacheUsesExistingFile();
await CoverImageCacheCreatesMissedFileAsync();
CoverImageCacheClearsOnlyCoverImagesAndFormatsSize();

Console.WriteLine("SettingsViewModel and cover image cache tests passed.");

static void StartupValidationShowsAppliedStatus()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var pendingCredit = Task.FromResult<(WalletCreditResponse?, long)>((new WalletCreditResponse { Credit = "12.34" }, 42L));
    var viewModel = new SettingsViewModel(CreateContext(), settings, pendingCredit);

    AssertEqual(true, viewModel.IsApiKeyValid, "Startup credit validation should mark the applied key valid");
    AssertEqual(ApiKeyStatusKind.Success, viewModel.ApiKeyStatusKind, "Startup credit validation should show applied status");
    AssertEqual("12.34", viewModel.UserCredit, "Startup credit validation should populate account credit");
}

static void EditingDraftApiKeyDoesNotClearAppliedStatus()
{
    var settings = new Settings { ApiKey = AppliedKey };
    var viewModel = new SettingsViewModel(CreateContext(), settings, null)
    {
        IsApiKeyValid = true,
        ApiKeyStatusKind = ApiKeyStatusKind.Success,
        UserCredit = "12.34",
    };

    viewModel.ApiKey = DraftKey;

    AssertEqual(true, viewModel.IsApiKeyValid, "Editing the draft key should not clear the applied valid key state");
    AssertEqual(ApiKeyStatusKind.Success, viewModel.ApiKeyStatusKind, "Editing the draft key should not clear the applied status");
    AssertEqual("12.34", viewModel.UserCredit, "Editing the draft key should not clear applied account credit");
    AssertEqual(AppliedKey, settings.ApiKey, "Editing the draft key should not persist until confirm");
}

void CoverImageCacheUsesExistingFile()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "11111111111111111111111111111111";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);
        var cachedFile = Path.Combine(cacheDir, $"{voiceId}.jpg");
        File.WriteAllBytes(cachedFile, new byte[] { 1, 2, 3 });

        var downloadCalled = false;
        var cache = new CoverImageCacheService(root, (_, _) =>
        {
            downloadCalled = true;
            return Task.FromResult(new byte[] { 9 });
        });

        var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/existing", 128);

        AssertEqual(new Uri(cachedFile).AbsoluteUri, result.DisplayUrl, "Existing cover image cache should return local file URI");
        AssertEqual(null, result.CacheTask, "Existing cover image cache should not create a download task");
        AssertEqual(false, downloadCalled, "Existing cover image cache should not download again");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

async Task CoverImageCacheCreatesMissedFileAsync()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "22222222222222222222222222222222";
        const string coverImage = "coverimage/missed";
        var bytes = new byte[] { 10, 20, 30, 40 };
        var downloadCount = 0;
        string? callbackUrl = null;
        var cache = new CoverImageCacheService(root, (_, _) =>
        {
            downloadCount++;
            return Task.FromResult(bytes);
        });

        var result = cache.ResolveCoverImageUrl(voiceId, coverImage, 128, url => callbackUrl = url);
        var expectedRemoteUrl = "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/missed";

        AssertEqual(expectedRemoteUrl, result.DisplayUrl, "Missing cover image cache should return remote URL immediately");
        AssertNotNull(result.CacheTask, "Missing cover image cache should create a download task");

        await result.CacheTask!;

        var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
        AssertEqual(true, File.Exists(cachedFile), "Missing cover image cache should create <id>.jpg");
        AssertEqual(bytes.Length, new FileInfo(cachedFile).Length, "Cached cover image file should contain downloaded bytes");
        AssertEqual(1, downloadCount, "Missing cover image cache should download once");
        AssertEqual(new Uri(cachedFile).AbsoluteUri, callbackUrl, "Missing cover image cache should notify with local file URI");
        AssertEqual($"{bytes.Length} B", cache.GetFormattedCacheSize(), "Cache size should include newly downloaded cover image");

        var secondResult = cache.ResolveCoverImageUrl(voiceId, coverImage, 128);
        AssertEqual(new Uri(cachedFile).AbsoluteUri, secondResult.DisplayUrl, "Created cover image cache should be reused");
        AssertEqual(null, secondResult.CacheTask, "Created cover image cache should not create another download task");
        AssertEqual(1, downloadCount, "Created cover image cache should not download again");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

void CoverImageCacheClearsOnlyCoverImagesAndFormatsSize()
{
    var root = CreateTempDirectory();
    try
    {
        const string voiceId = "33333333333333333333333333333333";
        var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(Path.Combine(cacheDir, $"{voiceId}.jpg"), new byte[] { 1, 2, 3 });
        var unrelated = Path.Combine(root, "keep.txt");
        File.WriteAllText(unrelated, "keep");

        var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult(new byte[] { 9 }));

        AssertEqual("3 B", cache.GetFormattedCacheSize(), "Cache size should count cover image files");
        cache.Clear();

        AssertEqual(false, File.Exists(Path.Combine(cacheDir, $"{voiceId}.jpg")), "Clear should remove cover image cache files");
        AssertEqual(true, File.Exists(unrelated), "Clear should not remove files outside cover_images");
        AssertEqual("0 B", cache.GetFormattedCacheSize(), "Cache size should refresh after clear");
        AssertEqual("1 KB", CoverImageCacheService.FormatBytes(1024), "Size formatter should use KB");
        AssertEqual("1.5 KB", CoverImageCacheService.FormatBytes(1536), "Size formatter should keep one decimal when needed");
        AssertEqual("1 MB", CoverImageCacheService.FormatBytes(1024 * 1024), "Size formatter should use MB");
        AssertEqual("1 TB", CoverImageCacheService.FormatBytes(1024L * 1024 * 1024 * 1024), "Size formatter should use TB");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static IPluginContext CreateContext()
{
    return DispatchProxy.Create<IPluginContext, ContextProxy>();
}

static string CreateTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "FishAudioTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected: {expected}; Actual: {actual}");
}

static void AssertNotNull(object? value, string message)
{
    if (value is null)
        throw new InvalidOperationException(message);
}

public class ContextProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == nameof(IPluginContext.GetTranslation))
            return args?[0]?.ToString() ?? "";

        return GetDefault(targetMethod.ReturnType);
    }

    private static object? GetDefault(Type type)
    {
        if (type == typeof(void))
            return null;

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
