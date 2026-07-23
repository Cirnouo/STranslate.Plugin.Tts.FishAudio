using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Caching;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

internal static class CoverImageCacheSuite
{
    internal static void CoverImageCacheUsesExistingFile()
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
                return Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 }));
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

    internal static async Task CoverImageCacheCreatesMissedFileAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            const string voiceId = "22222222222222222222222222222222";
            const string coverImage = "coverimage/missed";
            var bytes = MinimalJpegBytes();
            var downloadCount = 0;
            string? callbackUrl = null;
            var cache = new CoverImageCacheService(root, (_, _) =>
            {
                downloadCount++;
                return Task.FromResult<Stream>(new MemoryStream(bytes));
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

    internal static async Task CoverImageCacheRejectsInvalidDownloadsAsync()
    {
        await AssertRejectedCoverDownloadAsync(
            "55555555555555555555555555555555",
            (_, _) => Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>())),
            "Empty cover image response should not be cached");
        await AssertRejectedCoverDownloadAsync(
            "66666666666666666666666666666666",
            (_, _) => Task.FromResult<Stream>(new MemoryStream(NonImageBytes())),
            "Non-image cover response should not be cached");
        await AssertRejectedCoverDownloadAsync(
            "77777777777777777777777777777777",
            (_, _) => Task.FromResult<Stream>(new MemoryStream(OversizedImageBytes())),
            "Oversized cover image response should not be cached");
        await AssertRejectedCoverDownloadAsync(
            "88888888888888888888888888888888",
            (_, ct) => Task.FromCanceled<Stream>(ct.IsCancellationRequested ? ct : new CancellationToken(true)),
            "Canceled cover image download should not be cached");
    }

    internal static async Task CoverImageCacheCancelsSlowDownloadAfterTimeoutAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            const string voiceId = "99999999999999999999999999999999";
            var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string? callbackUrl = null;
            var cache = new CoverImageCacheService(root, (_, ct) =>
            {
                downloadStarted.SetResult();
                ct.Register(() => downloadCanceled.TrySetResult());
                return Task.Delay(Timeout.InfiniteTimeSpan, ct).ContinueWith<Stream>(task =>
                {
                    if (task.IsCanceled)
                        throw new OperationCanceledException(ct);

                    return new MemoryStream(MinimalJpegBytes());
                }, CancellationToken.None);
            });

            var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/slow", 128, url => callbackUrl = url);

            await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await downloadCanceled.Task.WaitAsync(TimeSpan.FromSeconds(12));
            await result.CacheTask!.WaitAsync(TimeSpan.FromSeconds(2));

            var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
            AssertEqual(false, File.Exists(cachedFile), "Timed-out cover image download should not write a cache file");
            AssertEqual(null, callbackUrl, "Timed-out cover image download should keep the UI on the remote URL fallback");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task CoverImageCacheDownloadsThroughBoundedStreamAsync()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new Settings
            {
                VoiceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                CachedVoice = new CachedVoiceInfo { Title = "Streamed", CoverImage = "coverimage/streamed" },
            };
            var oversizedStream = new LimitGuardStream((256 * 1024) + 32, CoverImageCacheService.MaxCachedImageBytes + 1);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetStreamResponse = oversizedStream;
            var viewModel = new SettingsViewModel(
                CreateContext(settings: settings, httpService: httpService, pluginCacheDirectoryPath: root),
                settings);

            AssertEqual(
                "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/streamed",
                viewModel.CachedVoiceCoverUrl,
                "Missing selected voice cover cache should use remote fallback immediately");

            await http.GetStreamReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await oversizedStream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{settings.VoiceId}.jpg");
            AssertEqual(1, http.GetAsStreamCallCount, "Cover image cache should download through GetAsStreamAsync");
            AssertEqual(0, http.GetAsBytesCallCount, "Cover image cache should not buffer downloads through GetAsBytesAsync");
            AssertEqual(TimeSpan.FromSeconds(10), http.LastGetStreamOptions?.Timeout, "Cover image stream request should set a 10 second timeout");
            AssertEqual(false, File.Exists(cachedFile), "Oversized streamed cover image should not write a cache file");
            AssertEqual(
                CoverImageCacheService.MaxCachedImageBytes + 1,
                oversizedStream.TotalBytesRead,
                "Oversized streamed cover image should stop reading after the cache limit plus one byte");
            AssertEqual(
                true,
                oversizedStream.ReadCallCount < 20,
                "Oversized streamed cover image should not read the full response");
            AssertEqual(
                "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/streamed",
                viewModel.CachedVoiceCoverUrl,
                "Rejected streamed cover image should keep remote fallback URL");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static void CoverImageCacheClearsOnlyCoverImagesAndFormatsSize()
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

            var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 })));

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

    internal static void CoverImageCacheSizeScansCoverImagesDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            const string voiceId = "44444444444444444444444444444444";
            var cacheDir = Path.Combine(root, CoverImageCacheService.DirectoryName);
            Directory.CreateDirectory(cacheDir);

            var cache = new CoverImageCacheService(root, (_, _) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 9 })));
            AssertEqual("0 B", cache.GetFormattedCacheSize(), "Empty cover image cache should report zero bytes");

            var cachedFile = Path.Combine(cacheDir, $"{voiceId}.jpg");
            File.WriteAllBytes(cachedFile, new byte[] { 1, 2, 3, 4, 5 });

            AssertEqual("5 B", cache.GetFormattedCacheSize(), "Cache size should scan cover_images for files created outside the in-memory index");

            File.Delete(cachedFile);
            AssertEqual("0 B", cache.GetFormattedCacheSize(), "Cache size should scan cover_images after files are removed outside the in-memory index");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task ClearCoverImageCacheCommandTracksBusyStateAsync()
    {
        var clearStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new SettingsViewModel(
            CreateContext(),
            new Settings(),
            () =>
            {
                clearStarted.SetResult();
                return releaseClear.Task;
            },
            TimeSpan.FromSeconds(5));

        var commandTask = viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null);
        await clearStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(true, viewModel.IsClearingCoverImageCache, "Clear cache command should enter busy state while cleanup is running");
        AssertEqual(false, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache command should be disabled while cleanup is running");

        releaseClear.SetResult();
        await commandTask.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsClearingCoverImageCache, "Clear cache command should leave busy state after cleanup completes");
        AssertEqual(true, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache command should be enabled after cleanup completes");
    }

    internal static async Task ClearCoverImageCacheCommandTimesOutAndRestoresButtonAsync()
    {
        var snackbar = new TestSnackbar();
        var blockedClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new SettingsViewModel(
            CreateContext(snackbar),
            new Settings(),
            () => blockedClear.Task,
            TimeSpan.FromMilliseconds(30));

        await viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        AssertEqual(false, viewModel.IsClearingCoverImageCache, "Clear cache timeout should leave busy state");
        AssertEqual(true, viewModel.ClearCoverImageCacheCommand.CanExecute(null), "Clear cache timeout should re-enable the command");
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_ClearCache_Timeout",
            snackbar.LastError,
            "Clear cache timeout should show a localized error message");

        blockedClear.SetResult();
        await Task.Delay(30);
    }

    internal static async Task LateClearCoverImageCacheTaskDoesNotUnlockNewOperationAsync()
    {
        var clearCall = 0;
        var firstClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondClear = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new SettingsViewModel(
            CreateContext(),
            new Settings(),
            () => ++clearCall == 1 ? firstClear.Task : secondClear.Task,
            TimeSpan.FromMilliseconds(150));

        await viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(false, viewModel.IsClearingCoverImageCache, "First clear cache timeout should restore the button");

        var secondCommandTask = viewModel.ClearCoverImageCacheCommand.ExecuteAsync(null);
        AssertEqual(true, viewModel.IsClearingCoverImageCache, "Second clear cache operation should enter busy state");

        firstClear.SetResult();
        await Task.Delay(30);
        AssertEqual(true, viewModel.IsClearingCoverImageCache, "Late first clear task should not unlock the second operation");

        secondClear.SetResult();
        await secondCommandTask.WaitAsync(TimeSpan.FromSeconds(2));
        AssertEqual(false, viewModel.IsClearingCoverImageCache, "Second clear cache operation should leave busy state after completion");
    }

    internal static void ClearCacheButtonUsesLocalizedTextAndBusySpinner()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Presentation", "View", "SettingsView.xaml")));
        const string commandBinding = "Command=\"{Binding ClearCoverImageCacheCommand}\"";
        var commandIndex = xaml.IndexOf(commandBinding, StringComparison.Ordinal);
        AssertEqual(true, commandIndex >= 0, "Settings view should bind a clear cover image cache button");

        var buttonStart = xaml.LastIndexOf("<Button", commandIndex, StringComparison.Ordinal);
        var buttonEnd = xaml.IndexOf("</Button>", commandIndex, StringComparison.Ordinal);
        AssertEqual(true, buttonStart >= 0 && buttonEnd > commandIndex, "Clear cover image cache command should belong to a button element");

        var buttonXaml = xaml.Substring(buttonStart, buttonEnd - buttonStart);
        AssertEqual(
            true,
            buttonXaml.Contains("Text=\"{DynamicResource STranslate_Plugin_Tts_FishAudio_ClearCache}\"", StringComparison.Ordinal),
            "Clear cache button should show explicit localized text");
        AssertEqual(
            true,
            buttonXaml.Contains("IsClearingCoverImageCache", StringComparison.Ordinal)
            && buttonXaml.Contains("<DoubleAnimation", StringComparison.Ordinal)
            && buttonXaml.Contains("RepeatBehavior=\"Forever\"", StringComparison.Ordinal),
            "Clear cache button should show a rotating busy indicator while clearing");
        AssertEqual(
            false,
            buttonXaml.Contains("Content=\"{DynamicResource STranslate_Plugin_Tts_FishAudio_ClearCache}\"", StringComparison.Ordinal),
            "Clear cache button content should be composed so text and busy indicator can be shown together");
        AssertEqual(
            false,
            buttonXaml.Contains("FontFamily=\"Segoe MDL2 Assets\"", StringComparison.Ordinal),
            "Clear cache button should not be icon-only");
    }

    internal static async Task AssertRejectedCoverDownloadAsync(
        string voiceId,
        Func<string, CancellationToken, Task<Stream>> download,
        string message)
    {
        var root = CreateTempDirectory();
        try
        {
            string? callbackUrl = null;
            var cache = new CoverImageCacheService(root, download);
            var result = cache.ResolveCoverImageUrl(voiceId, "coverimage/rejected", 128, url => callbackUrl = url);
            var expectedRemoteUrl = "https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/rejected";

            AssertEqual(expectedRemoteUrl, result.DisplayUrl, $"{message}: missing cache should return the remote URL fallback immediately");
            AssertNotNull(result.CacheTask, $"{message}: missing cache should start a background download");

            await result.CacheTask!.WaitAsync(TimeSpan.FromSeconds(2));

            var cachedFile = Path.Combine(root, CoverImageCacheService.DirectoryName, $"{voiceId}.jpg");
            AssertEqual(false, File.Exists(cachedFile), $"{message}: invalid download should not write <id>.jpg");
            AssertEqual(null, callbackUrl, $"{message}: invalid download should not notify the UI with a local URL");
            AssertEqual("0 B", cache.GetFormattedCacheSize(), $"{message}: invalid download should not increase cache size");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0,
        0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x01,
        0x00, 0x60, 0x00, 0x60,
        0x00, 0x00,
        0xFF, 0xD9,
    ];

    internal static byte[] NonImageBytes() => System.Text.Encoding.UTF8.GetBytes("not an image");

    internal static byte[] OversizedImageBytes()
    {
        var bytes = new byte[(256 * 1024) + 1];
        var jpeg = MinimalJpegBytes();
        Array.Copy(jpeg, bytes, jpeg.Length);
        return bytes;
    }
}
