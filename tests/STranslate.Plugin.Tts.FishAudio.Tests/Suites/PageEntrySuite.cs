using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Presentation;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class PageEntrySuite
{
    internal static async Task RunAllAsync(string? group = null)
    {
        if (group is null or "fields") await RunOnStaThreadAsync(CompiledFieldsConstrainWholeDraftsAsync);
        if (group is null or "search") await RunOnStaThreadAsync(SearchMaximumAndCommitTransitionsAsync);
        if (group is null or "favorites") await RunOnStaThreadAsync(FavoriteCommitsPreservePendingOwnerAsync);
        var extreme = new VoiceSearchPagination(int.MaxValue, null, false, null, true, false);
        AssertEqual(int.MaxValue, extreme.InputMaximum(), "Next-page allowance is overflow-safe at the integer boundary");
        AssertEqual(true, extreme.CanRequestPage(int.MaxValue), "The same canonical integer cap governs domain requests");
    }

    private static string Id(int number) => number.ToString("x32");
    private static string Response(int? total = 72, bool? more = null, int? maxOffset = null) => JsonSerializer.Serialize(new
    {
        total, has_more = more, max_offset = maxOffset,
        items = Enumerable.Range(1, 6).Select(i => new { _id = Id(i), title = "Page voice" }),
    });

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static TextBox Field(SettingsView view, string name) => (TextBox)view.FindName(name);
    private static void Draft(TextBox input, string text)
    {
        input.SetCurrentValue(TextBox.TextProperty, text);
        input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Drain();
    }
    private static bool Type(TextBox input, string text)
    {
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, text))
        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
        input.RaiseEvent(args);
        if (!args.Handled) input.SelectedText = text;
        Drain();
        return !args.Handled;
    }
    // Stage the supplied DataObject at the owned WPF paste boundary, then emulate
    // the native replacement only after approval. Never touch the system clipboard.
    private static bool Paste(TextBox input, DataObject data, string? format = null)
    {
        var args = new DataObjectPastingEventArgs(data, false, format ?? DataFormats.UnicodeText) { RoutedEvent = DataObject.PastingEvent };
        input.RaiseEvent(args);
        if (!args.CommandCancelled) input.SelectedText = (string)data.GetData(DataFormats.UnicodeText)!;
        Drain();
        return !args.CommandCancelled;
    }
    private static void Blur(TextBox input) => input.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
    private static void Enter(TextBox input)
    {
        var command = input.InputBindings.OfType<KeyBinding>().Single(binding => binding.Key == Key.Enter).Command;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private static async Task CompiledFieldsConstrainWholeDraftsAsync()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "favorite-voices.json"), JsonSerializer.Serialize(Enumerable.Range(1, 72).Select(Id)));
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response();
        var context = CreateContext(httpService: service, pluginSettingsDirectoryPath: directory);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, new Settings());
        await vm.SearchVoicesCommand.ExecuteAsync(null);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        Drain();
        var calls = http.GetCallCount;
        var saves = storage.SaveCount;
        foreach (var (name, source) in new[] { ("PageInputBox", nameof(vm.PageInput)), ("FavoritePageInputBox", nameof(vm.FavoritePageInput)) })
        {
            var input = Field(view, name);
            AssertEqual(40d, input.Width, "Native page field geometry stays 40 DIP");
            foreach (var invalid in new[] { "a", "+", "-", ".", " ", "１", "١", "1a", "1\n2", "2\0" })
            {
                Draft(input, "1"); input.SelectAll();
                AssertEqual(false, Type(input, invalid), "Reject non-ASCII/mixed composition wholesale");
                AssertEqual("1", input.Text, "Rejected composition leaves the draft intact");
                AssertEqual(false, Paste(input, new DataObject(DataFormats.UnicodeText, invalid)), "Reject malformed paste wholesale");
                AssertEqual("1", input.Text, "Rejected paste leaves the draft intact");
            }
            AssertEqual(false, Paste(input, new DataObject(DataFormats.Bitmap, new byte[] { 1 }), DataFormats.Bitmap), "Missing Unicode text is rejected");
            AssertEqual(BindingMode.TwoWay, BindingOperations.GetBinding(input, TextBox.TextProperty)!.Mode, "Page draft binding must explicitly publish both ways");
            Draft(input, "1"); input.CaretIndex = 1;
            AssertEqual(true, Type(input, "3"), "Digits remain editable");
            AssertEqual("12", input.Text, "13 clamps to 12 before blur");
            AssertEqual("12", source == nameof(vm.PageInput) ? vm.PageInput : vm.FavoritePageInput, "Immediate clamp reaches the draft source");
            Draft(input, "10"); input.Select(1, 1);
            AssertEqual(true, Type(input, "1"), "Selection replacement is accepted as a whole draft");
            AssertEqual("11", input.Text, "Selection replacement keeps the unselected prefix");
            input.SelectAll();
            AssertEqual(true, Paste(input, new DataObject(DataFormats.UnicodeText, new string('9', 4000))), "Oversized digit paste is valid lexical input");
            AssertEqual("12", input.Text, "Huge digit strings clamp without overflow");
            input.SelectAll(); Paste(input, new DataObject(DataFormats.UnicodeText, "0002"));
            AssertEqual("0002", input.Text, "Leading zeros survive editing");
            foreach (var temporary in new[] { "", "0", "000" })
            {
                Draft(input, temporary);
                AssertEqual(temporary, input.Text, "Empty and zero remain editable until commit");
                Blur(input); Drain();
                AssertEqual("1", input.Text, "Empty/zero commit restores the applied page");
            }
            Draft(input, "1");
            var space = new KeyEventArgs(Keyboard.PrimaryDevice, new TestInputSource(), 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            input.RaiseEvent(space);
            AssertEqual(true, space.Handled, "Space is blocked before native editing");
            // Programmatic invalid drafts remain the domain command's responsibility.
            Draft(input, "a1"); input.Select(1, 1);
            AssertEqual(false, Type(input, "2"), "Validate the whole selection replacement, including existing text");
            Draft(input, "1");
        }
        AssertEqual(calls, http.GetCallCount, "Editing and invalid restoration never navigate");
        AssertEqual(saves, storage.SaveCount, "Page entry never persists settings");
    }

    private static async Task SearchMaximumAndCommitTransitionsAsync()
    {
        var (service, http) = TestHttpServiceProxy.Create();
        http.GetResponseJson = Response();
        using var vm = new SettingsViewModel(CreateContext(httpService: service), new Settings());
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var input = Field(view, "PageInputBox");
        await vm.SearchVoicesCommand.ExecuteAsync(null); Drain();
        Draft(input, "12"); Enter(input); Blur(input);
        await vm.CommitPageInputCommand.ExecutionTask!; Drain();
        AssertEqual(12, vm.SearchPage, "Changed page commits through the compiled field");
        AssertEqual("12", input.Text, "New maximum is published before the new draft");
        var cards = vm.SearchResults;
        var calls = http.GetCallCount;
        Draft(input, "00012"); Enter(input); Blur(input); Blur(input); Drain();
        AssertEqual("12", input.Text, "Same-page commit canonicalizes leading zeros");
        AssertEqual(calls, http.GetCallCount, "Enter then blur and repeated blur are idempotent");
        AssertEqual(true, ReferenceEquals(cards, vm.SearchResults), "No-op commits preserve result collection identity");
        foreach (var invalid in new[] { "2\0", "+2", " 2", "2 ", "١", new string('9', 50) })
        {
            // No view here: independent command validation rejects programmatic input.
            view.DataContext = null; vm.PageInput = invalid;
            await vm.CommitPageInputCommand.ExecuteAsync(null);
            AssertEqual(calls, http.GetCallCount, "Domain rejects invalid/out-of-policy drafts independently");
            AssertEqual("12", vm.PageInput, "Domain rejection restores the applied page");
        }
        view.DataContext = vm;
        http.GetResponseJson = Response(100000, true, 1000);
        await vm.SearchVoicesCommand.ExecuteAsync(null); Drain();
        Draft(input, "999"); AssertEqual("167", input.Text, "Provider cap owns the input maximum, not approximate total");
        http.GetResponseJson = Response(null, true);
        await vm.SearchVoicesCommand.ExecuteAsync(null); Drain();
        AssertEqual("1", input.Text, "A new smaller maximum and page publish consistently");
        await vm.NextPageCommand.ExecuteAsync(null); Drain();
        AssertEqual("2", input.Text, "A new page draft survives expansion of the applied maximum");
        AssertEqual(3, PageInputBehavior.GetMaximum(input), "The compiled editor observes the new canonical allowance");
        await vm.NextPageCommand.ExecuteAsync(null); Drain();
        AssertEqual("3", input.Text, "The next new page and maximum publish together");
        AssertEqual(4, PageInputBehavior.GetMaximum(input), "The compiled bound advances before subsequent editing");
        await vm.PrevPageCommand.ExecuteAsync(null);
        await vm.PrevPageCommand.ExecuteAsync(null); Drain();
        Draft(input, "3"); AssertEqual("3", input.Text, "Unknown extent preserves a previously visited higher page");
        Draft(input, "9"); AssertEqual("3", input.Text, "Unknown extent clamps to canonical visited/next maximum");
        vm.SearchTotalPages = 99;
        Draft(input, "9"); AssertEqual("3", input.Text, "Presentation fallback cannot authorize navigation");
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        http.GetAsyncHandler = (_, _, _) => pending.Task;
        Draft(input, "0002"); calls = http.GetCallCount; Enter(input); Blur(input); Blur(input);
        AssertEqual(calls + 1, http.GetCallCount, "A pending commit cannot duplicate on focus loss");
        AssertEqual("2", input.Text, "Changed-page commit normalizes before awaiting the response");
        pending.SetResult(Response(null, true));
        await vm.CommitPageInputCommand.ExecutionTask!; Drain();
        AssertEqual("2", input.Text, "Page/max transition does not clamp against the previous maximum");
    }

    private static async Task FavoriteCommitsPreservePendingOwnerAsync()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "favorite-voices.json"), JsonSerializer.Serialize(Enumerable.Range(1, 7).Select(Id)));
        var (service, http) = TestHttpServiceProxy.Create();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        http.GetAsyncHandler = (_, _, token) => { tokens.Add(token); return pending.Task; };
        var context = CreateContext(httpService: service, pluginSettingsDirectoryPath: directory);
        var storage = (ContextProxy)(object)context;
        using var vm = new SettingsViewModel(context, new Settings());
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var input = Field(view, "FavoritePageInputBox");
        vm.SwitchToFavoritesCommand.Execute(null); Drain();
        var collection = vm.FavoriteResults;
        var card = collection[0];
        var owner = vm.PendingFavoriteMetadataTask;
        var calls = http.GetCallCount;
        var saves = storage.SaveCount;
        Draft(input, "0001"); Enter(input); Blur(input); Blur(input); Drain();
        AssertEqual(calls, http.GetCallCount, "Same-page favorite commit cannot reload metadata");
        AssertEqual(true, ReferenceEquals(collection, vm.FavoriteResults) && ReferenceEquals(card, vm.FavoriteResults[0]), "No-op preserves collection/card identity");
        AssertEqual(true, ReferenceEquals(owner, vm.PendingFavoriteMetadataTask), "No-op preserves the pending metadata task");
        AssertEqual(false, tokens.Any(token => token.IsCancellationRequested), "No-op cannot cancel metadata ownership");
        AssertEqual("1", input.Text, "No-op canonicalizes the page draft");
        foreach (var invalid in new[] { "2\0", "+2", " 2", "2 ", "0", "", "3" })
        {
            view.DataContext = null; vm.FavoritePageInput = invalid; vm.CommitFavoritePageInputCommand.Execute(null);
            AssertEqual(calls, http.GetCallCount, "Favorite domain rejects invalid drafts without loading");
            AssertEqual("1", vm.FavoritePageInput, "Invalid favorite draft restores old page");
        }
        view.DataContext = vm; Drain();
        Draft(input, "2"); Enter(input); Blur(input); Blur(input); Drain();
        var secondOwner = vm.PendingFavoriteMetadataTask;
        AssertEqual(calls + 1, http.GetCallCount, "Changed favorite page loads once");
        AssertEqual(true, tokens.Take(6).All(token => token.IsCancellationRequested), "Changed page retires prior loads");
        AssertEqual(2, vm.FavoritePage, "Favorite page changes once");
        vm.ToggleFavoriteCommand.Execute(vm.FavoriteResults[0]); Drain();
        AssertEqual(1, vm.FavoriteTotalPages, "Removing the final-page card reduces the exact maximum");
        AssertEqual(1, vm.FavoritePage, "Removal clamps applied page");
        AssertEqual("1", input.Text, "Maximum reduction and replacement draft agree");
        AssertEqual(1, PageInputBehavior.GetMaximum(input), "The compiled favorite maximum follows the exact reduced count");
        Draft(input, "2"); AssertEqual("1", input.Text, "Maximum changes recheck future editing");
        var refreshed = vm.PendingFavoriteMetadataTask;
        var previousCollection = vm.FavoriteResults;
        calls = http.GetCallCount;
        vm.ToggleFavoriteCommand.Execute(vm.FavoriteResults[0]); Drain();
        AssertEqual(true, http.GetCallCount > calls, "Same numbered page still refreshes after real bookmark mutation");
        AssertEqual(false, ReferenceEquals(previousCollection, vm.FavoriteResults), "Mutation rebuild is not suppressed by the input no-op guard");
        pending.SetResult("{\"title\":\"Finished offline metadata\"}");
        await Task.WhenAll(owner, secondOwner, refreshed, vm.PendingFavoriteMetadataTask);
        AssertEqual(saves, storage.SaveCount, "Transient navigation never saves Settings");
    }

    private sealed class TestInputSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
}
