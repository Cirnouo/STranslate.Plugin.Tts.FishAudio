using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static TestAssertions;

// Ordinary typed responsibilities extracted from the twelve retained legacy
// owners. Original files remain unchanged until the coordinated retirement.
internal static partial class PreviewSuite
{
    private static Task PlayOwnedPreviewAsync(PreviewCaseFixtureOwner owner, PreviewScenario scenario, bool search)
    {
        var originalCommandTask = search
            ? scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(scenario.Item)
            : scenario.ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null);
        owner.Track(originalCommandTask);
        var originalGuardedTask = originalCommandTask.WaitAsync(RegressionExecution.Timeout);
        return owner.Track(originalGuardedTask);
    }

    internal static Task PreparationTransitionsToPlaybackCaseAsync(bool search) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(response);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
            string? preparingAtOpen = null;
            string? playingDuringOutputStart = "unobserved";
            scenario.Player.OnOpen = () => preparingAtOpen = scenario.ViewModel.PreparingPreviewVoiceId;
            scenario.Player.OnPlay = () => playingDuringOutputStart = scenario.ViewModel.PreviewingVoiceId;

            var preview = PlayOwnedPreviewAsync(owner, scenario, search);
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
        });

    internal static Task SameVoiceAcrossEntriesCancelsCaseAsync(bool searchFirst) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(searchFirst));
            var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(response);
            CancellationToken requestToken = default;
            scenario.Http.GetAsStreamAsyncHandler = (_, _, token) => { requestToken = token; return response.Task; };
            var first = PlayOwnedPreviewAsync(owner, scenario, searchFirst);
            var duplicate = new VoiceSearchItem { Id = scenario.Id, SampleAudioUrl = PreviewScenario.Url };
            scenario.ViewModel.SearchResults = [scenario.Item, duplicate];
            AssertEqual(true, duplicate.IsPreparingPreview, "Newly displayed same-ID results must mirror the current preparation");

            if (searchFirst) await owner.Track(scenario.ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null));
            else await owner.Track(scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(duplicate));

            AssertEqual(true, requestToken.IsCancellationRequested, "A second click on the same Voice ID must cancel across both entry points");
            AssertEqual(1, scenario.Http.GetAsStreamCallCount, "Cross-entry cancellation must not start another resource request");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Cross-entry cancellation must clear preparation synchronously");
            AssertEqual(false, scenario.Item.IsPreviewActive, "The original result must return to idle on cancel");
            AssertEqual(false, duplicate.IsPreviewActive, "All matching results must return to idle on cancel");
            response.SetResult(new MemoryStream([1, 2, 3]));
            await first;
            AssertEqual(null, scenario.ViewModel.PreviewingVoiceId, "The canceled late response must not activate playback");
        });

    internal static Task OldCompletionKeepsNewPreparationCaseAsync(bool searchFirst) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(searchFirst));
            scenario.Item.Id = "22222222222222222222222222222222";
            var oldResponse = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(oldResponse);
            var newResponse = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(newResponse);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => scenario.Http.GetAsStreamCallCount == 1 ? oldResponse.Task : newResponse.Task;
            var first = PlayOwnedPreviewAsync(owner, scenario, searchFirst);
            var next = searchFirst ? owner.Track(scenario.ViewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null))
                : owner.Track(scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(scenario.Item));
            var currentId = searchFirst ? scenario.Id : scenario.Item.Id;
            AssertEqual(currentId, scenario.ViewModel.PreparingPreviewVoiceId, "Switching entries must transfer preparation to the new Voice ID");

            oldResponse.SetResult(new MemoryStream([4, 5, 6]));
            await first;
            AssertEqual(currentId, scenario.ViewModel.PreparingPreviewVoiceId, "A canceled request's late completion must not clear the new spinner");
            AssertEqual(searchFirst, scenario.ViewModel.IsDisplayVoicePreparing, "Only the current selected target may show preparation");
            AssertEqual(!searchFirst, scenario.Item.IsPreparingPreview, "Only the current search target may show preparation");

            newResponse.SetResult(new MemoryStream([1, 2, 3]));
            await next.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "The current successful request must clear its own spinner");
            AssertEqual(currentId, scenario.ViewModel.PreviewingVoiceId, "Only the current request may start playback");
        });

    internal static Task PreparationFailureClearsStateCaseAsync(bool search, string failure) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            if (failure == "refresh")
            {
                scenario.ViewModel.CachedVoiceSampleUrl = scenario.Item.SampleAudioUrl = CreateExpiredPreviewUrl("preparation.mp3");
                scenario.Http.GetAsyncHandler = (_, _, _) => Task.FromException<string>(new HttpRequestException("Metadata failed"));
            }
            else if (failure == "download") scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromException<Stream>(new IOException("Resource failed"));
            else if (failure == "open") scenario.Player.OnOpen = () => throw new InvalidOperationException("Open failed");
            else scenario.Player.OnPlay = () => throw new InvalidOperationException("Play failed");
            await PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, $"{failure} failure must clear preparation");
            AssertEqual(null, scenario.ViewModel.PreviewingVoiceId, $"{failure} failure must leave playback idle");
            AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreviewActive, $"{failure} failure must restore the selected play control");
            AssertEqual(false, scenario.Item.IsPreviewActive, $"{failure} failure must restore the search play control");
        });

    internal static Task ResetClearsPreparationCaseAsync(bool search) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(response);
            CancellationToken refreshToken = default;
            scenario.ViewModel.CachedVoiceSampleUrl = scenario.Item.SampleAudioUrl = CreateExpiredPreviewUrl("clear.mp3");
            scenario.Http.GetAsyncHandler = (_, _, token) => { refreshToken = token; return response.Task; };
            var pending = PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(scenario.Id, scenario.ViewModel.PreparingPreviewVoiceId, "Metadata refresh must show preparation before its first await");
            scenario.ViewModel.ClearVoiceCommand.Execute(null);
            AssertEqual(true, refreshToken.IsCancellationRequested, "Clear voice must cancel a pending preview refresh");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Clear voice must clear pending state before a late refresh returns");
            AssertEqual(false, scenario.Item.IsPreparingPreview, "Clear voice must clear mirrored result loading state");
            response.SetResult(RefreshedPreviewJson(scenario.Id));
            await pending;
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Late refresh must not restore cleared preparation");
        });

    internal static Task QueuedPublicationReadsCurrentPreparationCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var settings = new Settings { VoiceId = "11111111111111111111111111111111", CachedVoice = new() { SampleAudioUrl = PreviewScenario.Url } };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetAsStreamAsyncHandler = (_, _, token) => Task.FromCanceled<Stream>(new CancellationToken(true));
            var queue = new Queue<Action>();
            string? visibleId = null;
            var coordinator = new PreviewRefreshCoordinator(CreateContext(settings: settings, httpService: service),
                () => settings.VoiceId, () => settings.CachedVoice.SampleAudioUrl, () => settings.VoiceId, () => settings.VoiceId,
                () => [], () => null, () => { }, (_, stream) => stream.Dispose(), update => update(settings), _ => { }, (_, _, _, _) => "", () => false,
                applyPreparingState: id => visibleId = id, runOnUiThread: queue.Enqueue);
            owner.Own(coordinator);
            await owner.Track(coordinator.ToggleDisplayPreviewAsync());
            var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(response);
            http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
            var next = owner.Track(coordinator.ToggleDisplayPreviewAsync());
            while (queue.TryDequeue(out var publish)) publish();
            AssertEqual(settings.VoiceId, visibleId, "Queued completion from an older request must read the new preparation owner");
            coordinator.Dispose();
            while (queue.TryDequeue(out var publish)) publish();
            AssertEqual(null, visibleId, "Disposal must publish idle even if preparing callbacks were queued");
            response.SetResult(new MemoryStream([1]));
            await next.WaitAsync(RegressionExecution.Timeout);
        });

    internal static Task SearchPreviewRemovedResultIgnoresLateRefreshCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string voiceId = "fedcba9876543210fedcba9876543210";
            const string lateUrl = "https://platform.r2.fish.audio/audio/search-late.mp3";
            var oldUrl = CreateExpiredPreviewUrl("search-removed.mp3");
            var item = new VoiceSearchItem
            {
                Id = voiceId,
                Title = "Original Search Voice",
                SampleAudioUrl = oldUrl,
            };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, _) =>
            {
                requestStarted.TrySetResult();
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var viewModel = new SettingsViewModel(
                CreateContext(httpService: httpService),
                new Settings(),
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player)
            {
                SearchResults = [item],
            };
            owner.Own(viewModel);

            var previewTask = owner.Track(viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item));
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
            viewModel.SearchResults = [];
            releaseResponse.SetResult($$"""
                {"_id":"{{voiceId}}","title":"Late Search Voice","description":"late","cover_image":"late-cover","samples":[{"audio":"{{lateUrl}}"}],"task_count":99}
                """);
            await previewTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual("Original Search Voice", item.Title, "A removed search item should ignore late refreshed metadata");
            AssertEqual(oldUrl, item.SampleAudioUrl, "A removed search item should keep its original sample URL after a late response");
            AssertEqual(0, player.PlayCallCount, "A removed search item should not start playback after a late response");
        });

    internal static Task SearchPreviewSecondClickCancelsPendingRefreshCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string voiceId = "fedcba9876543210fedcba9876543210";
            var oldUrl = CreateExpiredPreviewUrl("search-pending.mp3");
            var item = new VoiceSearchItem
            {
                Id = voiceId,
                Title = "Pending Search Voice",
                SampleAudioUrl = oldUrl,
            };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, cancellationToken) =>
            {
                requestStarted.TrySetResult();
                owner.Scope(cancellationToken.Register(() => requestCanceled.TrySetResult()));
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var viewModel = new SettingsViewModel(
                CreateContext(httpService: httpService),
                new Settings(),
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player)
            {
                SearchResults = [item],
            };
            owner.Own(viewModel);

            var firstPreviewTask = owner.Track(viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item));
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);
            await owner.Track(viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item)).WaitAsync(RegressionExecution.Timeout);
            await requestCanceled.Task.WaitAsync(RegressionExecution.Timeout);

            releaseResponse.SetResult($$"""
                {"_id":"{{voiceId}}","title":"Late Search Voice","description":"","cover_image":"","samples":[{"audio":"https://platform.r2.fish.audio/audio/search-late-after-cancel.mp3"}],"task_count":0}
                """);
            await firstPreviewTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(1, http.GetCallCount, "Second click on a pending search refresh should cancel without starting another detail request");
            AssertEqual("Pending Search Voice", item.Title, "Canceled search refresh should not apply late metadata");
            AssertEqual(oldUrl, item.SampleAudioUrl, "Canceled search refresh should preserve the original URL");
            AssertEqual(0, player.PlayCallCount, "Canceled search refresh should not start playback");
        });

    internal static Task DisplayPreviewSecondClickCancelsPendingRefreshWithoutRestartCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string voiceId = "0123456789abcdef0123456789abcdef";
            var oldUrl = CreateExpiredPreviewUrl("pending-old.mp3");
            const string lateUrl = "https://platform.r2.fish.audio/audio/pending-late.mp3";
            var originalCache = new CachedVoiceInfo { Title = "Pending Original", SampleAudioUrl = oldUrl };
            var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, ct) =>
            {
                requestStarted.TrySetResult();
                owner.Scope(ct.Register(() => requestCanceled.TrySetResult()));
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var context = CreateContext(settings: settings, httpService: httpService);
            var contextProxy = (ContextProxy)(object)context;
            var viewModel = new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player);
            owner.Own(viewModel);

            owner.ObserveCommand(() => viewModel.ToggleDisplayPreviewCommand.ExecutionTask);
            viewModel.ToggleDisplayPreviewCommand.Execute(null);
            var firstPreviewTask = viewModel.ToggleDisplayPreviewCommand.ExecutionTask
                ?? throw new InvalidOperationException("First selected preview command should expose its execution task");
            owner.Track(firstPreviewTask);
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);

            viewModel.ToggleDisplayPreviewCommand.Execute(null);
            var canceledBeforeRelease = requestCanceled.Task.IsCompleted;
            var getCallsBeforeRelease = http.GetCallCount;
            var savesBeforeRelease = contextProxy.SaveCount;
            var playsBeforeRelease = player.PlayCallCount;

            releaseResponse.SetResult($$"""
                {"_id":"{{voiceId}}","title":"Pending Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
                """);
            await firstPreviewTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(true, canceledBeforeRelease, "Second selected preview click should cancel the pending request before its response is released");
            AssertEqual(1, getCallsBeforeRelease, "Second selected preview click should not start another model detail request");
            AssertEqual(0, savesBeforeRelease, "Second selected preview click should not save settings before the late response");
            AssertEqual(0, playsBeforeRelease, "Second selected preview click should not start playback before the late response");
            AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late canceled response should not replace cached voice metadata");
            AssertEqual(0, contextProxy.SaveCount, "Late canceled response should not save settings");
            AssertEqual(0, player.PlayCallCount, "Late canceled response should not start playback");
            AssertEqual(null, viewModel.PreviewingVoiceId, "Canceling a pending selected preview should leave playback stopped");
        });

    internal static Task SearchPreviewInvalidatesPendingDisplayRefreshCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string selectedVoiceId = "11111111111111111111111111111111";
            const string searchVoiceId = "22222222222222222222222222222222";
            var selectedOldUrl = CreateExpiredPreviewUrl("selected-old.mp3");
            const string selectedLateUrl = "https://platform.r2.fish.audio/audio/selected-late.mp3";
            const string searchUrl = "https://platform.r2.fish.audio/audio/search-current.mp3";
            var originalCache = new CachedVoiceInfo { Title = "Selected Original", SampleAudioUrl = selectedOldUrl };
            var settings = new Settings { VoiceId = selectedVoiceId, CachedVoice = originalCache };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, ct) =>
            {
                requestStarted.TrySetResult();
                owner.Scope(ct.Register(() => requestCanceled.TrySetResult()));
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var context = CreateContext(settings: settings, httpService: httpService);
            var contextProxy = (ContextProxy)(object)context;
            var viewModel = new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player);
            owner.Own(viewModel);

            var selectedPreviewTask = owner.Track(viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null));
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);

            var searchItem = new VoiceSearchItem
            {
                Id = searchVoiceId,
                SampleAudioUrl = searchUrl,
            };
            viewModel.SearchResults = [searchItem];
            await owner.Track(viewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(searchItem)).WaitAsync(RegressionExecution.Timeout);
            releaseResponse.SetResult($$"""
                {"_id":"{{selectedVoiceId}}","title":"Selected Late","description":"Late","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{selectedLateUrl}}"}],"task_count":99,"author":null,"tags":[],"languages":[],"like_count":0}
                """);
            await selectedPreviewTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(searchVoiceId, viewModel.PreviewingVoiceId, "Search preview should remain active after the older selected refresh completes");
            AssertEqual(searchUrl, http.LastGetStreamUrl, "Late selected refresh should not replace the search preview URL");
            AssertEqual(1, player.PlayCallCount, "Late selected refresh should not start selected voice playback");
            AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Late selected refresh should not replace cached voice metadata after search preview starts");
            AssertEqual(0, contextProxy.SaveCount, "Late selected refresh should not save cached voice metadata after search preview starts");
            await requestCanceled.Task.WaitAsync(RegressionExecution.Timeout);
        });

    internal static Task DisplayPreviewVoiceSwitchIgnoresLateRefreshCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string oldVoiceId = "11111111111111111111111111111111";
            const string newVoiceId = "22222222222222222222222222222222";
            var oldUrl = CreateExpiredPreviewUrl("old-selected.mp3");
            const string newUrl = "https://platform.r2.fish.audio/audio/new-selected.mp3";
            const string lateUrl = "https://platform.r2.fish.audio/audio/late-old.mp3";
            var settings = new Settings
            {
                VoiceId = oldVoiceId,
                CachedVoice = new CachedVoiceInfo { Title = "Old Voice", SampleAudioUrl = oldUrl },
            };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, ct) =>
            {
                requestStarted.TrySetResult();
                owner.Scope(ct.Register(() => requestCanceled.TrySetResult()));
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var context = CreateContext(settings: settings, httpService: httpService);
            var contextProxy = (ContextProxy)(object)context;
            var viewModel = new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player);
            owner.Own(viewModel);

            var previewTask = owner.Track(viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null));
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);

            var replacementCard = new VoiceSearchItem
            {
                Id = newVoiceId,
                Title = "New Voice",
                Description = "New description",
                AuthorName = "New Author",
                TaskCount = 5,
                SampleAudioUrl = newUrl,
            };
            viewModel.SearchResults = [replacementCard];
            viewModel.SelectVoiceCommand.Execute(replacementCard);
            var saveCountAfterSwitch = contextProxy.SaveCount;

            releaseResponse.SetResult($$"""
                {"_id":"{{oldVoiceId}}","title":"Late Old Voice","description":"Late description","cover_image":"late-cover","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":99,"author":{"_id":"","nickname":"Late Author","avatar":""},"tags":[],"languages":[],"like_count":0}
                """);
            await previewTask.WaitAsync(RegressionExecution.Timeout);

            await requestCanceled.Task.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(newVoiceId, settings.VoiceId, "Voice switch should keep the new selected voice ID after a late preview refresh");
            AssertEqual("New Voice", settings.CachedVoice?.Title, "Voice switch should keep the new cached voice after a late preview refresh");
            AssertEqual(newUrl, settings.CachedVoice?.SampleAudioUrl, "Voice switch should keep the new sample URL after a late preview refresh");
            AssertEqual(saveCountAfterSwitch, contextProxy.SaveCount, "Late preview refresh should not save settings after the selected voice changes");
            AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after the selected voice changes");
        });

    internal static Task DisplayPreviewDisposeCancelsAndIgnoresLateRefreshCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            const string voiceId = "11111111111111111111111111111111";
            var oldUrl = CreateExpiredPreviewUrl("dispose-old.mp3");
            const string lateUrl = "https://platform.r2.fish.audio/audio/dispose-late.mp3";
            var originalCache = new CachedVoiceInfo { Title = "Keep After Dispose", SampleAudioUrl = oldUrl };
            var settings = new Settings { VoiceId = voiceId, CachedVoice = originalCache };
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(releaseResponse);
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetAsyncHandler = (_, _, ct) =>
            {
                requestStarted.TrySetResult();
                owner.Scope(ct.Register(() => requestCanceled.TrySetResult()));
                return releaseResponse.Task;
            };
            var player = new TestPreviewAudioPlayer();
            var context = CreateContext(settings: settings, httpService: httpService);
            var contextProxy = (ContextProxy)(object)context;
            var viewModel = new SettingsViewModel(
                context,
                settings,
                clearCoverImageCacheAsync: null,
                clearCoverImageCacheTimeout: null,
                previewAudioPlayerFactory: () => player);
            owner.Own(viewModel);

            var previewTask = owner.Track(viewModel.ToggleDisplayPreviewCommand.ExecuteAsync(null));
            await requestStarted.Task.WaitAsync(RegressionExecution.Timeout);

            viewModel.Dispose();
            var saveCountAfterDispose = contextProxy.SaveCount;
            await requestCanceled.Task.WaitAsync(RegressionExecution.Timeout);

            releaseResponse.SetResult($$"""
                {"_id":"{{voiceId}}","title":"Late After Dispose","description":"","cover_image":"","samples":[{"title":"","text":"","audio":"{{lateUrl}}"}],"task_count":0,"author":null,"tags":[],"languages":[],"like_count":0}
                """);
            await previewTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(true, ReferenceEquals(originalCache, settings.CachedVoice), "Disposed preview refresh should preserve the original cached voice");
            AssertEqual(saveCountAfterDispose, contextProxy.SaveCount, "Late preview refresh should not save settings after Dispose");
            AssertEqual(0, player.PlayCallCount, "Late preview refresh should not start playback after Dispose");
        });

    internal static Task RejectedPreviewSizeCaseAsync(bool search, int size) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            var stream = owner.Scope(new CountingPreviewStream(size));
            scenario.Http.GetStreamResponse = stream;
            await PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(1, scenario.Http.GetAsStreamCallCount, "A valid visible preview target must reach the host downloader");
            AssertEqual(0, scenario.Player.PlayCallCount, "Empty or oversized audio must not create playback");
            AssertEqual(true, stream.Disposed, "Rejected resource stream must be disposed");
            AssertEqual(true, stream.BytesRead <= PreviewAudioDownloader.MaximumBytes + 1, "Copy must read at most cap plus one byte");
            AssertEqual(SettingsViewModel.PreviewPlaybackFailedKey, scenario.Snackbar.LastError, "Download rejection should use the playback error");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Rejected downloads must clear the preparation indicator");
        });

    internal static Task ExactCapPreviewCaseAsync(bool search) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            var stream = owner.Scope(new CountingPreviewStream(PreviewAudioDownloader.MaximumBytes));
            scenario.Http.GetStreamResponse = stream;
            scenario.Player.OnOpen = () =>
            {
                AssertEqual(true, scenario.Player.LastOpenedStream?.CanSeek, "Local playback requires a seekable buffer");
                AssertEqual(0L, scenario.Player.LastOpenedStream?.Position, "Local buffer must rewind before Open");
            };
            await PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(1, scenario.Player.PlayCallCount, "An exact-cap resource must remain accepted");
            AssertEqual(true, stream.Disposed, "Network stream should close before local playback");
        });

    internal static Task RefreshedResourcePreviewCaseAsync(bool search, HttpStatusCode status) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            scenario.Http.GetResponseJson = RefreshedPreviewJson(scenario.Id);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => scenario.Http.GetAsStreamCallCount == 1
                ? Task.FromException<Stream>(new HttpRequestException("Private signed URL must not be logged", null, status))
                : Task.FromResult<Stream>(new MemoryStream([7, 8, 9]));
            await PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(2, scenario.Http.GetAsStreamCallCount, "403/404 should retry the refreshed resource once");
            AssertEqual(1, scenario.Http.GetCallCount, "403/404 should refresh voice detail once");
            AssertEqual(1, scenario.Player.PlayCallCount, "Refreshed sample should play locally");
            AssertEqual("Bearer private-preview-key", AssertHeaders(scenario.Http.LastGetOptions, "Detail refresh headers")["Authorization"], "Metadata refresh should use the configured value while resources stay unauthenticated");
            AssertEqual(true, scenario.Http.GetStreamOptionsByUrl.All(call => call.Options?.Headers is null || !call.Options.Headers.ContainsKey("Authorization")), "No resource request may receive authorization");
        });

    internal static Task TerminalResourceFailurePreviewCaseAsync(bool search, HttpStatusCode status) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            scenario.Http.GetResponseJson = RefreshedPreviewJson(scenario.Id);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromException<Stream>(
                new HttpRequestException("https://user:private@platform.r2.fish.audio/a?X-Amz-Signature=private-signature", null, status));
            await PlayOwnedPreviewAsync(owner, scenario, search);
            AssertEqual(status == HttpStatusCode.Forbidden ? 2 : 1, scenario.Http.GetAsStreamCallCount, "Only 403/404 may retry, without a loop");
            AssertEqual(status == HttpStatusCode.Forbidden ? 1 : 0, scenario.Http.GetCallCount, "Refresh must be bounded");
            AssertEqual(0, scenario.Player.PlayCallCount, "Failed download must not use stale bytes");
            AssertEqual(false, scenario.Logger.Contains(LogLevel.Warning, "private-signature"), "Credential exception details must not enter plugin logs");
        });

    internal static Task PendingDownloadCancellationCaseAsync(bool search, string trigger) =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(search));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(release);
            var cancelled = false;
            scenario.Http.GetAsStreamAsyncHandler = (_, _, token) =>
            {
                owner.Scope(token.Register(() => cancelled = true));
                started.SetResult();
                return release.Task; // Deliberately ignore cancellation, then return a late stream.
            };
            var first = PlayOwnedPreviewAsync(owner, scenario, search);
            await started.Task.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(scenario.Id, scenario.ViewModel.PreparingPreviewVoiceId, "Pending downloads must show the preparation indicator");
            if (trigger == "second-click") await PlayOwnedPreviewAsync(owner, scenario, search);
            else if (trigger == "voice-change") scenario.ViewModel.VoiceId = "22222222222222222222222222222222";
            else if (trigger == "results-replaced") scenario.ViewModel.SearchResults = [];
            else scenario.ViewModel.Dispose();
            AssertEqual(true, cancelled, "Stop/switch/removal/dispose must cancel the pending host request");
            AssertEqual(null, scenario.ViewModel.PreparingPreviewVoiceId, "Stop/switch/removal/dispose must clear preparation before the held response returns");
            AssertEqual(false, scenario.ViewModel.IsDisplayVoicePreparing, "Stop/switch/removal/dispose must clear selected preparation");
            AssertEqual(false, scenario.Item.IsPreparingPreview, "Stop/switch/removal/dispose must clear search preparation");
            var late = new CountingPreviewStream(3);
            release.SetResult(late);
            await first.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(true, late.Disposed, "A late resource returned after cancellation must be disposed");
            AssertEqual(0, scenario.Player.PlayCallCount, "A stale download must never start playback");
            AssertEqual(null, scenario.Snackbar.LastError, "User cancellation must remain quiet");
        });

    internal static Task SwitchEntryRejectsLateDownloadCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(false));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.ReleaseOnCleanup(release);
            scenario.Http.GetAsStreamAsyncHandler = (_, _, _) =>
            {
                if (scenario.Http.GetAsStreamCallCount == 1) { started.SetResult(); return release.Task; }
                return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
            };
            var first = PlayOwnedPreviewAsync(owner, scenario, false);
            await started.Task.WaitAsync(RegressionExecution.Timeout);
            var item = new VoiceSearchItem { Id = "22222222222222222222222222222222", SampleAudioUrl = PreviewScenario.Url };
            scenario.ViewModel.SearchResults = [item];
            await owner.Track(scenario.ViewModel.ToggleSearchItemPreviewCommand.ExecuteAsync(item));
            var late = new CountingPreviewStream(3);
            release.SetResult(late);
            await first.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(item.Id, scenario.ViewModel.PreviewingVoiceId, "Late selected download must not replace the new search preview");
            AssertEqual(1, scenario.Player.PlayCallCount, "Only the current entry may play");
            AssertEqual(true, late.Disposed, "Stale cross-entry response must close");
        });

    internal static Task PreparationUsesOneDeadlineCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var clock = new PreviewManualTimeProvider();
            var settings = new Settings { VoiceId = "11111111111111111111111111111111", CachedVoice = new() { SampleAudioUrl = CreateExpiredPreviewUrl("budget.mp3") } };
            var (service, http) = TestHttpServiceProxy.Create();
            var snackbar = new TestSnackbar();
            var body = owner.Scope(new BlockingPreviewStream());
            CancellationToken detailToken = default;
            http.GetAsyncHandler = (_, _, token) =>
            {
                detailToken = token;
                clock.Advance(TimeSpan.FromSeconds(6));
                return Task.FromResult(RefreshedPreviewJson(settings.VoiceId));
            };
            http.GetAsStreamAsyncHandler = (_, _, token) =>
            {
                AssertEqual(detailToken, token, "Refresh and download must share the same cancellation token");
                clock.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult<Stream>(body);
            };
            var plays = 0;
            string? preparingVoiceId = null;
            var coordinator = new PreviewRefreshCoordinator(CreateContext(snackbar, settings, service),
                () => settings.VoiceId, () => settings.CachedVoice?.SampleAudioUrl,
                () => settings.VoiceId, () => settings.VoiceId, () => [], () => null, () => { },
                (_, buffer) => { plays++; buffer.Dispose(); }, update => update(settings), _ => { }, (_, _, _, _) => "", () => false, clock,
                applyPreparingState: id => preparingVoiceId = id);
            owner.Own(coordinator);
            var pending = owner.Track(coordinator.ToggleDisplayPreviewAsync());
            await body.Started.Task.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(settings.VoiceId, preparingVoiceId, "The shared refresh/download deadline must keep preparation visible while its body is pending");
            clock.Advance(TimeSpan.FromSeconds(5));
            AssertEqual(false, body.Token.IsCancellationRequested, "The whole budget should still have one second remaining");
            clock.Advance(TimeSpan.FromSeconds(1));
            await pending.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(true, body.Token.IsCancellationRequested, "Body reading must stop at the original 15-second deadline");
            AssertEqual(true, body.Disposed, "Timed-out body must close");
            AssertEqual(0, plays, "A timed-out preparation must not start a player");
            AssertEqual(null, preparingVoiceId, "The operation deadline must clear preparation after failure");
            AssertEqual(SettingsViewModel.PreviewPlaybackFailedKey, snackbar.LastError, "A still-current timeout should report failure");
        });

    internal static Task LocalPlayerStateAndFailureCaseAsync(string failure) =>
        PreviewCaseFixtureOwner.RunAsync(owner =>
        {
            var player = new TestPreviewAudioPlayer();
            var logger = new TestLogger();
            var failures = 0;
            PreviewPlaybackController? controller = null;
            player.OnOpen = () =>
            {
                AssertEqual(null, controller!.PreviewingVoiceId, "Open must not activate the playing state");
                if (failure == "open") throw new InvalidOperationException("X-Amz-Signature=private");
            };
            player.OnPlay = () =>
            {
                AssertEqual(null, controller!.PreviewingVoiceId, "Play must return before state becomes active");
                if (failure == "play") throw new InvalidOperationException("X-Amz-Signature=private");
                if (failure == "synchronous-end") player.RaiseEnded();
            };
            controller = owner.Own(new PreviewPlaybackController(logger, (_, _) => { }, _ => failures++, () => failure == "factory"
                ? throw new InvalidOperationException("X-Amz-Signature=private") : player));
            var buffer = new MemoryStream([1, 2, 3]);
            controller.Toggle("voice", buffer);
            AssertEqual(failure == "none" ? "voice" : null, controller.PreviewingVoiceId, "Only successful Play may leave an active preview");
            AssertEqual(failure is "factory" or "open" or "play" ? 1 : 0, failures, "Failure notification must occur once");
            if (failure != "none") AssertEqual(false, buffer.CanRead, "A failed or ended player must release the local buffer");
            AssertEqual(false, logger.Contains(LogLevel.Warning, "X-Amz"), "Player exception credentials must not be logged");
            return Task.CompletedTask;
        });

    internal static Task QueuedStalePlayerEventCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(owner =>
        {
            var oldPlayer = new TestPreviewAudioPlayer();
            var current = new TestPreviewAudioPlayer();
            var queue = new Queue<Action>();
            var created = 0;
            var playback = new PreviewPlaybackController(null, (_, _) => { }, _ => throw new Exception("Stale failure surfaced"),
                () => created++ == 0 ? oldPlayer : current, queue.Enqueue);
            owner.Own(playback);
            playback.Toggle("old", new MemoryStream([1]));
            oldPlayer.RaiseFailed(new Exception("late"));
            playback.Toggle("new", new MemoryStream([2]));
            while (queue.TryDequeue(out var action)) action();
            AssertEqual("new", playback.PreviewingVoiceId, "Queued events from a disposed player must not reset a new player");
            return Task.CompletedTask;
        });

    internal static Task AcceptedPreviewSurvivesResultReplacementCaseAsync() =>
        PreviewCaseFixtureOwner.RunAsync(async owner =>
        {
            var network = owner.Scope(OverrideNetworkAvailability(true));
            var scenario = owner.Own(new PreviewScenario(true));
            await PlayOwnedPreviewAsync(owner, scenario, true);
            scenario.ViewModel.SearchResults = [];
            AssertEqual(scenario.Id, scenario.ViewModel.PreviewingVoiceId, "Search replacement retains accepted playback independently of its old presentation");
        });
}
