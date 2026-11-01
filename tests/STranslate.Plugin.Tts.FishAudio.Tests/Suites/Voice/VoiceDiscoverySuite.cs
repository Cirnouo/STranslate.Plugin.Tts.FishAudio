using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.Presentation;
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

[NUnit.Framework.TestFixture]
[NUnit.Framework.NonParallelizable]
internal static class VoiceDiscoverySuite
{
    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/by-id-timer-creation-cannot-outlive-concurrent-retirement")]
    [NUnit.Framework.Order(203)]
    public static async Task ByIdTimerCreationCannotOutliveConcurrentRetirementAsync()
    {
        // Installing a timer without rechecking its reserved owner after CreateTimer must fail.
        foreach (var retirement in new[] { "dispose", "replacement" })
        {
            using var clock = new InterceptingTimeProvider();
            using var releaseCreation = new ManualResetEventSlim();
            var creationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clock.BeforeNextTimerReturns = () =>
            {
                creationEntered.SetResult();
                AssertEqual(true, releaseCreation.Wait(RegressionExecution.Timeout), "The test must release the same paused timer creation");
            };
            var settings = new Settings();
            using var view = CreateTimedDiscoveryView(CreateContext(settings: settings), settings, clock);
            var lookup = Task.Run(() => view.SubmitVoiceIdCommand.ExecuteAsync(null));
            var lateErrorChanges = 0;
            try
            {
                await creationEntered.Task.WaitAsync(RegressionExecution.Timeout);
                if (retirement == "dispose")
                    view.Dispose();
                else
                {
                    view.VoiceIdInput = "bad";
                    await view.SubmitVoiceIdCommand.ExecuteAsync(null);
                }
                view.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(view.VoiceIdError)) lateErrorChanges++; };
            }
            finally
            {
                releaseCreation.Set();
                await lookup.WaitAsync(RegressionExecution.Timeout);
            }
            AssertEqual(true, clock.Timers[0].IsDisposed, "A timer whose creation lost ownership must be disposed");
            AssertEqual(0, lateErrorChanges, "A timer creation finishing after retirement cannot publish an old error");
            if (retirement == "dispose")
                AssertEqual<string?>(null, view.VoiceIdError, "Completed disposal must reject an in-flight error publication");
            else
            {
                AssertEqual("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat", view.VoiceIdError, "Concurrent replacement owns the visible error");
                AssertEqual(false, clock.Timers[1].IsDisposed, "Rejecting an old timer cannot retire the replacement timer");
                clock.Advance(TimeSpan.FromMilliseconds(4999));
                AssertNotNull(view.VoiceIdError, "The replacement retains its full five-second lifetime");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                AssertEqual<string?>(null, view.VoiceIdError, "The replacement still expires on its own timer");
            }
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/reentrant-by-id-timer-creation-cannot-replace-its-successor")]
    [NUnit.Framework.Order(204)]
    public static async Task ReentrantByIdTimerCreationCannotReplaceItsSuccessorAsync()
    {
        // A TimeProvider may reenter before returning; the outer candidate must not overwrite its successor.
        using var clock = new InterceptingTimeProvider();
        var settings = new Settings();
        using var view = CreateTimedDiscoveryView(CreateContext(settings: settings), settings, clock);
        clock.BeforeNextTimerReturns = () =>
        {
            view.VoiceIdInput = "bad";
            view.SubmitVoiceIdCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        };
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(true, clock.Timers[0].IsDisposed, "Reentrant replacement must dispose the outer timer candidate");
        AssertEqual(false, clock.Timers[1].IsDisposed, "The reentrant successor must retain ownership of its timer");
        AssertEqual("STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat", view.VoiceIdError, "The outer occurrence cannot overwrite the reentrant error");
        clock.Advance(TimeSpan.FromSeconds(5));
        AssertEqual<string?>(null, view.VoiceIdError, "The surviving reentrant occurrence expires normally");
        AssertEqual(true, clock.Timers[1].IsDisposed, "Expiry disposes the surviving timer");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/by-id-errors-expire-at-five-seconds")]
    [NUnit.Framework.Order(202)]
    public static async Task ByIdErrorsExpireAtFiveSecondsAsync()
    {
        // Removing expiry, expiring early, or expiring only local validation must fail.
        (string Input, Exception? Failure, string Json, string Error)[] cases =
        [
            ("", null, "", "STranslate_Plugin_Tts_FishAudio_VoiceId_Empty"),
            ("ABCDEF0123456789ABCDEF0123456789", null, "", "STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat"),
            ("abcdef0123456789abcdef0123456789\n", null, "", "STranslate_Plugin_Tts_FishAudio_VoiceId_InvalidFormat"),
            ("11111111111111111111111111111111", new HttpRequestException("missing", null, HttpStatusCode.NotFound), "", "STranslate_Plugin_Tts_FishAudio_Voice_NotFound"),
            ("11111111111111111111111111111111", null, "{\"_id\":\"11111111111111111111111111111111\",\"type\":\"asr\"}", "STranslate_Plugin_Tts_FishAudio_Voice_NotFound"),
            ("11111111111111111111111111111111", new TimeoutException(), "", "STranslate_Plugin_Tts_FishAudio_Request_Timeout"),
            ("11111111111111111111111111111111", new InvalidOperationException("lookup failed"), "", "lookup failed"),
        ];
        foreach (var item in cases)
        {
            var clock = new ManualStreamingTimeProvider();
            var settings = new Settings { VoiceId = "22222222222222222222222222222222", CachedVoice = new CachedVoiceInfo { Title = "Selected" } };
            var (service, http) = TestHttpServiceProxy.Create();
            http.GetException = item.Failure;
            http.GetResponseJson = item.Json;
            var context = CreateContext(settings: settings, httpService: service);
            using var view = CreateTimedDiscoveryView(context, settings, clock);
            view.VoiceIdInput = item.Input;
            var searchCard = new VoiceSearchItem { Id = "33333333333333333333333333333333", Title = "Retained search" };
            view.SearchResults = [searchCard];
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(item.Error, view.VoiceIdError, "Lookup should publish the appropriate current error");
            clock.Advance(TimeSpan.FromMilliseconds(4999));
            AssertEqual(item.Error, view.VoiceIdError, "The error must remain for the full first 4999 ms");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            AssertEqual<string?>(null, view.VoiceIdError, "Only the error text must expire at 5000 ms");
            AssertEqual(item.Input, view.VoiceIdInput, "Expiry must preserve the raw input draft");
            AssertEqual(true, ReferenceEquals(searchCard, view.SearchResults.Single()), "Expiry must retain existing search cards");
            AssertEqual("22222222222222222222222222222222", settings.VoiceId, "Expiry must preserve selection");
            AssertEqual("Selected", settings.CachedVoice?.Title, "Expiry must preserve selected metadata");
            AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Transient errors must not save settings");
            AssertEqual(item.Input.Length == 32 && char.IsDigit(item.Input[0]) ? 1 : 0, http.GetCallCount, "Raw invalid IDs must remain rejected without a request");
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/identical-by-id-errors-restart-their-lifetime")]
    [NUnit.Framework.Order(205)]
    public static async Task IdenticalByIdErrorsRestartTheirLifetimeAsync()
    {
        // A text-equality guard or an old timer clearing a replacement must fail.
        var clock = new ManualStreamingTimeProvider();
        var settings = new Settings();
        using var view = CreateTimedDiscoveryView(CreateContext(settings: settings), settings, clock);
        view.VoiceIdInput = "invalid";
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        var error = view.VoiceIdError;
        clock.Advance(TimeSpan.FromSeconds(4));
        await view.SubmitVoiceIdCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        AssertEqual(error, view.VoiceIdError, "The first occurrence's deadline must not clear an identical replacement");
        clock.Advance(TimeSpan.FromMilliseconds(3999));
        AssertEqual(error, view.VoiceIdError, "An identical replacement receives its own full lifetime");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        AssertEqual<string?>(null, view.VoiceIdError, "The replacement must expire at its own five-second deadline");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/edits-and-new-by-id-lookups-clear-errors-immediately")]
    [NUnit.Framework.Order(206)]
    public static Task EditsAndNewByIdLookupsClearErrorsImmediatelyAsync() =>
        RunEditsAndNewByIdLookupsClearErrorsImmediatelyAsync();

    internal static async Task RunEditsAndNewByIdLookupsClearErrorsImmediatelyAsync(
        Action<NUnitCompatibility.PendingLookupOwnershipObservation>? onHeldWork = null)
    {
        SettingsViewModel? ownedView = null;
        TaskCompletionSource<string>? ownedResponse = null;
        Task? ownedLookup = null;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            // Omitting edit/lookup retirement must fail before fake time advances.
            var clock = new ManualStreamingTimeProvider();
            var settings = new Settings();
            var (service, http) = TestHttpServiceProxy.Create();
            var view = CreateTimedDiscoveryView(CreateContext(settings: settings, httpService: service), settings, clock);
            ownedView = view;
            view.VoiceIdInput = "bad";
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            view.VoiceIdInput = "also bad";
            AssertEqual<string?>(null, view.VoiceIdError, "Editing an invalid draft must immediately clear its former error");
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            view.VoiceIdInput = "11111111111111111111111111111111";
            http.GetException = new TimeoutException();
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertNotNull(view.VoiceIdError, "The failed request should publish an error before replacement");
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ownedResponse = pending;
            http.GetAsyncHandler = (_, _, _) => pending.Task;
            var lookup = view.SubmitVoiceIdCommand.ExecuteAsync(null);
            ownedLookup = lookup;
            onHeldWork?.Invoke(new NUnitCompatibility.PendingLookupOwnershipObservation(
                lookup, pending.Task,
                () => { pending.TrySetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Found\"}"); }, view, http.GetCallCount));
            AssertEqual<string?>(null, view.VoiceIdError, "A new lookup must clear the previous error while pending");
            pending.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Found\"}");
            await lookup.WaitAsync(RegressionExecution.Timeout);
            var card = view.ByIdResults.Single();
            clock.Advance(TimeSpan.FromSeconds(5));
            AssertEqual<string?>(null, view.VoiceIdError, "Successful lookup must remain error-free");
            AssertEqual(true, ReferenceEquals(card, view.ByIdResults.Single()), "An old error deadline must preserve the successful result object");
            AssertEqual("11111111111111111111111111111111", view.VoiceIdInput, "Successful lookup retains the input");
            AssertEqual("", settings.VoiceId, "Successful lookup still requires explicit Apply");
        },
        () => { ownedView?.Dispose(); return Task.CompletedTask; },
        () => { ownedResponse?.TrySetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Found\"}"); return Task.CompletedTask; },
        () => ownedLookup ?? Task.CompletedTask);
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/deferred-by-id-expiry-rechecks-occurrence-and-disposal")]
    [NUnit.Framework.Order(207)]
    public static async Task DeferredByIdExpiryRechecksOccurrenceAndDisposalAsync()
    {
        // A generation check before dispatch alone permits a queued old expiry to clear new text.
        foreach (var retirement in new[] { "identical", "different", "edit", "dispose" })
        {
            var clock = new ManualStreamingTimeProvider();
            var settings = new Settings();
            using var view = CreateTimedDiscoveryView(CreateContext(settings: settings), settings, clock);
            view.VoiceIdInput = "bad";
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            var queued = new List<Action>();
            var previousInvoker = SettingsViewModel.UiThreadInvokerOverride;
            try
            {
                SettingsViewModel.UiThreadInvokerOverride = queued.Add;
                await Task.Run(() => clock.Advance(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                SettingsViewModel.UiThreadInvokerOverride = previousInvoker;
            }
            AssertEqual(1, queued.Count, "The background timer must marshal one expiry to the UI boundary");
            switch (retirement)
            {
                case "identical":
                    await view.SubmitVoiceIdCommand.ExecuteAsync(null);
                    break;
                case "different":
                    view.VoiceIdInput = "";
                    await view.SubmitVoiceIdCommand.ExecuteAsync(null);
                    break;
                case "edit":
                    view.VoiceIdInput = "edited";
                    break;
                case "dispose":
                    view.Dispose();
                    break;
            }
            var expected = view.VoiceIdError;
            var errorChanges = 0;
            view.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(view.VoiceIdError)) errorChanges++; };
            queued.Single()();
            AssertEqual(expected, view.VoiceIdError, "A queued expired occurrence cannot publish after replacement, edit or disposal");
            AssertEqual(0, errorChanges, "A retired queued expiry must not notify the disposed/replaced facade");
            if (retirement is "identical" or "different")
            {
                clock.Advance(TimeSpan.FromMilliseconds(4999));
                AssertEqual(expected, view.VoiceIdError, "The queued older deadline cannot shorten the new occurrence");
                clock.Advance(TimeSpan.FromMilliseconds(1));
                AssertEqual<string?>(null, view.VoiceIdError, "The current occurrence must still expire normally");
            }
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/by-id-disposal-retires-timer-and-late-responses")]
    [NUnit.Framework.Order(208)]
    public static async Task ByIdDisposalRetiresTimerAndLateResponsesAsync()
    {
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            // Missing timer disposal or request-owner checks must fail at the observable facade.
            foreach (var retirement in new[] { "edit", "replacement", "credentials", "dispose" })
            foreach (var fail in new[] { false, true })
            {
                SettingsViewModel? ownedView = null;
                TaskCompletionSource<string>? ownedResponse = null;
                Task? ownedLookup = null;
                await RegressionFixtureOwner.RunAsync(async () =>
                {
                    var clock = new ManualStreamingTimeProvider();
                    var settings = new Settings();
                    var (service, http) = TestHttpServiceProxy.Create();
                    var view = CreateTimedDiscoveryView(CreateContext(settings: settings, httpService: service), settings, clock);
                    ownedView = view;
                    var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ownedResponse = pending;
                    CancellationToken requestToken = default;
                    http.GetAsyncHandler = (_, _, token) => { requestToken = token; return pending.Task; }; // Intentionally ignores cancellation.
                    view.VoiceIdInput = "11111111111111111111111111111111";
                    var lookup = view.SubmitVoiceIdCommand.ExecuteAsync(null);
                    ownedLookup = lookup;
                    switch (retirement)
                    {
                        case "edit": view.VoiceIdInput = "edited"; break;
                        case "replacement":
                            http.GetAsyncHandler = null;
                            http.GetException = new TimeoutException();
                            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
                            break;
                        case "credentials": view.ApiKey = "new-account"; break;
                        case "dispose": view.Dispose(); break;
                    }
                    AssertEqual(true, requestToken.IsCancellationRequested, "Retiring lookup ownership must propagate cancellation");
                    var expectedError = view.VoiceIdError;
                    if (fail) pending.SetException(new InvalidOperationException("late failure"));
                    else pending.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Late\"}");
                    await lookup.WaitAsync(RegressionExecution.Timeout);
                    AssertEqual(expectedError, view.VoiceIdError, "Cancellation-ignoring completion cannot republish an error");
                    AssertEqual(0, view.ByIdResults.Count, "Cancellation-ignoring completion cannot republish a card");
                    clock.Advance(TimeSpan.FromSeconds(5));
                    AssertEqual<string?>(null, view.VoiceIdError, "Only a current replacement error may expire");
                },
                () => { ownedView?.Dispose(); return Task.CompletedTask; },
                () =>
                {
                    if (ownedLookup is null) return Task.CompletedTask;
                    if (fail) ownedResponse?.TrySetException(new InvalidOperationException("late failure"));
                    else ownedResponse?.TrySetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Late\"}");
                    return Task.CompletedTask;
                },
                () => ownedLookup ?? Task.CompletedTask);
            }

            var disposalClock = new ManualStreamingTimeProvider();
            var disposalSettings = new Settings();
            using var disposedView = CreateTimedDiscoveryView(CreateContext(settings: disposalSettings), disposalSettings, disposalClock);
            await disposedView.SubmitVoiceIdCommand.ExecuteAsync(null);
            disposedView.Dispose();
            var notifications = 0;
            disposedView.PropertyChanged += (_, _) => notifications++;
            var previous = SettingsViewModel.UiThreadInvokerOverride;
            var dispatches = 0;
            try
            {
                SettingsViewModel.UiThreadInvokerOverride = _ => dispatches++;
                disposalClock.Advance(TimeSpan.FromSeconds(10));
            }
            finally { SettingsViewModel.UiThreadInvokerOverride = previous; }
            AssertEqual(0, dispatches, "Disposal must dispose its timer rather than leave a queued UI callback");
            AssertEqual(0, notifications, "Timer retirement must leave a disposed facade untouched");
        });
    }

    private static SettingsViewModel CreateTimedDiscoveryView(IPluginContext context, Settings settings, TimeProvider clock) =>
        new(context, settings, null, null, voiceIdErrorTimeProvider: clock);

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-and-by-id-use-configured-token-or-dummy")]
    [NUnit.Framework.Order(190)]
    public static async Task SearchAndByIdUseConfiguredTokenOrDummyAsync()
    {
        foreach (var key in new[] { AppliedKey, "unvalidated-configured-key", "", " \t" })
        {
            var settings = new Settings { ApiKey = key };
            var (httpService, http) = TestHttpServiceProxy.Create();
            http.GetResponseJson = "{\"total\":0,\"items\":[]}";
            using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
            {
                SearchQuery = "voice",
            };

            await viewModel.SearchVoicesCommand.ExecuteAsync(null);

            var headers = AssertHeaders(http.LastGetOptions, "Voice search should send Authorization headers");
            AssertEqual($"Bearer {(string.IsNullOrWhiteSpace(key) ? "dummy" : key)}", headers["Authorization"], "Voice search should use the configured value or dummy only when blank");

            http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
            viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";

            await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);

            headers = AssertHeaders(http.LastGetOptions, "Voice by-ID lookup should send Authorization headers");
            AssertEqual($"Bearer {(string.IsNullOrWhiteSpace(key) ? "dummy" : key)}", headers["Authorization"], "Voice lookup should preserve the configured value without TTS validation");
            AssertEqual(1, viewModel.ByIdResults.Count, "Public By-ID lookup should expose one card for inspection");
            AssertEqual("", settings.VoiceId, "Public metadata lookup should leave selection to Apply");
        }
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/credential-changes-preserve-display-and-invalidate-pending-search")]
    [NUnit.Framework.Order(200)]
    public static async Task CredentialChangesPreserveDisplayAndInvalidatePendingSearchAsync()
    {
        SettingsViewModel? ownedView = null;
        TaskCompletionSource<string>? ownedResponse = null;
        Task? ownedSearch = null;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var settings = new Settings { ApiKey = "first-account" };
            var (service, http) = TestHttpServiceProxy.Create();
            var view = new SettingsViewModel(CreateContext(settings: settings, httpService: service), settings);
            ownedView = view;
            const string id = "fedcba9876543210fedcba9876543210";
            http.GetResponseJson = "{\"_id\":\"" + id + "\",\"title\":\"Private voice\"}";
            view.VoiceIdInput = id;
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            view.ToggleFavoriteCommand.Execute(view.ByIdResults.Single());
            view.SelectVoiceCommand.Execute(view.ByIdResults.Single());
            var calls = http.GetCallCount;
            view.ApiKey = "second-account";
            AssertEqual(id, settings.VoiceId, "Key edits retain selected voice identity");
            AssertEqual("Private voice", settings.CachedVoice?.Title, "Key edits retain the selected display snapshot");
            AssertEqual("Private voice", view.ByIdResults.Single().Title, "Changing credentials retains the inspected detail card");
            await view.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(calls + 1, http.GetCallCount, "The new account must not reuse the old metadata cache");
            AssertEqual("Bearer second-account", AssertHeaders(http.LastGetOptions, "New credential headers")["Authorization"], "New lookup uses new credentials");

            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ownedResponse = pending;
            http.GetAsyncHandler = (_, _, _) => pending.Task; // Deliberately ignores cancellation.
            var search = view.SearchVoicesCommand.ExecuteAsync(null);
            ownedSearch = search;
            view.ApiKey = "";
            pending.SetResult("{\"total\":1,\"items\":[{\"_id\":\"" + id + "\",\"title\":\"Late private voice\"}]}");
            await search;
            AssertEqual(0, view.SearchResults.Count, "An old credential response must not republish private cards");
            http.GetAsyncHandler = null;
            http.GetException = new HttpRequestException("rejected", null, HttpStatusCode.Unauthorized);
            view.ApiKey = "rejected-key";
            calls = http.GetCallCount;
            await view.SearchVoicesCommand.ExecuteAsync(null);
            AssertEqual(calls + 1, http.GetCallCount, "Rejected configured credentials must not fall back to dummy");
            AssertEqual("Bearer rejected-key", AssertHeaders(http.LastGetOptions, "Rejected key headers")["Authorization"], "Rejected key is forwarded once");
        },
        () => { ownedView?.Dispose(); return Task.CompletedTask; },
        () => { ownedResponse?.TrySetResult("{\"total\":1,\"items\":[{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Late private voice\"}]}"); return Task.CompletedTask; },
        () => ownedSearch ?? Task.CompletedTask);
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/voice-lookup-requests-use-timeout-and-preserve-failure-semantics")]
    [NUnit.Framework.Order(209)]
    public static async Task VoiceLookupRequestsUseTimeoutAndPreserveFailureSemanticsAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        var snackbar = new TestSnackbar();
        var context = CreateContext(snackbar, settings, httpService);
        using var viewModel = new SettingsViewModel(context, settings)
        {
            SearchQuery = "voice",
        };

        http.GetResponseJson = "{\"total\":0,\"items\":[]}";
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice search should set a 15 second timeout");

        http.GetResponseJson = "{\"_id\":\"fedcba9876543210fedcba9876543210\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
        viewModel.VoiceIdInput = "fedcba9876543210fedcba9876543210";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(TimeSpan.FromSeconds(15), http.LastGetOptions?.Timeout, "Voice by-ID lookup should set a 15 second timeout");

        http.GetException = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound);
        viewModel.VoiceIdInput = "00000000000000000000000000000000";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Voice_NotFound",
            viewModel.VoiceIdError,
            "Only HTTP 404 should be shown as voice not found");

        http.GetException = new TimeoutException("lookup timed out");
        viewModel.VoiceIdInput = "11111111111111111111111111111111";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "STranslate_Plugin_Tts_FishAudio_Request_Timeout",
            viewModel.VoiceIdError,
            "Voice by-ID timeout should show the localized timeout error instead of not found");

        http.GetException = new InvalidOperationException("server unavailable");
        viewModel.VoiceIdInput = "22222222222222222222222222222222";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual("server unavailable", viewModel.VoiceIdError, "Non-404 lookup failures should preserve failure details");

        http.GetException = null;
        http.GetResponseJson = "null";
        viewModel.VoiceIdInput = "44444444444444444444444444444444";
        await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
        AssertEqual(
            "Fish Audio model lookup returned an empty response.",
            viewModel.VoiceIdError,
            "Successful non-404 lookup responses that deserialize to null should be treated as response errors");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/voice-lookup-requests-cancel-previous-and-dispose-work")]
    [NUnit.Framework.Order(210)]
    public static Task VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync() => RunVoiceLookupRequestsCancelPreviousAndDisposeWorkAsync();

    internal static Task RunVoiceLookupRequestsCancelPreviousAndDisposeWorkAsync(Action<DiscoveryRemainderFixtureOwner, string>? onHeldWork = null)
    {
        var owned = new DiscoveryRemainderFixtureOwner();
        return owned.RunAsync(async () =>
        {
            var settings = new Settings();
            var (httpService, http) = TestHttpServiceProxy.Create();
            var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
            owned.Track(viewModel);
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecond = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owned.Track(releaseSecond, "{\"total\":0,\"items\":[]}");
            var calls = 0;

            http.GetAsyncHandler = (_, _, ct) =>
            {
                calls++;
                if (calls == 1)
                {
                    firstStarted.SetResult();
                    var request = Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
                    {
                        if (task.IsCanceled)
                            throw new OperationCanceledException(ct);
                        return "{\"total\":0,\"items\":[]}";
                    }, CancellationToken.None);
                    return owned.TrackRequest(request, ct);
                }

                return releaseSecond.Task;
            };

            var firstSearch = owned.TrackOperation(viewModel.SearchVoicesCommand.ExecuteAsync(null));
            await firstStarted.Task.WaitAsync(RegressionExecution.Timeout);
            onHeldWork?.Invoke(owned, "first-search-held");
            var secondSearch = owned.TrackOperation(viewModel.SearchVoicesCommand.ExecuteAsync(null));
            releaseSecond.SetResult("{\"total\":0,\"items\":[]}");

            await secondSearch.WaitAsync(RegressionExecution.Timeout);
            await firstSearch.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(2, calls, "A new voice search should start even when a previous search is still pending");
            AssertEqual(false, viewModel.IsSearching, "Search busy state should recover after replacing a pending search");

            var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            http.GetAsyncHandler = (_, _, ct) =>
            {
                byIdStarted.SetResult();
                var request = Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith<string>(task =>
                {
                    if (task.IsCanceled)
                        throw new OperationCanceledException(ct);
                    return "{\"_id\":\"33333333333333333333333333333333\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}";
                }, CancellationToken.None);
                return owned.TrackRequest(request, ct);
            };

            viewModel.VoiceIdInput = "33333333333333333333333333333333";
            var submitTask = owned.TrackOperation(viewModel.SubmitVoiceIdCommand.ExecuteAsync(null));
            await byIdStarted.Task.WaitAsync(RegressionExecution.Timeout);
            onHeldWork?.Invoke(owned, "by-id-held");
            viewModel.Dispose();
            await submitTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing the view model should cancel by-ID lookup and restore busy state");
        });
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/voice-lookup-completions-after-dispose-do-not-mutate-state")]
    [NUnit.Framework.Order(211)]
    public static Task VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync() => RunVoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync();

    internal static Task RunVoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync(Action<DiscoveryRemainderFixtureOwner, string>? onHeldWork = null)
    {
        var owned = new DiscoveryRemainderFixtureOwner();
        return owned.RunAsync(async () =>
        {
            var settings = new Settings();
            var (httpService, http) = TestHttpServiceProxy.Create();
            var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
            {
                SearchQuery = "late",
            };
            owned.Track(viewModel);
            var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSearch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owned.Track(releaseSearch, "{\"total\":1,\"items\":[{\"_id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"Late Search\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}");
            http.GetAsyncHandler = (_, _, _) =>
            {
                searchStarted.SetResult();
                return releaseSearch.Task;
            };

            var searchTask = owned.TrackOperation(viewModel.SearchVoicesCommand.ExecuteAsync(null));
            await searchStarted.Task.WaitAsync(RegressionExecution.Timeout);
            onHeldWork?.Invoke(owned, "search-held");
            viewModel.Dispose();
            releaseSearch.SetResult("{\"total\":1,\"items\":[{\"_id\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"title\":\"Late Search\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}");
            await searchTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(false, viewModel.IsSearching, "Disposing during search should restore search busy state");
            AssertEqual(false, viewModel.HasSearched, "Search completion after dispose should not mark search as completed");
            AssertEqual(0, viewModel.SearchResults.Count, "Search completion after dispose should not apply stale results");

            const string originalVoiceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            settings = new Settings
            {
                VoiceId = originalVoiceId,
                CachedVoice = new CachedVoiceInfo { Title = "Existing Voice" },
            };
            (httpService, http) = TestHttpServiceProxy.Create();
            var context = CreateContext(settings: settings, httpService: httpService);
            var proxy = (ContextProxy)(object)context;
            viewModel = new SettingsViewModel(context, settings)
            {
                VoiceIdInput = "cccccccccccccccccccccccccccccccc",
            };
            owned.Track(viewModel);
            var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseById = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owned.Track(releaseById, "{\"_id\":\"cccccccccccccccccccccccccccccccc\",\"title\":\"Late Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            http.GetAsyncHandler = (_, _, _) =>
            {
                byIdStarted.SetResult();
                return releaseById.Task;
            };

            var submitTask = owned.TrackOperation(viewModel.SubmitVoiceIdCommand.ExecuteAsync(null));
            await byIdStarted.Task.WaitAsync(RegressionExecution.Timeout);
            onHeldWork?.Invoke(owned, "by-id-held");
            viewModel.Dispose();
            releaseById.SetResult("{\"_id\":\"cccccccccccccccccccccccccccccccc\",\"title\":\"Late Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            await submitTask.WaitAsync(RegressionExecution.Timeout);

            AssertEqual(false, viewModel.IsSubmittingVoiceId, "Disposing during by-ID lookup should restore submit busy state");
            AssertEqual(0, viewModel.ByIdResults.Count, "By-ID completion after disposal must not publish an Apply card");
            AssertEqual(originalVoiceId, settings.VoiceId, "By-ID completion after dispose should not update saved voice ID");
            AssertEqual("Existing Voice", settings.CachedVoice?.Title, "By-ID completion after dispose should not update cached voice");
            AssertEqual(0, proxy.SaveCount, "Dispose and late by-ID completion should not issue redundant or stale saves");
        });
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-pagination-honors-server-window")]
    [NUnit.Framework.Order(213)]
    public static async Task SearchPaginationHonorsServerWindowAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = """
            {"total":1000000,"total_is_exact":false,"max_offset":1000,
             "accessible_upper_bound":1000,"window_limited":true,"has_more":true,
             "items":[{"_id":"11111111111111111111111111111111","type":"tts","title":"Windowed Voice"}]}
            """;
        using var viewModel = new SettingsViewModel(
            CreateContext(settings: settings, httpService: httpService), settings);

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);

        AssertEqual(167, viewModel.SearchTotalPages,
            "An approximate total must not grant access beyond the server's 1000-record window");
        AssertEqual("≈1000000", viewModel.SearchResultCountText, "The reported approximate total must not be presented as exact");
        AssertEqual("≤167", viewModel.SearchPageLimitText, "The accessible page cap must not be presented as an exact page count");
        AssertEqual(true, viewModel.ShowNextPage, "A permitted next page should remain visible within the server window");
        var calls = http.GetCallCount;
        viewModel.PageInput = "168";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "An out-of-window page must not issue an HTTP request");
        AssertEqual("1", viewModel.PageInput, "An out-of-window page should restore the visible page input");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/pagination-metadata-preserves-legacy-and-unknown-totals")]
    [NUnit.Framework.Order(214)]
    public static void PaginationMetadataPreservesLegacyAndUnknownTotals()
    {
        (string Json, int Page, int? Total, bool Exact, int? LastPage, bool Next, bool Limited)[] cases =
        [
            ("""{"total":12,"items":[{}]}""", 1, 12, true, 2, true, false),
            ("""{"total":0,"items":[]}""", 1, 0, true, 1, false, false),
            ("""{"items":[{},{},{},{},{},{}]}""", 1, null, false, null, true, false),
            ("""{"items":[{}]}""", 1, null, false, 1, false, false),
            ("""{"total":1000000,"total_is_exact":false,"has_more":true,"items":[{}]}""", 1, 1000000, false, null, true, false),
            ("""{"total":1000000,"total_is_exact":false,"items":[{}]}""", 1, 1000000, false, 1, false, false),
            ("""{"total":1000000,"max_offset":1000,"accessible_upper_bound":1002,"window_limited":true,"total_is_exact":false,"has_more":true,"items":[{}]}""", 1, 1000000, false, 167, true, true),
            ("""{"max_offset":1000,"accessible_upper_bound":1000,"has_more":true,"items":[{},{},{},{}]}""", 167, null, false, 167, false, false),
            ("""{"total":200,"total_is_exact":true,"max_offset":1000,"accessible_upper_bound":200,"has_more":true,"items":[{}]}""", 1, 200, true, 34, true, false),
            ("""{"total":2000,"total_is_exact":true,"max_offset":1000,"has_more":true,"items":[{}]}""", 1, 2000, true, 167, true, true),
            ("""{"total":2000,"total_is_exact":true,"accessible_upper_bound":1000,"window_limited":false,"has_more":true,"items":[{}]}""", 1, 2000, true, 167, true, true),
            ("""{"total":200,"total_is_exact":true,"has_more":false,"items":[{}]}""", 2, 200, true, 2, false, false),
            ("""{"has_more":true,"items":[]}""", 2, null, false, 2, false, false),
            ("""{"has_more":true,"items":null}""", 2, null, false, 2, false, false),
            ("""{"total":null,"max_offset":null,"accessible_upper_bound":null,"has_more":null,"items":[{}]}""", 1, null, false, 1, false, false),
            ("""{"total":-1,"max_offset":-1,"accessible_upper_bound":-1,"items":[{},{},{},{},{},{}]}""", 1, null, false, null, true, false),
            ("""{"total":2147483647,"total_is_exact":true,"max_offset":2147483647,"items":[{}]}""", 357913941, 2147483647, true, 357913942, true, false),
        ];

        foreach (var test in cases)
        {
            var response = JsonSerializer.Deserialize<ModelListResponse>(test.Json)!;
            var pagination = VoiceSearchPagination.FromResponse(response, test.Page, 6);
            AssertEqual(test.Total, pagination.ReportedTotal, $"Reported total should preserve optionality: {test.Json}");
            AssertEqual(test.Exact, pagination.IsTotalExact, $"A total must only be exact when the response permits it: {test.Json}");
            AssertEqual(test.LastPage, pagination.LastAccessiblePage, $"Accessible pages should respect the response bounds: {test.Json}");
            AssertEqual(test.Next, pagination.HasNextPage, $"Next should respect explicit metadata and raw page contents: {test.Json}");
            AssertEqual(test.Limited, pagination.IsWindowLimited, $"An explicit or inferred accessibility window should be preserved: {test.Json}");
        }

        var unknown = VoiceSearchPagination.FromResponse(
            JsonSerializer.Deserialize<ModelListResponse>("""{"has_more":true,"items":[{}]}""")!, 3, 6);
        AssertEqual(true, unknown.CanRequestPage(1), "An unknown total should still permit returning to an earlier page");
        AssertEqual(true, unknown.CanRequestPage(4), "An explicit next page may be requested without an exact total");
        AssertEqual(false, unknown.CanRequestPage(5), "An unknown total must not grant arbitrary forward jumps");
        AssertEqual(true, unknown.CanRequestPage(7, highestVisitedPage: 8), "Previously visited pages should remain reachable with an unknown total");
        AssertEqual(false, unknown.CanRequestPage(0), "Page zero must not be requested");
        AssertEqual(false, unknown.CanRequestPage(int.MaxValue), "Large input must not overflow the navigation boundary");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-window-tail-and-explicit-has-more")]
    [NUnit.Framework.Order(215)]
    public static async Task SearchWindowTailAndExplicitHasMoreAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = CreateSearchResponse(
            new { total = 1000000, total_is_exact = false, max_offset = 1000, accessible_upper_bound = 1000, has_more = true },
            new { _id = "first", title = "First" });
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);

        http.GetResponseJson = CreateSearchResponse(
            new { total = 1000000, total_is_exact = false, max_offset = 1000, accessible_upper_bound = 1000, has_more = true },
            new { _id = "a", title = "Tail 1" }, new { _id = "b", title = "Tail 2" },
            new { _id = "c", title = "Tail 3" }, new { _id = "d", title = "Tail 4" });
        viewModel.PageInput = "167";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(167, viewModel.SearchPage, "The partial page at offset 996 must remain accessible");
        AssertEqual(4, viewModel.SearchResults.Count, "A partial last window page must retain all returned voices");
        AssertEqual(true, http.LastGetUrl!.Contains("page_number=167", StringComparison.Ordinal), "The window tail should be requested directly");
        AssertEqual(false, viewModel.ShowNextPage, "A stale has_more=true must not permit leaving the server window");
        viewModel.SearchTotalPages = 999;
        AssertEqual(false, viewModel.ShowNextPage, "The compatibility page-count property must not override the applied navigation policy");
        var calls = http.GetCallCount;
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "Next at the window boundary must not issue HTTP");

        http.GetResponseJson = CreateSearchResponse(
            new { total = 1000000, total_is_exact = false, max_offset = 1000, has_more = false },
            new { _id = "stopped", title = "Stopped" });
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(false, viewModel.ShowNextPage, "An explicit server end must stop next-page navigation");
        calls = http.GetCallCount;
        viewModel.PageInput = "2";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "An explicit server end must also reject a forward page jump");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-unknown-totals-and-raw-type-filtering")]
    [NUnit.Framework.Order(216)]
    public static async Task SearchUnknownTotalsAndRawTypeFilteringAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = CreateSearchResponse(new { },
            new { _id = "a", type = "tts", title = "TTS" },
            new { _id = "b", title = "Legacy" },
            new { _id = "c", type = " ", title = "Blank" },
            new { _id = "d", type = "asr", title = "ASR" },
            new { _id = "e", type = "future", title = "Unknown" },
            new { _id = "f", type = "TTS", title = "TTS case" });
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEnumerableEqual(new[] { "TTS", "Legacy", "Blank", "TTS case" },
            viewModel.SearchResults.Select(item => item.Title), "Only explicit non-TTS records should be removed from a mixed page");
        AssertEqual(true, viewModel.ShowNextPage, "The full raw page should permit Next despite filtering down to four cards");
        AssertEqual("?", viewModel.SearchResultCountText, "An absent total must not be displayed as zero results");
        AssertEqual("?", viewModel.SearchPageLimitText, "An unknown page extent must not be displayed as an exact total");

        var calls = http.GetCallCount;
        viewModel.PageInput = "3";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "An unknown total should not allow jumping beyond the known next page");

        http.GetResponseJson = CreateSearchResponse(new { },
            Enumerable.Range(1, 6).Select(index => (object)new { _id = index.ToString(), type = "asr", title = "ASR" }).ToArray());
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(2, viewModel.SearchPage, "The next raw page should still load");
        AssertEqual(0, viewModel.SearchResults.Count, "An all-ASR page must contain no selectable voice cards");
        AssertEqual(true, viewModel.ShowNextPage, "Filtering every card must not mark a full raw page as the server end");

        http.GetResponseJson = CreateSearchResponse(new { }, new { _id = "last", title = "Last" });
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(3, viewModel.SearchPage, "A short unknown-total page should be published");
        AssertEqual(false, viewModel.ShowNextPage, "A short raw page with no has_more should stop forward navigation");

        http.GetResponseJson = CreateSearchResponse(new { },
            Enumerable.Range(1, 6).Select(index => (object)new { _id = index.ToString(), title = "Voice" }).ToArray());
        viewModel.PageInput = "1";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        viewModel.PageInput = "3";
        await viewModel.CommitPageInputCommand.ExecuteAsync(null);
        AssertEqual(3, viewModel.SearchPage, "Returning to an earlier unknown-total page must not forget previously visited pages");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-committed-query-survives-edits-and-failure")]
    [NUnit.Framework.Order(217)]
    public static async Task SearchCommittedQuerySurvivesEditsAndFailureAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = CreateSearchResponse(new { total = 18 }, new { _id = "voice", title = "Voice" });
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
        {
            SearchQuery = "first & query",
        };
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        viewModel.SearchQuery = "uncommitted";
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(true, http.LastGetUrl!.Contains("title=first%20%26%20query", StringComparison.Ordinal),
            "Editing the search box must not change the query of an existing result set");

        http.GetException = new TimeoutException("new query failed");
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(2, viewModel.SearchPage, "A failed replacement query should retain the old page");
        http.GetException = null;
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(3, viewModel.SearchPage, "The old result set should remain navigable after a failed replacement");
        AssertEqual(true, http.LastGetUrl!.Contains("title=first%20%26%20query", StringComparison.Ordinal),
            "A failed new query must not replace the last committed query snapshot");

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(1, viewModel.SearchPage, "A successful replacement query should reset the page");
        AssertEqual(true, http.LastGetUrl!.Contains("title=uncommitted", StringComparison.Ordinal),
            "An explicit successful search should commit the new query");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-empty-page-stops-forward-navigation")]
    [NUnit.Framework.Order(218)]
    public static async Task SearchEmptyPageStopsForwardNavigationAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = CreateSearchResponse(new { has_more = true }, new { _id = "voice", title = "Voice" });
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);
        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        http.GetResponseJson = """{"total":1000000,"total_is_exact":false,"has_more":true,"items":[]}""";
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(2, viewModel.SearchPage, "A successful empty page should be published once");
        AssertEqual(0, viewModel.SearchResults.Count, "A successful empty page should clear the previous cards");
        AssertEqual(false, viewModel.ShowNextPage, "An empty raw page must stop Next even if the metadata claims more");
        var calls = http.GetCallCount;
        await viewModel.NextPageCommand.ExecuteAsync(null);
        AssertEqual(calls, http.GetCallCount, "An inconsistent empty page must not trigger an unbounded request loop");

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);
        AssertEqual(1, viewModel.SearchPage, "A new empty query should reset the visible page to one");
        AssertEqual(0, viewModel.SearchResults.Count, "A new empty query must keep previous cards cleared");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-replacement-rejects-late-pagination")]
    [NUnit.Framework.Order(220)]
    public static async Task SearchReplacementRejectsLatePaginationAsync()
    {
        SettingsViewModel? ownedView = null;
        TaskCompletionSource<string>? ownedResponse = null;
        Task? ownedSearch = null;
        await RegressionFixtureOwner.RunAsync(async () =>
        {
            var settings = new Settings();
            var (httpService, http) = TestHttpServiceProxy.Create();
            var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ownedResponse = releaseFirst;
            http.GetAsyncHandler = (url, _, _) =>
            {
                if (url.Contains("title=first", StringComparison.Ordinal))
                {
                    firstStarted.TrySetResult();
                    return releaseFirst.Task;
                }
                return Task.FromResult(CreateSearchResponse(new { total = 12 }, new { _id = "new", title = "New result" }));
            };
            var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings)
            {
                SearchQuery = "first",
            };
            ownedView = viewModel;
            var first = viewModel.SearchVoicesCommand.ExecuteAsync(null);
            ownedSearch = first;
            await firstStarted.Task.WaitAsync(RegressionExecution.Timeout);
            viewModel.SearchQuery = "second";
            await viewModel.SearchVoicesCommand.ExecuteAsync(null);
            releaseFirst.SetResult(CreateSearchResponse(
                new { total = 1000000, total_is_exact = false, max_offset = 1000, has_more = true },
                new { _id = "old", title = "Stale result" }));
            await first.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(2, viewModel.SearchTotalPages, "A replaced response must not overwrite applied pagination");
            AssertEqual("New result", viewModel.SearchResults[0].Title, "A replaced response must not overwrite applied cards");
            viewModel.SearchQuery = "uncommitted edit";
            await viewModel.NextPageCommand.ExecuteAsync(null);
            AssertEqual(true, http.LastGetUrl!.Contains("title=second", StringComparison.Ordinal),
                "A late response must not replace the committed query snapshot");
        },
        () => { ownedView?.Dispose(); return Task.CompletedTask; },
        () => { ownedResponse?.TrySetResult(CreateSearchResponse(new { total = 1000000, total_is_exact = false, max_offset = 1000, has_more = true }, new { _id = "old", title = "Stale result" })); return Task.CompletedTask; },
        () => ownedSearch ?? Task.CompletedTask);
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/by-id-rejects-explicit-non-tts-models")]
    [NUnit.Framework.Order(219)]
    public static async Task ByIdRejectsExplicitNonTtsModelsAsync()
    {
        const string originalVoiceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var settings = new Settings { VoiceId = originalVoiceId };
        var (httpService, http) = TestHttpServiceProxy.Create();
        var context = CreateContext(settings: settings, httpService: httpService);
        using var viewModel = new SettingsViewModel(context, settings)
        {
            VoiceIdInput = "cccccccccccccccccccccccccccccccc",
        };
        foreach (var type in new[] { "asr", "future" })
        {
            http.GetResponseJson = JsonSerializer.Serialize(new { _id = viewModel.VoiceIdInput, type, title = "Not a voice" });
            await viewModel.SubmitVoiceIdCommand.ExecuteAsync(null);
            AssertEqual(originalVoiceId, settings.VoiceId, "An explicit non-TTS model must not replace the saved voice");
            AssertEqual(0, viewModel.ByIdResults.Count, "An explicit non-TTS model must not expose an Apply card");
            AssertEqual("STranslate_Plugin_Tts_FishAudio_Voice_NotFound", viewModel.VoiceIdError,
                "An explicit non-TTS model should be reported as unavailable for voice selection");
        }
        AssertEqual(0, ((ContextProxy)(object)context).SaveCount, "Rejecting non-TTS records must not save settings");
    }

    private static string CreateSearchResponse(object metadata, params object[] items)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(metadata))!;
        body["items"] = JsonSerializer.SerializeToElement(items);
        return JsonSerializer.Serialize(body);
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-pagination-updates-visible-page-after-success-only")]
    [NUnit.Framework.Order(212)]
    public static async Task SearchPaginationUpdatesVisiblePageAfterSuccessOnlyAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Page 1\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":1}]}";
        using var viewModel = new SettingsViewModel(CreateContext(settings: settings, httpService: httpService), settings);

        await viewModel.SearchVoicesCommand.ExecuteAsync(null);

        AssertEqual(1, viewModel.SearchPage, "Initial search should load page 1");
        AssertEqual("1", viewModel.PageInput, "Initial search should show page input 1");
        AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Initial search should display page 1 results");

        http.GetException = new TimeoutException("page 2 timed out");
        await viewModel.NextPageCommand.ExecuteAsync(null);

        AssertEqual(1, viewModel.SearchPage, "Failed next page load should keep previous visible page");
        AssertEqual("1", viewModel.PageInput, "Failed next page load should keep previous page input");
        AssertEqual(2, viewModel.SearchTotalPages, "Failed next page load should keep previous total pages");
        AssertEqual("Page 1", viewModel.SearchResults[0].Title, "Failed next page load should keep previous result cards");

        http.GetException = null;
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"22222222222222222222222222222222\",\"title\":\"Page 2\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":2}]}";
        await viewModel.NextPageCommand.ExecuteAsync(null);

        AssertEqual(2, viewModel.SearchPage, "Successful next page load should update visible page after results arrive");
        AssertEqual("2", viewModel.PageInput, "Successful next page load should update page input after results arrive");
        AssertEqual("Page 2", viewModel.SearchResults[0].Title, "Successful next page load should display new result cards");

        http.GetResponseJson = """{"total":12,"has_more":"invalid","items":[]}""";
        await viewModel.PrevPageCommand.ExecuteAsync(null);
        AssertEqual(2, viewModel.SearchPage, "Malformed pagination metadata must retain the last successful page");
        AssertEqual("2", viewModel.PageInput, "Malformed pagination metadata must retain the last successful page input");
        AssertEqual("Page 2", viewModel.SearchResults[0].Title, "Malformed pagination metadata must retain the last successful cards");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/search-publishes-pagination-before-resolving-covers")]
    [NUnit.Framework.Order(221)]
    public static async Task SearchPublishesPaginationBeforeResolvingCoversAsync()
    {
        var settings = new Settings();
        var (httpService, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = "{\"total\":12,\"items\":[{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"cover\",\"samples\":[],\"task_count\":1}]}";
        var events = new List<string>();
        var pageInput = "0";
        using var coordinator = new VoiceDiscoveryCoordinator(
            context: CreateContext(settings: settings, httpService: httpService),
            getSearchQuery: () => "voice",
            getPageInput: () => pageInput,
            getVoiceIdInput: () => "",
            setSearching: _ => { },
            setSearchPagination: _ => events.Add("pagination"),
            setPageInput: value =>
            {
                pageInput = value;
                events.Add("input");
            },
            setHasSearched: _ => events.Add("searched"),
            setSearchResults: _ => events.Add("results"),
            resolveCoverImageUrl: (_, _, _, _) =>
            {
                events.Add("cover");
                return "cover";
            },
            syncPreviewStateToResults: () => events.Add("sync"),
            setVoiceIdWithoutSaving: _ => { },
            persistSelectedVoice: (_, _) => true,
            applyCachedVoice: _ => { },
            setVoiceIdError: _ => { },
            setSubmittingVoiceId: _ => { },
            isFacadeDisposed: () => false);

        await coordinator.SearchVoicesAsync();

        AssertEnumerableEqual(
            new[] { "pagination", "input", "searched", "results", "sync", "cover" },
            events,
            "Search publishes its complete metadata/navigation snapshot before resolving result covers");
    }

    [NUnit.Framework.Test]
    [NUnit.Framework.Property("FishAudioCaseId", "voice.discovery/dispose-restores-discovery-busy-state-before-cancel-callbacks")]
    [NUnit.Framework.Order(222)]
    public static Task DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync() => RunDisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync();

    internal static Task RunDisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync(Action<DiscoveryRemainderFixtureOwner, string>? onHeldWork = null)
    {
        var owned = new DiscoveryRemainderFixtureOwner();
        return owned.RunAsync(async () =>
        {
            var settings = new Settings();
            var (httpService, http) = TestHttpServiceProxy.Create();
            var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var byIdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSearch = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseById = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            owned.Track(releaseSearch, "{\"total\":0,\"items\":[]}");
            owned.Track(releaseById, "{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            SettingsViewModel? viewModel = null;
            var cancellationCallbackInvoked = false;
            var observedIsSearching = true;
            var observedIsSubmittingVoiceId = true;
            http.GetAsyncHandler = (url, _, ct) =>
            {
                if (url.Contains("/model?", StringComparison.Ordinal))
                {
                    searchStarted.SetResult();
                    owned.Track(ct.Register(() =>
                    {
                        cancellationCallbackInvoked = true;
                        observedIsSearching = viewModel!.IsSearching;
                        observedIsSubmittingVoiceId = viewModel.IsSubmittingVoiceId;
                        throw new InvalidOperationException("Cancellation callback failure.");
                    }));
                    return releaseSearch.Task;
                }

                byIdStarted.SetResult();
                return releaseById.Task;
            };
            viewModel = new SettingsViewModel(
                CreateContext(settings: settings, httpService: httpService),
                settings)
            {
                SearchQuery = "voice",
                VoiceIdInput = "11111111111111111111111111111111",
            };
            owned.Track(viewModel);

            var searchTask = owned.TrackOperation(viewModel.SearchVoicesCommand.ExecuteAsync(null));
            var byIdTask = owned.TrackOperation(viewModel.SubmitVoiceIdCommand.ExecuteAsync(null));
            await searchStarted.Task.WaitAsync(RegressionExecution.Timeout);
            await byIdStarted.Task.WaitAsync(RegressionExecution.Timeout);
            AssertEqual(true, viewModel.IsSearching, "Search should be busy before disposal");
            AssertEqual(true, viewModel.IsSubmittingVoiceId, "By-ID lookup should be busy before disposal");
            onHeldWork?.Invoke(owned, "both-lookups-held");

            Exception? disposeException = null;
            try
            {
                viewModel.Dispose();
            }
            catch (Exception ex)
            {
                disposeException = ex;
            }

            AssertNotNull(disposeException, "The throwing cancellation callback should propagate from disposal");
            AssertEqual(true, cancellationCallbackInvoked, "Disposal should cancel the active search request");
            AssertEqual(false, observedIsSearching, "Cancellation callbacks should observe restored search busy state");
            AssertEqual(false, observedIsSubmittingVoiceId, "Cancellation callbacks should observe restored by-ID busy state");
            AssertEqual(false, viewModel.IsSearching, "Search busy state should remain restored when cancellation throws");
            AssertEqual(false, viewModel.IsSubmittingVoiceId, "By-ID busy state should remain restored when cancellation throws");

            releaseSearch.SetResult("{\"total\":0,\"items\":[]}");
            releaseById.SetResult("{\"_id\":\"11111111111111111111111111111111\",\"title\":\"Voice\",\"description\":\"\",\"cover_image\":\"\",\"samples\":[],\"task_count\":0}");
            await searchTask.WaitAsync(RegressionExecution.Timeout);
            await byIdTask.WaitAsync(RegressionExecution.Timeout);
            viewModel.Dispose();
        });
    }
}
