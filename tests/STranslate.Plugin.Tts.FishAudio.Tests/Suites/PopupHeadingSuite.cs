using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class PopupHeadingSuite
{
    internal static async Task RunAsync()
    {
        LeaseOwnershipAndFailures();
        await RunOnStaThreadAsync(CompiledSearchHeading);
        await RunOnStaThreadAsync(CompiledLanguageHeading);
        await RunOnStaThreadAsync(HeadingFailureDoesNotAffectMaterialOrInput);
        await RunOnStaThreadAsync(SourceReplacementAndDestroyedRetirement);
    }

    private static void CompiledSearchHeading()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        using var owner = Attach(view);
        var control = (SearchAdjustmentControl)view.FindName("SearchAdjustments");
        var popup = (Popup)control.FindName("AdjustmentPopup");
        try
        {
            ((Button)control.FindName("AdjustmentButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();
            var surface = (Border)control.FindName("PopupSurface");
            AssertHeadingAndLocales(view, surface, "STranslate_Plugin_Tts_FishAudio_Search_Adjustments");
            var first = (HwndSource)PresentationSource.FromVisual(surface);
            popup.IsOpen = false; Drain();
            ((Button)control.FindName("AdjustmentButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain();
            AssertEqual(false, ReferenceEquals(first, PresentationSource.FromVisual(surface)), "Reopening owns a fresh source and heading lease");
            AssertHeading(surface, "STranslate_Plugin_Tts_FishAudio_Search_Adjustments");
        }
        finally { popup.IsOpen = false; Drain(); }
    }

    private static void CompiledLanguageHeading()
    {
        var settings = new Settings();
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        var tags = new VoiceLanguageTags { Languages = ["en", "zh", "ja", "de", "fr", "ko", "ru", "tr", "pt", "it"] };
        tags.Resources.MergedDictionaries.Add(view.Resources);
        var scroll = new ScrollViewer { Content = tags, Height = 220 };
        using var owner = Attach(scroll);
        try
        {
            tags.OpenPopup(); Drain();
            AssertEqual(true, tags.IsPopupOpen, "The compiled language shell opens before checking native identity");
            var surface = (Border)tags.FindName("PopupSurface");
            AssertHeadingAndLocales(tags, surface, "STranslate_Plugin_Tts_FishAudio_Languages_More");
            var first = (HwndSource)PresentationSource.FromVisual(surface);
            tags.ClosePopup(); Drain(); tags.OpenPopup(); Drain();
            AssertEqual(false, ReferenceEquals(first, PresentationSource.FromVisual(surface)), "Language reopening owns a fresh source and heading lease");
            AssertHeading(surface, "STranslate_Plugin_Tts_FishAudio_Languages_More");
        }
        finally { tags.ClosePopup(); Drain(); }
    }

    private static void LeaseOwnershipAndFailures()
    {
        var api = new FakeTextApi { Text = "prior nonempty" };
        var live = true;
        var lease = new PopupHeadingLease((IntPtr)7, () => live, "Heading", api);
        AssertEqual("Heading", api.Text, "Initial heading replaces the bounded prior text");
        lease.Update("Localized heading");
        AssertEqual("Localized heading", api.Text, "Locale updates the same live lease");
        lease.End(); lease.Update("retired");
        AssertEqual("prior nonempty", api.Text, "Retirement restores the exact nonempty prior text once");
        AssertEqual(3, api.Writes, "Retired updates cannot write again");

        api = new FakeTextApi { Text = "old" };
        lease = new PopupHeadingLease((IntPtr)7, () => live, "Heading", api);
        api.Text = "other writer";
        lease.Update("Localized"); lease.End();
        AssertEqual("other writer", api.Text, "Another writer is retained across update and restore");
        AssertEqual(1, api.Writes, "Ownership loss authorizes no later writes");

        api = new FakeTextApi();
        lease = new PopupHeadingLease((IntPtr)7, () => live, "Heading", api);
        live = false; var calls = api.Calls;
        lease.Update("Localized"); lease.End();
        AssertEqual(calls, api.Calls, "Destroyed sources receive no read, update or restore call");
        live = true;

        foreach (var stage in new[] { "initial-read", "initial-write", "update-read", "update-write", "restore-read", "restore-write" })
        {
            var original = new IOException("Injected optional heading failure");
            api = new FakeTextApi { Text = "prior" };
            if (stage == "initial-read") api.ReadFailure = original;
            if (stage == "initial-write") api.WriteFailure = original;
            lease = new PopupHeadingLease((IntPtr)7, () => live, "Heading", api);
            if (stage == "update-read") api.ReadFailure = original;
            if (stage == "update-write") api.WriteFailure = original;
            lease.Update("Localized");
            if (stage == "restore-read") api.ReadFailure = original;
            if (stage == "restore-write") api.WriteFailure = original;
            lease.End();
            AssertEqual(true, ReferenceEquals(original, stage.StartsWith("restore", StringComparison.Ordinal) ? lease.CleanupFailure : lease.Failure), "Optional failure keeps its original identity at the correct stage");
        }
        var firstFailure = new IOException("Initial heading write");
        api = new FakeTextApi();
        lease = new PopupHeadingLease((IntPtr)7, () => live, "Heading", api);
        api.WriteFailure = firstFailure; lease.Update("Localized");
        var cleanupFailure = new IOException("Later cleanup");
        api.ReadFailure = cleanupFailure; lease.End();
        AssertEqual(true, ReferenceEquals(firstFailure, lease.Failure), "Cleanup cannot replace the first heading failure");
        AssertEqual(true, ReferenceEquals(cleanupFailure, lease.CleanupFailure), "The independent cleanup failure retains its own identity");
    }

    private static void HeadingFailureDoesNotAffectMaterialOrInput()
    {
        foreach (var stage in new[] { "read", "write", "update" })
        {
            var surface = new Border { Width = 140, Height = 100 };
            surface.Resources["Heading"] = "Existing heading";
            surface.Resources["FlyoutPresenterBackground"] = Brushes.White;
            var input = new Button { Content = "Input" }; surface.Child = input;
            using var owner = Attach(new Border());
            var popup = new Popup { Child = surface, PlacementTarget = owner.RootVisual as UIElement, StaysOpen = true };
            var api = new FakeTextApi();
            var failure = new IOException("Optional heading failure");
            if (stage == "read") api.ReadFailure = failure;
            if (stage == "write") api.WriteFailure = failure;
            var material = new PopupMaterial(popup, surface, new AcceptedBackdropApi(), "Heading", api);
            var clicks = 0; input.Click += (_, _) => clicks++;
            try
            {
                material.Begin(); popup.IsOpen = true; Drain();
                if (stage == "update") { api.WriteFailure = failure; surface.Resources["Heading"] = "Localized heading"; Drain(); }
                AssertEqual(true, material.AttributeAccepted, "Optional heading failures cannot remove accepted material");
                AssertEqual<Exception?>(null, material.Failure, "Optional heading failures do not replace the material failure owner");
                input.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertEqual(1, clicks, "Heading failure leaves popup input usable");
                AssertEqual(true, popup.IsOpen, "Heading failure cannot close the popup");
            }
            finally { popup.IsOpen = false; material.End(); Drain(); }
        }
    }

    private static void SourceReplacementAndDestroyedRetirement()
    {
        var surface = new Border(); surface.Resources["Heading"] = "Heading";
        var api = new FakeTextApi { Text = "first prior" };
        var heading = new PopupNativeHeading(surface, "Heading", api);
        using var first = Attach(new Border());
        using var second = Attach(new Border());
        var native = new NativePopupWindowTextApi();
        var prior = native.Read(first.Handle);
        try
        {
            native.Write(first.Handle, new string('x', 4095));
            AssertEqual(4095, native.Read(first.Handle).Length, "Native reads retain the complete maximum accepted prior text");
            native.Write(first.Handle, new string('x', 4096));
            var rejected = false;
            try { native.Read(first.Handle); } catch (InvalidOperationException) { rejected = true; }
            AssertEqual(true, rejected, "Native reads reject over-bound text instead of retaining a truncated prior value");
        }
        finally { native.Write(first.Handle, prior); }
        var nativeState = WindowState(first.Handle);
        var nativeHeading = new PopupNativeHeading(surface, "Heading", native);
        nativeHeading.Begin();
        try
        {
            nativeHeading.Attach(first);
            AssertEqual("Heading", native.Read(first.Handle), "Native lease uses the existing dynamic heading");
            AssertEqual(nativeState, WindowState(first.Handle), "Setting native heading preserves styles, bounds and owner");
        }
        finally { nativeHeading.End(); }
        AssertEqual(prior, native.Read(first.Handle), "Ending the live native lease restores its nonempty prior identity exactly");
        AssertEqual(nativeState, WindowState(first.Handle), "Restoring native heading preserves styles, bounds and owner");
        heading.Begin();
        try
        {
            heading.Attach(first);
            heading.Attach(second);
            AssertEqual("Heading", api.Text, "Replacement acquires a fresh source lease after prior restoration");
            AssertEqual(second.Handle, api.LastHandle, "Replacement updates target only its fresh source");
            first.Dispose();
            surface.Resources["Heading"] = "Localized"; Drain();
            AssertEqual(second.Handle, api.LastHandle, "Stale source retirement cannot update its successor");
            second.Dispose(); var calls = api.Calls;
            surface.Resources["Heading"] = "Destroyed update"; Drain(); heading.End();
            AssertEqual(calls, api.Calls, "Destroyed source retirement performs no native call");
            surface.Resources["Heading"] = "Retired update"; Drain();
            AssertEqual(calls, api.Calls, "Retired resource callbacks stay detached");
        }
        finally { heading.End(); }
    }

    private static void AssertHeadingAndLocales(FrameworkElement root, Border surface, string key)
    {
        var source = (HwndSource)PresentationSource.FromVisual(surface);
        var handle = source.Handle;
        var style = GetWindowLongPtrW(handle, -16); var extended = GetWindowLongPtrW(handle, -20);
        var owner = GetWindow(handle, 4);
        foreach (var locale in UiLocales.All)
        {
            var previous = root.Resources.MergedDictionaries.SingleOrDefault(item => item.Source?.LocalPath.Replace('\\', '/').Contains("/Languages/", StringComparison.Ordinal) == true);
            if (previous is not null) root.Resources.MergedDictionaries.Remove(previous);
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
            Drain();
            AssertHeading(surface, key);
            AssertEqual(true, ReferenceEquals(source, PresentationSource.FromVisual(surface)), "Locale updates keep the same presentation source");
            AssertEqual(style, GetWindowLongPtrW(handle, -16), "Native text does not add visible caption styles");
            AssertEqual(extended, GetWindowLongPtrW(handle, -20), "Native text preserves nonactivation/tool-window semantics");
            AssertEqual(owner, GetWindow(handle, 4), "Native text preserves the existing owner");
        }
        SetWindowTextW(handle, "other writer");
        var last = root.Resources.MergedDictionaries.Last(); root.Resources.MergedDictionaries.Remove(last); root.Resources.MergedDictionaries.Add(last); Drain();
        AssertEqual("other writer", ReadText(handle), "External native text is not overwritten by later resource evaluation");
    }

    private sealed class FakeTextApi : IPopupWindowTextApi
    {
        internal string Text = "";
        internal Exception? ReadFailure, WriteFailure;
        internal int Calls, Writes;
        internal IntPtr LastHandle;
        public string Read(IntPtr handle) { Calls++; LastHandle = handle; if (ReadFailure is not null) throw ReadFailure; return Text; }
        public void Write(IntPtr handle, string text) { Calls++; LastHandle = handle; if (WriteFailure is not null) throw WriteFailure; Writes++; Text = text; }
    }
    private sealed class AcceptedBackdropApi : IPopupBackdropApi
    {
        public bool IsSupported => true;
        public PopupBackdropResult Apply(IntPtr handle, bool dark) => new(0, 0, 2);
        public void UpdateChrome(IntPtr handle, bool dark) { }
        public void Remove(IntPtr handle) { }
    }

    private static void AssertHeading(Border surface, string key)
    {
        var source = (HwndSource)PresentationSource.FromVisual(surface);
        AssertEqual((string)surface.FindResource(key), ReadText(source.Handle), "The compiled popup HWND uses its existing localized heading as native identity");
    }
    private static HwndSource Attach(FrameworkElement view)
    {
        var source = new HwndSource(new HwndSourceParameters("Popup heading regression owner") { Width = 480, Height = 1600, WindowStyle = unchecked((int)0x80000000), PositionX = 120, PositionY = 120 });
        source.RootVisual = view;
        SettingsLayoutSuite.Layout(view, 480);
        return source;
    }
    private static (IntPtr Style, IntPtr ExtendedStyle, IntPtr Owner, int Left, int Top, int Right, int Bottom) WindowState(IntPtr handle)
    {
        AssertEqual(true, GetWindowRect(handle, out var bounds), "The live source exposes native bounds");
        return (GetWindowLongPtrW(handle, -16), GetWindowLongPtrW(handle, -20), GetWindow(handle, 4), bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    private static string ReadText(IntPtr handle) { var text = new StringBuilder(4097); GetWindowTextW(handle, text, text.Capacity); return text.ToString(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr handle, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr handle, StringBuilder text, int maximum);
}
