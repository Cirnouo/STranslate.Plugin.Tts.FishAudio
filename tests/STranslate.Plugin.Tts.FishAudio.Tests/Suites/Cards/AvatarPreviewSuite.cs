using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

// These are injected WPF hover/focus states on compiled controls, not physical input.
internal static class AvatarPreviewSuite
{
    private const string Id = "0123456789abcdef0123456789abcdef";
    private const string OtherId = "1123456789abcdef0123456789abcdef";
    private const string Sample = "https://platform.r2.fish.audio/audio/avatar.mp3";

    internal static async Task RunAllAsync(string? proofDirectory = null)
    {
        foreach (var testCase in CreateCases(proofDirectory))
            await testCase.Run();
        Console.WriteLine("Avatar preview compiled hover/nonfocusable and fake network/audio lifecycle tests passed (four locations, two themes, 100/125/150%, 320/640 DIP).");
    }

    internal static IReadOnlyList<(string Id, Func<Task> Run)> CreateCases(string? proofDirectory = null)
    {
        var cases = new List<(string Id, Func<Task> Run)>();
        foreach (var dark in new[] { false, true })
        foreach (var dpi in new[] { 1d, 1.25, 1.5 })
        foreach (var width in new[] { 320d, 640d })
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            var scale = dpi switch { 1 => "1", 1.25 => "1-25", _ => "1-5" };
            var browse = mode switch { VoiceBrowseMode.Search => "search", VoiceBrowseMode.ById => "by-id", _ => "favorites" };
            var id = FormattableString.Invariant($"cards.avatar/{(dark ? "dark" : "light")}/dpi-{scale}/width-{width:0}/{browse}");
            cases.Add((id, () => RunContextAsync(mode, dark, dpi, width, proofDirectory)));
        }
        return cases;
    }

    private static async Task RunContextAsync(VoiceBrowseMode mode, bool dark, double dpi, double width, string? proofDirectory)
    {
        var elapsed = Stopwatch.StartNew();
        var name = $"{mode}/{(dark ? "Dark" : "Light")}/{dpi * 100}%/{width}";
        Console.WriteLine(FormattableString.Invariant($"[avatar-preview/{name}] queued with {RegressionExecution.Timeout.TotalSeconds}s action deadline."));
        var lastLayoutSubstage = "not entered";
        try
        {
            await RunOnStaThreadAsync(async () =>
            {
                Console.WriteLine($"[avatar-preview/{name}] entered after {elapsed.ElapsedMilliseconds}ms.");
                var passed = false;
                try { await CheckLifecycleAsync(mode, dark, dpi, width, proofDirectory, phase => Volatile.Write(ref lastLayoutSubstage, phase)); passed = true; }
                finally { Console.WriteLine($"[avatar-preview/{name}] completed after {elapsed.ElapsedMilliseconds}ms; passed={passed}."); }
            });
        }
        catch (Exception error)
        {
            Console.WriteLine($"[avatar-preview/{name}] failed ({error.GetType().Name}) after {elapsed.ElapsedMilliseconds}ms; last-layout-substage={Volatile.Read(ref lastLayoutSubstage)}.");
            throw;
        }
    }

    private static async Task CheckLifecycleAsync(VoiceBrowseMode mode, bool dark, double dpi, double width, string? proofDirectory, Action<string> layoutSubstage)
    {
        using var network = OverrideNetworkAvailability(true);
        var settings = new Settings { VoiceId = Id, CachedVoice = new() { Title = "Selected without cover", SampleAudioUrl = Sample } };
        var (service, http) = TestHttpServiceProxy.Create();
        var player = new TestPreviewAudioPlayer { Duration = TimeSpan.FromSeconds(10) };
        TaskCompletionSource? clipboardWrite = null;
        using var vm = new SettingsViewModel(CreateContext(settings: settings, httpService: service, pluginSettingsDirectoryPath: CreateTempDirectory()), settings,
            null, null, previewAudioPlayerFactory: () => player, copyVoiceIdToClipboard: _ => { clipboardWrite?.TrySetResult(); });
        var item = new VoiceSearchItem { Id = Id, Title = "Origin", SampleAudioUrl = Sample, HasMetadata = true };
        var other = new VoiceSearchItem { Id = OtherId, Title = "Independent card", SampleAudioUrl = Sample, HasMetadata = true };
        vm.SearchResults = [item, other]; vm.ByIdResults = [item, other]; vm.FavoriteResults = [item, other]; vm.HasSearched = true; vm.BrowseMode = mode;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        view.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        var layoutNumber = 0;
        void Layout()
        {
            var number = ++layoutNumber;
            LayoutOwner(view, width, phase => layoutSubstage($"{number}/{phase}"));
        }
        // Materialize this owner's compiled card tree before changing its root DPI;
        // otherwise later-created controls can retain the preceding theme DPI.
        Layout();
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [view, new DpiScale(dpi, dpi)]);
        Layout();
        var selected = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleDisplayPreviewCommand));
        var browse = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSearchItemPreviewCommand) && ReferenceEquals(button.CommandParameter, item) && IsPresented(button));
        var second = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSearchItemPreviewCommand) && ReferenceEquals(button.CommandParameter, other) && IsPresented(button));
        var controls = new[] { selected, browse };
        var selectedRoot = Card(selected); var browseRoot = Card(browse); var secondRoot = Card(second);
        var background = browseRoot.Background;
        var selectedBackground = selectedRoot.Background;
        var secondBackground = secondRoot.Background;
        foreach (var button in controls)
        {
            var overlay = Overlay(button);
            AssertEqual(dpi, VisualTreeHelper.GetDpi(overlay).DpiScaleX, "The realized preview overlay inherits the requested visual-root DPI");
            AssertEqual(dpi, VisualTreeHelper.GetDpi(button).DpiScaleX, "The actual preview action inherits the requested visual-root DPI");
            var diameter = ReferenceEquals(button, selected) ? 56d : 48d;
            var host = (Grid)overlay.Parent;
            var portrait = host.Children.OfType<Ellipse>().Single();
            AssertEqual(diameter, host.ActualWidth, "Preview follows the enlarged 48/56-DIP portrait allocation");
            var portraitBounds = new Rect(0, 0, portrait.Width, portrait.Height);
            AssertEqual(portraitBounds, overlay.TransformToAncestor(host).TransformBounds(new Rect(overlay.RenderSize)), "Preview overlay must cover the actual portrait bounds in every card location");
            AssertEqual(portraitBounds, button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize)), "The actual preview action must cover the portrait, including its outer edge");
            var backplate = overlay.Children.OfType<Ellipse>().Single(ellipse => ellipse.Stroke is null);
            AssertEqual(portraitBounds, backplate.TransformToAncestor(host).TransformBounds(new Rect(backplate.RenderSize)), "The light contrast backplate follows the portrait bounds");
            AssertRoundedPlay(button, diameter * .3);
            AssertEqual(.4, backplate.Opacity, "Only the black image backplate uses the reference's forty-percent opacity");
            AssertEqual(Colors.Black, ((SolidColorBrush)backplate.Fill).Color, "The reference-style backplate darkens the image consistently in both themes");
            AssertEqual(false, backplate.IsHitTestVisible, "The contrast layer never owns pointer input");
            AssertProgressGeometry(button);
            try
            {
                host.Width = host.Height = portrait.Width = portrait.Height = diameter + 4;
                Layout();
                AssertEqual(diameter + 4, overlay.ActualWidth, "Overlay sizing follows its actual avatar owner after a later portrait resize");
                AssertEqual(diameter + 4, button.ActualWidth, "Preview input sizing follows the resized avatar owner");
                AssertRoundedPlay(button, (diameter + 4) * .3);
                AssertProgressGeometry(button);
            }
            finally
            {
                host.Width = host.Height = portrait.Width = portrait.Height = diameter;
                Layout();
            }
            AssertEqual(true, !button.Focusable && !KeyboardNavigation.GetIsTabStop(button) && button.IsEnabled, "Avatar action stays available without cover/hover but does not enter keyboard navigation");
            AssertEqual<Style?>(null, button.FocusVisualStyle, "Avatar preview has no keyboard-focus adornment");
            AssertEqual(0d, overlay.Opacity, "Idle artwork is hidden without card hover");
            AssertEqual(Visibility.Visible, button.Visibility, "Only artwork hides; the action remains available");
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_PlaySample"), AutomationProperties.GetName(button), "Idle action has its existing accessible name");
        }
        var originalBounds = controls.Select(button => button.TranslatePoint(new Point(), Card(button))).ToArray();
        SaveProof("idle-no-cover");
        try
        {
            var cover = FindRepoFile("STranslate.Plugin.Tts.FishAudio/icon.png");
            item.CoverUrl = cover; vm.CachedVoiceCoverUrl = cover; Layout();
            SetHover(browseRoot, true); Layout();
            AssertCoverShowsThrough(browse, dpi);
            SaveProof("idle-hover");
            AssertEqual(1d, Overlay(browse).Opacity, "Hover over any card content reveals Play on its avatar");
            AssertForegroundAndHitOwnership(browse);
            AssertEqual(0d, Overlay(selected).Opacity, "Another same-ID card does not inherit hover");
            SetHover(selectedRoot, true); Layout();
            AssertCoverShowsThrough(selected, dpi);
            SaveProof("idle-hover-both-fixture");
            SetHover(selectedRoot, false); Layout();
            AssertEqual(false, Equals(background, browseRoot.Background), "Only pointer hover highlights the card");
            AssertEqual(secondBackground, secondRoot.Background, "Adjacent card keeps its normal background");
            SetHover(browseRoot, false); Layout();
            AssertEqual(0d, Overlay(browse).Opacity, "Leaving the idle card hides Play");
            AssertEqual(background, browseRoot.Background, "Leaving restores normal background");
            // Model mouse-acquired focus retained after Click, without asking WPF to move physical focus.
            SetFocused(browse, true); Layout();
            AssertEqual(0d, Overlay(browse).Opacity, "Retained mouse focus alone must not reveal idle Play");
            foreach (var hoveredAtEnd in new[] { false, true })
            {
                var response = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                http.GetAsStreamAsyncHandler = (_, _, _) => response.Task;
                var pending = vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item); Layout();
                foreach (var button in controls) AssertActive(button, "Preview_CancelLoading");
                SaveProof("preparing-leave");
                AssertEqual(background, browseRoot.Background, "Preparing with pointer outside does not highlight");
                SetHover(secondRoot, true); Layout();
                AssertEqual(background, browseRoot.Background, "A playing Voice ID does not acquire another card's hover highlight");
                AssertEqual(false, Equals(secondBackground, secondRoot.Background), "The independent hovered card highlights while another voice is active");
                AssertEqual(1d, Overlay(second).Opacity, "The independent idle avatar reveals Play while another voice is active");
                SetHover(secondRoot, false);
                response.SetResult(new MemoryStream([1, 2, 3])); await pending; Layout();
                foreach (var button in controls) AssertActive(button, "Preview_Stop");
                AssertCoverShowsThrough(browse, dpi);
                SaveProof("active-leave");
                vm.CachedVoiceCoverUrl = null; Layout();
                AssertActive(selected, "Preview_Stop");
                SaveProof("active-leave-no-selected-cover");
                vm.CachedVoiceCoverUrl = cover; Layout();
                AssertEqual(true, vm.IsDisplayVoicePreviewing && item.IsBeingPreviewed, "Real fake-player output start projects to both compiled overlays");
                SetHover(browseRoot, hoveredAtEnd); Layout();
                player.RaiseEnded(); Layout();
                for (var i = 0; i < controls.Length; i++)
                    AssertEqual(originalBounds[i], controls[i].TranslatePoint(new Point(), Card(controls[i])), "Hover/preparing/playing/end never shifts the avatar allocation within its card");
                AssertEqual(false, vm.IsDisplayVoicePreviewActive || item.IsPreviewActive, "The player's natural completion retires the existing owner");
                AssertEqual(hoveredAtEnd ? 1d : 0d, Overlay(browse).Opacity, "Natural end restores hover-only artwork despite retained mouse focus");
                AssertEqual(0d, Overlay(selected).Opacity, "Natural end hides the unhovered selected avatar");
                if (proofDirectory is not null && mode == VoiceBrowseMode.Search && dpi == 1.25)
                    Save(browseRoot, System.IO.Path.Combine(proofDirectory, $"{(dark ? "dark" : "light")}-{width}-natural-end-{hoveredAtEnd}.png"), dpi);
                SetHover(browseRoot, false); Layout();
            }
            // A second click cancels preparation; its late response may not restore artwork.
            var held = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.GetAsStreamAsyncHandler = (_, _, _) => held.Task;
            var old = vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
            vm.CachedVoiceSampleUrl = item.SampleAudioUrl = null!; Layout();
            AssertActive(selected, "Preview_CancelLoading"); AssertActive(browse, "Preview_CancelLoading");
            await vm.ToggleDisplayPreviewCommand.ExecuteAsync(null); Layout();
            var idleCount = http.GetAsStreamCallCount;
            await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
            AssertEqual(idleCount, http.GetAsStreamCallCount, "Idle missing-sample cards cannot start another request");
            vm.CachedVoiceSampleUrl = item.SampleAudioUrl = Sample;
            AssertEqual(0d, Overlay(browse).Opacity, "Cross-entry preparation cancel hides the unhovered overlay");
            held.SetResult(new MemoryStream([4, 5, 6])); await old; Layout();
            AssertEqual(0d, Overlay(browse).Opacity, "Late canceled completion cannot reactivate the overlay");
            http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
            await vm.ToggleDisplayPreviewCommand.ExecuteAsync(null); Layout();
            // Missing refreshed media/cover must never remove a still-active cancel/Stop action.
            vm.CachedVoiceSampleUrl = item.SampleAudioUrl = null!; Layout();
            AssertActive(selected, "Preview_Stop"); AssertActive(browse, "Preview_Stop");
            await vm.ToggleDisplayPreviewCommand.ExecuteAsync(null); Layout();
            AssertEqual(false, vm.IsDisplayVoicePreviewActive, "Existing Stop command retires playback");
            vm.CachedVoiceSampleUrl = item.SampleAudioUrl = Sample;
            await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item); player.RaiseFailed(new IOException("Fixture failure")); Layout();
            AssertEqual(0d, Overlay(browse).Opacity, "Playback failure restores idle hover-only artwork");
            held = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            http.GetAsStreamAsyncHandler = (_, _, _) => held.Task;
            old = vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item);
            vm.SearchResults = [other]; vm.ByIdResults = [other]; vm.FavoriteResults = [other]; Layout();
            AssertEqual(0d, Overlay(selected).Opacity, "Removing the originating card retires matching selected activity");
            held.SetResult(new MemoryStream([7, 8, 9])); await old; Layout();
            AssertEqual(false, vm.IsDisplayVoicePreviewActive, "Removed origin's stale response cannot restart playback");
            AssertEqual(selectedBackground, selectedRoot.Background, "Audio retirement never latches the selected background");

            // Native automation still invokes the selected preview without mouse hover.
            http.GetAsStreamAsyncHandler = (_, _, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
            var count = http.GetAsStreamCallCount;
            var acceptedPlayback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.ComponentModel.PropertyChangedEventHandler playingPublished = (_, args) =>
            {
                if (args.PropertyName == nameof(vm.IsDisplayVoicePreviewing) && vm.IsDisplayVoicePreviewing)
                    acceptedPlayback.TrySetResult();
            };
            vm.PropertyChanged += playingPublished;
            try
            {
                ((IInvokeProvider)new ButtonAutomationPeer(selected).GetPattern(PatternInterface.Invoke)!).Invoke();
                await acceptedPlayback.Task.WaitAsync(RegressionExecution.Timeout);
            }
            finally { vm.PropertyChanged -= playingPublished; }
            Layout();
            AssertEqual(count + 1, http.GetAsStreamCallCount, "The reachable preview button retains native automation invocation");
            item.SampleAudioUrl = null!;
            await vm.ToggleSearchItemPreviewCommand.ExecuteAsync(item); Layout();
            AssertEqual(true, vm.IsDisplayVoicePreviewing, "A removed missing-sample card cannot stop another current owner's same-ID playback");
            AssertEqual(count + 1, http.GetAsStreamCallCount, "Removed missing-sample action cannot request audio");
            player.RaiseEnded(); Layout();
            var title = Visuals(selectedRoot).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.CopyVoiceIdCommand));
            clipboardWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                ((IInvokeProvider)new ButtonAutomationPeer(title).GetPattern(PatternInterface.Invoke)!).Invoke();
                await clipboardWrite.Task.WaitAsync(RegressionExecution.Timeout);
            }
            finally { clipboardWrite = null; }
            vm.BrowseMode = VoiceBrowseMode.Search;
            vm.ToggleSelectedVoiceFavoriteCommand.Execute(null);
            vm.SelectVoiceCommand.Execute(other);
            AssertEqual(count + 1, http.GetAsStreamCallCount, "Title/Like/Apply retain independent actions and never start preview");
        }
        finally
        {
            SetHover(browseRoot, false); SetHover(selectedRoot, false); SetHover(secondRoot, false); SetFocused(browse, false);
            clipboardWrite = null;
        }
        Console.WriteLine($"AVATAR {mode}/{(dark ? "Dark" : "Light")}/{dpi * 100}%/{width} passed");

        void SaveProof(string state)
        {
            if (proofDirectory is not null && mode == VoiceBrowseMode.Search && dpi == 1.25)
                foreach (var button in controls)
                    Save(Card(button), System.IO.Path.Combine(proofDirectory, $"{(dark ? "dark" : "light")}-{width}-{(ReferenceEquals(button, selected) ? "selected" : "search")}-{state}.png"), dpi);
        }

        void AssertActive(Button button, string resource)
        {
            AssertEqual(Visibility.Visible, Overlay(button).Visibility, "Active action survives missing sample or cover");
            AssertEqual(1d, Overlay(button).Opacity, "Preparation/playback stays visible after pointer leave");
            AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_" + resource), AutomationProperties.GetName(button), "Active tooltip/accessibility uses the existing localized state");
            AssertEqual(AutomationProperties.GetName(button), button.ToolTip, "Tooltip and accessibility state agree");
            AssertEqual(true, button.IsEnabled, "Active cancel/Stop remains usable even without a sample URL");
            var spinner = Overlay(button).Children.OfType<Grid>().Single();
            AssertEqual(resource == "Preview_CancelLoading" ? Visibility.Visible : Visibility.Collapsed, spinner.Visibility, "Only preparation uses the existing indeterminate spinner");
            var arc = Overlay(button).Children.OfType<Ellipse>().Single(ellipse => ReferenceEquals(ellipse.Style, view.FindResource("AvatarProgressRingStyle")));
            AssertEqual(resource == "Preview_Stop" ? Visibility.Visible : Visibility.Collapsed, arc.Visibility, "Real playing state alone exposes the avatar-sized progress arc");
            AssertEqual(true, Visuals(button).OfType<Border>().Any(border => border.Width is 9 or 10 && border.Visibility == Visibility.Visible), "Active control keeps square Stop/cancel");
            AssertEqual(Visibility.Collapsed, Visuals(button).OfType<Path>().Single().Visibility, "Preparation/playing hides only the idle filled triangle");
            AssertForegroundAndHitOwnership(button);
            AssertProgressGeometry(button);
            var originalDisplayProgress = vm.PreviewProgress;
            var originalItemProgress = item.PreviewProgress;
            foreach (var progress in resource == "Preview_Stop" ? new[] { 0d, .25, 1d } : Array.Empty<double>())
            {
                vm.PreviewProgress = item.PreviewProgress = progress;
                Layout();
                var circumferenceUnits = (Overlay(button).ActualWidth - arc.StrokeThickness) * Math.PI / arc.StrokeThickness;
                AssertEqual(true, Math.Abs(arc.StrokeDashOffset - (1 - progress) * circumferenceUnits) < .00001,
                    "Zero, quarter and complete progress use the actual avatar circumference, without changing playback state");
            }
            vm.PreviewProgress = originalDisplayProgress;
            item.PreviewProgress = originalItemProgress;
            Layout();
            var host = (Grid)Overlay(button).Parent;
            var portrait = host.Children.OfType<Ellipse>().Single();
            var diameter = host.Width;
            try
            {
                host.Width = host.Height = portrait.Width = portrait.Height = diameter + 4;
                Layout();
                AssertProgressGeometry(button);
                if (resource == "Preview_Stop")
                    AssertEqual(true, Math.Abs(arc.StrokeDashOffset - (1 - originalDisplayProgress) * (diameter + 4 - 1.5) * Math.PI / 1.5) < .00001,
                        "Active progress offset follows a resized portrait rather than retaining the previous circumference");
            }
            finally
            {
                host.Width = host.Height = portrait.Width = portrait.Height = diameter;
                Layout();
            }
        }
    }

    private static void LayoutOwner(FrameworkElement view, double width, Action<string> stage)
    {
        stage("Measure");
        view.Measure(new Size(width, double.PositiveInfinity));
        stage("Arrange");
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        stage("first UpdateLayout");
        view.UpdateLayout();
        // These assertions require the owner's bindings/layout, not global idle.
        // UpdateLayout still participates in WPF's shared layout manager.
        stage("owner DataBind fence");
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        stage("second UpdateLayout");
        view.UpdateLayout();
        stage("owner validity checks");
        AssertEqual(true, view.IsMeasureValid, "Avatar assertions require a measured owner");
        AssertEqual(true, view.IsArrangeValid, "Avatar assertions require an arranged owner");
        stage("ready");
    }

    internal static Grid Overlay(Button button) => (Grid)VisualTreeHelper.GetParent(button);

    private static void AssertRoundedPlay(Button button, double nominalSize)
    {
        var paths = Visuals(button).OfType<Path>().ToArray();
        AssertEqual(1, paths.Length, "Current avatars use a filled rounded Play contour instead of the original font outline");
        var play = paths.Single();
        AssertEqual(true, ReferenceEquals(play.Data, play.FindResource("AvatarPreviewPlayGeometry")), "All current avatar locations share one closed Play contour");
        AssertEqual(true, Math.Abs(play.Width - nominalSize) < .00001 && Math.Abs(play.Height - nominalSize) < .00001 &&
            Math.Abs(Math.Max(play.ActualWidth, play.ActualHeight) - nominalSize) < .00001,
            "The filled triangle's maximum dimension follows its reference-proportional foreground allocation");
        AssertEqual(true, play.ActualWidth < play.ActualHeight && play.ActualWidth > play.ActualHeight * .8,
            "Uniform stretch preserves the rounded triangle's aspect instead of distorting it to a square");
        AssertEqual(Visibility.Visible, play.Visibility, "Idle Play remains available for the existing hover reveal");
        AssertEqual(1d, play.Opacity, "The filled Play foreground is not faded with its backplate");
        AssertEqual(false, play.IsHitTestVisible || play.Focusable, "The filled artwork is decorative and leaves pointer/focus ownership on its existing button");
        AssertEqual(Colors.White, ((SolidColorBrush)play.Fill).Color, "Filled Play keeps the reference's opaque white foreground in either theme");
        AssertEqual<Brush?>(null, play.Stroke, "Play is filled geometry rather than an outlined triangle with rounded stroke joins");
        var bounds = play.Data.Bounds;
        bool Filled(double x, double y) => play.Data.FillContains(new Point(bounds.Left + bounds.Width * x, bounds.Top + bounds.Height * y));
        AssertEqual(true, Filled(.005, .5) && Filled(.25, .5) && Filled(.5, .5) && Filled(.75, .5), "The right-facing triangle has a solid interior and a substantial left edge");
        AssertEqual(false, Filled(.005, .005) || Filled(.005, .995), "The two left corners round the filled region itself");
        AssertEqual(true, Filled(.97, .46) && Filled(.97, .54), "The right tip has a soft filled curve instead of a sharp triangle vertex");
        AssertEqual(true, PathGeometry.CreateFromGeometry(play.Data).Figures.All(figure => figure.IsClosed), "The shared Play silhouette is explicitly closed");
    }

    private static void AssertForegroundAndHitOwnership(Button button)
    {
        var overlay = Overlay(button);
        AssertEqual(1d, button.Opacity, "The foreground button is not faded with the backplate");
        foreach (var layer in overlay.Children.OfType<UIElement>().Where(layer => layer is not Ellipse { Stroke: null }))
            AssertEqual(1d, layer.Opacity, "Play, Stop, spinner and progress remain at full presentation opacity");
        foreach (var stop in Visuals(button).OfType<Border>().Where(border => border.Width is 9 or 10))
            AssertEqual(Colors.White, ((SolidColorBrush)stop.Background).Color, "Stop/cancel retains opaque white contrast against the reference backplate");
        foreach (var ring in Visuals(overlay).OfType<Ellipse>().Where(ellipse =>
            ReferenceEquals(ellipse.Style, ellipse.FindResource("AvatarProgressRingStyle")) ||
            ReferenceEquals(ellipse.Style, ellipse.FindResource("AvatarPreparingRingStyle"))))
            AssertEqual(Colors.White, ((SolidColorBrush)ring.Stroke).Color, "Progress/spinner retains opaque white contrast in both themes");
        var host = (Grid)overlay.Parent;
        foreach (var point in new[] { new Point(1, host.ActualHeight / 2), new Point(host.ActualWidth - 1, host.ActualHeight / 2) })
        {
            // This compiled fixture has no presentation source; inspect its actual
            // visual hit geometry rather than InputHitTest's presentation visibility.
            var hit = VisualTreeHelper.HitTest(host, point)?.VisualHit;
            AssertNotNull(hit, "The transparent preview target covers both portrait edges in the compiled visual tree");
            AssertEqual(true, ReferenceEquals(hit, button) || Ancestors(hit!).Contains(button), "Both portrait edges route pointer input to the existing preview action");
        }
    }

    private static void AssertProgressGeometry(Button button)
    {
        var overlay = Overlay(button);
        var host = (Grid)overlay.Parent;
        var center = new Point(host.ActualWidth / 2, host.ActualHeight / 2);
        foreach (var ellipse in Visuals(overlay).OfType<Ellipse>().Where(ellipse => ellipse.Stroke is not null && IsPresented(ellipse)))
        {
            var geometry = ellipse.RenderedGeometry.Bounds;
            var actual = ellipse.TranslatePoint(new Point(geometry.Left + geometry.Width / 2, geometry.Top + geometry.Height / 2), host);
            AssertEqual(true, Math.Abs(actual.X - center.X) < .00001 && Math.Abs(actual.Y - center.Y) < .00001,
                "The actual rest, progress and spinner paths center on the portrait owner");
            AssertEqual(true, Math.Abs(geometry.Width + ellipse.StrokeThickness - host.ActualWidth) < .00001,
                "Progress geometry fits inside the avatar-sized owner with its original stroke weight");
            if (ReferenceEquals(ellipse.Style, ellipse.FindResource("AvatarProgressRingStyle")))
            {
                AssertEqual(2, ellipse.StrokeDashArray.Count, "Visible progress retains one circumference dash and gap");
                var circumferenceUnits = geometry.Width * Math.PI / ellipse.StrokeThickness;
                AssertEqual(true, ellipse.StrokeDashArray.All(dash => Math.Abs(dash - circumferenceUnits) < .00001),
                    "The progress dash and gap each match the actual ring circumference");
            }
            else if (ReferenceEquals(ellipse.Style, ellipse.FindResource("AvatarPreparingRingStyle")))
            {
                AssertEqual(2, ellipse.StrokeDashArray.Count, "Visible preparation retains a quarter-ring dash and its remaining gap");
                var circumferenceUnits = geometry.Width * Math.PI / ellipse.StrokeThickness;
                AssertEqual(true, Math.Abs(ellipse.StrokeDashArray[0] - circumferenceUnits / 4) < .00001 &&
                    Math.Abs(ellipse.StrokeDashArray[1] - circumferenceUnits * 3 / 4) < .00001,
                    "The preparation spinner retains one quarter of the avatar-sized ring rather than repeating a legacy-sized dash");
            }
        }
    }
    internal static Border Card(DependencyObject child) => Ancestors(child).OfType<Border>().First(border => border.Child is DockPanel);
    internal static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current)) yield return current;
    }
    internal static void SetHover(UIElement target, bool value)
    {
        var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
        writeFlag.Invoke(target, [Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache"), value]);
        target.SetValue((DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
        AssertEqual(value, target.IsMouseOver, "Injected hover fixture establishes WPF's retained pointer state");
    }
    private static void SetFocused(Button target, bool value) => target.SetValue((DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    internal static bool IsPresented(FrameworkElement element) => element.Visibility == Visibility.Visible && Ancestors(element).OfType<UIElement>().All(parent => parent.Visibility == Visibility.Visible);
    internal static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement card, string path, double dpi)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var bitmap = Capture(card, dpi);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }

    private static void AssertCoverShowsThrough(Button button, double dpi)
    {
        var overlay = Overlay(button);
        var avatar = (Grid)overlay.Parent;
        var portrait = avatar.Children.OfType<Ellipse>().Single();
        AssertEqual(portrait.TransformToAncestor(avatar).TransformBounds(new Rect(portrait.RenderSize)),
            overlay.TransformToAncestor(avatar).TransformBounds(new Rect(overlay.RenderSize)), "Image-backed portrait and overlay have equal realized bounds");
        var image = (ImageBrush)portrait.Fill;
        var original = image.ImageSource;
        var originalBinding = BindingOperations.GetBindingExpression(image, ImageBrush.ImageSourceProperty);
        AssertNotNull(originalBinding, "The transparency fixture must exercise the compiled cover binding");
        var card = Card(button);
        var point = overlay.TranslatePoint(new Point(7, overlay.ActualHeight / 2), card);
        try
        {
            image.SetCurrentValue(ImageBrush.ImageSourceProperty, BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4));
            var red = Pixel(Capture(card, dpi));
            image.SetCurrentValue(ImageBrush.ImageSourceProperty, BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 0, 0, 255 }, 4));
            var blue = Pixel(Capture(card, dpi));
            AssertEqual(true, Math.Abs(red[0] - blue[0]) is >= 150 and <= 156 && Math.Abs(red[2] - blue[2]) is >= 150 and <= 156,
                "Idle/active compositing retains about sixty percent of both portrait color channels (forty-percent black backplate, injected red/blue ImageBrush fixture)");
        }
        finally { image.SetCurrentValue(ImageBrush.ImageSourceProperty, original); }
        AssertEqual(true, ReferenceEquals(originalBinding, BindingOperations.GetBindingExpression(image, ImageBrush.ImageSourceProperty)),
            "The transparency fixture must retain the original cover BindingExpression");
        byte[] Pixel(RenderTargetBitmap bitmap)
        {
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)(point.X * dpi), (int)(point.Y * dpi), 1, 1), pixel, 4, 0);
            return pixel;
        }
    }

    private static RenderTargetBitmap Capture(FrameworkElement card, double dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * dpi), (int)Math.Ceiling(card.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, card.ActualWidth, card.ActualHeight);
            // Native card and hover brushes are translucent; preserve the actual themed page backdrop.
            context.DrawRectangle((Brush)card.FindResource("SolidBackgroundFillColorBaseBrush"), null, bounds);
            context.DrawRectangle(new VisualBrush(card), null, bounds);
        }
        bitmap.Render(drawing);
        return bitmap;
    }
}
