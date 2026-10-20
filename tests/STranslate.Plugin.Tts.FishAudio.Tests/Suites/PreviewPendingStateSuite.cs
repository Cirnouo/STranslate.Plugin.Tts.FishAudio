using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

internal static partial class PreviewSuite
{
    internal static async Task PreviewPublishesPreparationBeforeFirstAwaitAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        const string id = "11111111111111111111111111111111";
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Pending preview", SampleAudioUrl = "https://platform.r2.fish.audio/audio/pending.mp3" } };
        var (service, http) = TestHttpServiceProxy.Create();
        var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new TestPreviewAudioPlayer();
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings,
            clearCoverImageCacheAsync: null, clearCoverImageCacheTimeout: null, previewAudioPlayerFactory: () => player);
        object? pendingAtRequest = null;
        http.GetAsStreamAsyncHandler = (_, _, _) =>
        {
            pendingAtRequest = viewModel.PreparingPreviewVoiceId;
            return response.Task;
        };

        var first = viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        try
        {
            AssertEqual(id, pendingAtRequest, "The first preview click must publish its preparing Voice ID before issuing the resource request");
            AssertEqual(1, http.GetAsStreamCallCount, "The held resource request must have started before assertions");
            AssertEqual(null, viewModel.PreviewingVoiceId, "Pending preparation must not claim actual playback");
            AssertEqual(0d, viewModel.PreviewProgress, "Pending preparation must not invent playback progress");
            await viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
            AssertEqual(null, viewModel.PreparingPreviewVoiceId, "Second click must clear preparation immediately while the response remains held");
            AssertEqual(false, viewModel.IsDisplayVoicePreviewActive, "Second click must restore the idle control immediately");
        }
        finally
        {
            response.SetResult(new MemoryStream([1, 2, 3]));
            await first.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    internal static async Task PreviewPreparationTransitionsToRealPlaybackAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var search in new[] { false, true })
        {
            using var scenario = new PreviewScenario(search);
            var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
            string? preparingAtOpen = null;
            string? playingDuringOutputStart = "unobserved";
            scenario.Player.OnOpen = () => preparingAtOpen = scenario.ViewModel.PreparingPreviewVoiceId;
            scenario.Player.OnPlay = () => playingDuringOutputStart = scenario.ViewModel.PreviewingVoiceId;

            var preview = scenario.PlayAsync();
            AssertEqual(scenario.Id, scenario.ViewModel.PreparingPreviewVoiceId, "Preparation must publish synchronously for both commands");
            AssertEqual(true, scenario.ViewModel.IsDisplayVoicePreparing, "The selected card must mirror preparation by Voice ID");
            AssertEqual(true, scenario.Item.IsPreparingPreview, "The matching search card must mirror preparation by Voice ID");
            AssertEqual(true, scenario.ViewModel.IsDisplayVoicePreviewActive, "Selected preparation must expose its cancel control");
            AssertEqual(true, scenario.Item.IsPreviewActive, "Search preparation must expose its cancel control");
            AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreviewing, "Selected preparation must not show playing progress");
            AssertEqual(false, scenario.Item.IsBeingPreviewed, "Search preparation must not show playing progress");

            response.SetResult(new MemoryStream([1, 2, 3]));
            await preview;

            AssertEqual(scenario.Id, preparingAtOpen, "Preparation must remain active through local output preparation");
            AssertEqual(null, playingDuringOutputStart, "Playback must remain unannounced until output Play returns");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Successful output start must clear preparation");
            AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreparing, "Selected loading spinner must stop on success");
            AssertEqual(false, scenario.Item.IsPreparingPreview, "Search loading spinner must stop on success");
            AssertEqual(scenario.Id, scenario.ViewModel.PreviewingVoiceId, "Clearing preparation must retain actual playback");
            AssertEqual(true, scenario.ViewModel.IsDisplayVoicePreviewing, "Selected progress must become active only after output starts");
            AssertEqual(true, scenario.Item.IsBeingPreviewed, "Matching result progress must become active after output starts");
            scenario.Player.RaiseEnded();
            AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreviewActive, "Natural completion must return the selected control to idle");
            AssertEqual(false, scenario.Item.IsPreviewActive, "Natural completion must return the search control to idle");
        }
    }

    internal static async Task PreviewSameVoiceAcrossEntriesCancelsPreparationAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var searchFirst in new[] { false, true })
        {
            using var scenario = new PreviewScenario(searchFirst);
            var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken requestToken = default;
            scenario.Http.GetAsStreamAsyncHandler = (_, _, token) => { requestToken = token; return response.Task; };
            var first = scenario.PlayAsync();
            var duplicate = new VoiceSearchItem { Id = scenario.Id, SampleAudioUrl = PreviewScenario.Url };
            scenario.ViewModel.SearchResults = [scenario.Item, duplicate];
            AssertEqual(true, duplicate.IsPreparingPreview, "Newly displayed same-ID results must mirror the current preparation");

            if (searchFirst) await scenario.ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
            else await scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(duplicate);

            AssertEqual(true, requestToken.IsCancellationRequested, "A second click on the same Voice ID must cancel across both entry points");
            AssertEqual(1, scenario.Http.GetAsStreamCallCount, "Cross-entry cancellation must not start another resource request");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Cross-entry cancellation must clear preparation synchronously");
            AssertEqual(false, scenario.Item.IsPreviewActive, "The original result must return to idle on cancel");
            AssertEqual(false, duplicate.IsPreviewActive, "All matching results must return to idle on cancel");
            response.SetResult(new MemoryStream([1, 2, 3]));
            await first;
            AssertEqual(null, scenario.ViewModel.PreviewingVoiceId, "The canceled late response must not activate playback");
        }
    }

    internal static async Task PreviewOldCompletionCannotClearNewPreparationAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var searchFirst in new[] { false, true })
        {
            using var scenario = new PreviewScenario(searchFirst);
            scenario.Item.Id = "22222222222222222222222222222222";
            var oldResponse = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newResponse = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => scenario.Http.GetAsStreamCallCount == 1 ? oldResponse.Task : newResponse.Task;
            var first = scenario.PlayAsync();
            var next = searchFirst ? scenario.ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null)
                : scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(scenario.Item);
            var currentId = searchFirst ? scenario.Id : scenario.Item.Id;
            AssertEqual(currentId, scenario.ViewModel.PreparingPreviewVoiceId, "Switching entries must transfer preparation to the new Voice ID");

            oldResponse.SetResult(new MemoryStream([4, 5, 6]));
            await first;
            AssertEqual(currentId, scenario.ViewModel.PreparingPreviewVoiceId, "A canceled request's late completion must not clear the new spinner");
            AssertEqual(searchFirst, scenario.ViewModel.IsDisplayVoicePreparing, "Only the current selected target may show preparation");
            AssertEqual(!searchFirst, scenario.Item.IsPreparingPreview, "Only the current search target may show preparation");

            newResponse.SetResult(new MemoryStream([1, 2, 3]));
            await next.WaitAsync(TimeSpan.FromSeconds(2));
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "The current successful request must clear its own spinner");
            AssertEqual(currentId, scenario.ViewModel.PreviewingVoiceId, "Only the current request may start playback");
        }
    }

    internal static async Task PreviewPreparationFailureAndResetClearStateAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        foreach (var search in new[] { false, true })
        {
            foreach (var failure in new[] { "refresh", "download", "open", "play" })
            {
                using var scenario = new PreviewScenario(search);
                if (failure == "refresh")
                {
                    scenario.ViewModel.CachedVoiceSampleUrl = scenario.Item.SampleAudioUrl = CreateExpiredPreviewUrl("preparation.mp3");
                    scenario.Http.GetAsyncHandler = (_, _, _) => Task.FromException<string>(new HttpRequestException("Metadata failed"));
                }
                else if (failure == "download") scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromException<Stream>(new IOException("Resource failed"));
                else if (failure == "open") scenario.Player.OnOpen = () => throw new InvalidOperationException("Open failed");
                else scenario.Player.OnPlay = () => throw new InvalidOperationException("Play failed");
                await scenario.PlayAsync();
                AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, $"{failure} failure must clear preparation");
                AssertEqual(null, scenario.ViewModel.PreviewingVoiceId, $"{failure} failure must leave playback idle");
                AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreviewActive, $"{failure} failure must restore the selected play control");
                AssertEqual(false, scenario.Item.IsPreviewActive, $"{failure} failure must restore the search play control");
            }
            using (var scenario = new PreviewScenario(search))
            {
                var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken refreshToken = default;
                scenario.ViewModel.CachedVoiceSampleUrl = scenario.Item.SampleAudioUrl = CreateExpiredPreviewUrl("clear.mp3");
                scenario.Http.GetAsyncHandler = (_, _, token) => { refreshToken = token; return response.Task; };
                var pending = scenario.PlayAsync();
                AssertEqual(scenario.Id, scenario.ViewModel.PreparingPreviewVoiceId, "Metadata refresh must show preparation before its first await");
                scenario.ViewModel.ClearVoiceCommand.Execute(null);
                AssertEqual(true, refreshToken.IsCancellationRequested, "Clear voice must cancel a pending preview refresh");
                AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Clear voice must clear pending state before a late refresh returns");
                AssertEqual(false, scenario.Item.IsPreparingPreview, "Clear voice must clear mirrored result loading state");
                response.SetResult(RefreshedPreviewJson(scenario.Id));
                await pending;
                AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Late refresh must not restore cleared preparation");
            }
        }
    }

    internal static async Task PreviewQueuedPublicationsReadCurrentPreparationOwnerAsync()
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { VoiceId = "11111111111111111111111111111111", CachedVoice = new() { SampleAudioUrl = PreviewScenario.Url } };
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetAsStreamAsyncHandler = (_, _, token) => Task.FromCanceled<Stream>(new CancellationToken(true));
        var queue = new Queue<Action>();
        string? visibleId = null;
        using var coordinator = new PreviewRefreshCoordinator(CreateContext(settings: settings, httpService: service),
            () => settings.VoiceId, () => settings.CachedVoice.SampleAudioUrl, () => settings.VoiceId, () => settings.VoiceId,
            () => [], () => null, () => { }, (_, stream) => stream.Dispose(), update => update(settings), _ => { }, (_, _, _, _) => "", () => false,
            applyPreparingState: id => visibleId = id, runOnUiThread: queue.Enqueue);
        await coordinator.ToggleDisplayPreviewAsync();
        var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
        var next = coordinator.ToggleDisplayPreviewAsync();
        while (queue.TryDequeue(out var publish)) publish();
        AssertEqual(settings.VoiceId, visibleId, "Queued completion from an older request must read the new preparation owner");
        coordinator.Dispose();
        while (queue.TryDequeue(out var publish)) publish();
        AssertEqual(null, visibleId, "Disposal must publish idle even if preparing callbacks were queued");
        response.SetResult(new MemoryStream([1]));
        await next.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
