using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using SettingsCard = iNKORE.UI.WPF.Modern.Controls.SettingsCard;
using HyperlinkButton = iNKORE.UI.WPF.Modern.Controls.HyperlinkButton;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class ProjectRepositoryLinkSuite
{
    internal static async Task RunAllAsync()
    {
        foreach (var language in UiLocales.All)
            await RunOnStaThreadAsync(() => CompiledSupportUsesProjectRepository(language));
    }

    private static void CompiledSupportUsesProjectRepository(string language)
    {
        var settings = new Settings();
        var snackbar = new TestSnackbar();
        var logger = new TestLogger();
        var context = CreateContext(settings: settings, snackbar: snackbar, logger: logger);
        using var vm = new SettingsViewModel(context, settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{language}.xaml"), UriKind.Absolute) });
        SettingsLayoutSuite.Layout(view);
        var link = view.FindName("ProjectRepositoryLink") as Hyperlink;
        AssertNotNull(link, "Support must expose ordinary native hyperlink text for the project repository");
        AssertEqual("https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio", link!.NavigateUri.AbsoluteUri, "Support must target this plugin's repository");
        AssertEqual(true, link.Parent is TextBlock, "The link must be ordinary TextBlock content without button chrome");
        AssertEqual(true, link.Focusable, "The native hyperlink must remain keyboard focusable");
        AssertEqual(true, System.Windows.Input.KeyboardNavigation.GetIsTabStop(link), "The native hyperlink must remain in keyboard navigation");
        var text = (TextBlock)link.Parent;
        var card = (SettingsCard)view.FindName("SupportCard");
        AssertEqual(text, card.Content, "Repository text must use the native Support card content slot");
        AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Support"), card.Header, "The Support header must remain localized");
        AssertEqual(view.FindResource("STranslate_Plugin_Tts_FishAudio_Support_Description"), card.Description, "The project description must use the active locale");
        AssertNotNull(view.TryFindResource("STranslate_Plugin_Tts_FishAudio_RepositoryLink_Failed"), "Every existing locale must translate link launch failure");
        AssertEqual("GitHub", string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)), "Ordinary link text must identify GitHub");
        AssertEqual("GitHub", new HyperlinkAutomationPeer(link).GetName(), "The native hyperlink must expose its visible text to accessibility");
        var iconContainer = text.Inlines.FirstInline as InlineUIContainer;
        AssertNotNull(iconContainer, "The Octocat must appear immediately before the ordinary text link");
        AssertEqual(link, iconContainer!.NextInline, "The Octocat and text must remain adjacent");
        var icon = iconContainer.Child as System.Windows.Shapes.Path;
        AssertNotNull(icon, "The repository link must include its vector Octocat");
        AssertEqual(16d, icon!.Width, "The Octocat must retain its 16px asset viewport");
        AssertEqual(16d, icon.Height, "The Octocat must retain its 16px asset viewport");
        AssertEqual(true, icon.Data.GetArea() > 100, "The compiled Octocat geometry must contain visible filled outlines");
        AssertEqual(text.Foreground, icon.Fill, "The Octocat must inherit the native text foreground");
        AssertEqual(false, Logical(card).OfType<System.Windows.Controls.Primitives.ButtonBase>().Any(), "Support must have no text-containing button or button chrome");
        var providerLink = Visuals(view).OfType<HyperlinkButton>().Single(button => button.NavigateUri?.AbsoluteUri == "https://fish.audio/app/api-keys");
        AssertEqual("fish.audio", providerLink.Content, "The API Key card must retain its provider link");
        var referenceInput = Logical(view).OfType<ComboBox>().Single(input => BindingOperations.GetBinding(input, ComboBox.SelectedItemProperty)?.Path.Path == "SelectedLatency");
        var reference = Ancestor<SettingsCard>(referenceInput);
        foreach (var width in new[] { 400d, 600d })
        {
            SettingsLayoutSuite.Layout(view, width);
            foreach (var nativeCard in new[] { card, reference })
                AssertEqual(true, VisualStateManager.GoToState(nativeCard, "RightWrapped", false), "The fixture must apply the actual native wrapped card state");
            SettingsLayoutSuite.Layout(view, width);
            var header = Visuals(card).OfType<TextBlock>().First(block => block.Text == card.Header?.ToString());
            var referenceHeader = Visuals(reference).OfType<TextBlock>().First(block => block.Text == reference.Header?.ToString());
            Near(referenceHeader.TranslatePoint(new Point(), reference).X, header.TranslatePoint(new Point(), card).X, "Support must retain native header alignment");
            var iconElement = (FrameworkElement)card.HeaderIcon;
            var referenceIcon = (FrameworkElement)reference.HeaderIcon;
            Near(referenceIcon.TranslatePoint(new Point(), reference).X, iconElement.TranslatePoint(new Point(), card).X, "Support must retain the native header icon column");
            var contentPresenter = (FrameworkElement)VisualTreeHelper.GetParent(text);
            Near(contentPresenter.TranslatePoint(new Point(), card).X, text.TranslatePoint(new Point(), card).X, "Repository text must start at its native content inset");
            AssertEqual(true, text.TranslatePoint(new Point(text.ActualWidth, 0), view).X <= width, "Repository text must fit the native card content bounds");
            AssertEqual(true, icon.ActualWidth > 0 && text.ActualHeight >= icon.ActualHeight, "The icon and ordinary text must remain visible at narrow and normal widths");
        }

        // Runs inside native hyperlinks must retain the interactive focus path
        // when the settings view completes an editor on an outside click.
        var run = link.Inlines.OfType<Run>().Single();
        var interactive = typeof(SettingsView).GetMethod("HasInteractiveAncestor", BindingFlags.Static | BindingFlags.NonPublic)!;
        AssertEqual(true, (bool)interactive.Invoke(null, [run])!, "Text inside a native Hyperlink must preserve interactive focus handling");
        ProcessStartInfo? opened = null;
        view.RepositoryLinkLauncher = info => opened = info;
        var request = new RequestNavigateEventArgs(link.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent };
        link.RaiseEvent(request);
        AssertEqual(true, request.Handled, "The compiled hyperlink must own its navigation route");
        AssertNotNull(opened, "The compiled link must invoke the shell launch boundary");
        AssertEqual("https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio", opened!.FileName, "Only the fixed HTTPS project repository may reach the shell");
        AssertEqual(true, opened.UseShellExecute, "The native hyperlink must use shell navigation");
        opened = null;
        link.RaiseEvent(new RequestNavigateEventArgs(new Uri("https://example.invalid/untrusted"), "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
        AssertEqual<ProcessStartInfo?>(null, opened, "An unexpected routed URI must never reach the shell boundary");
        view.RepositoryLinkLauncher = _ => throw new InvalidOperationException("Synthetic private shell details");
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
        AssertEqual("STranslate_Plugin_Tts_FishAudio_RepositoryLink_Failed", snackbar.LastError, "Shell failure must produce a localized transient error");
        AssertEqual(false, logger.Contains("Synthetic private shell details"), "Shell exception details must not be logged");
        snackbar.Clear(); vm.Dispose();
        link.RaiseEvent(new RequestNavigateEventArgs(link.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
        AssertEqual<string?>(null, snackbar.LastError, "A retired ViewModel must not publish a stale error");
    }

    private static void Near(double expected, double actual, string message) => AssertEqual(true, Math.Abs(expected - actual) < .01, message);
    private static T Ancestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = LogicalTreeHelper.GetParent(source); current is not null; current = LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        throw new InvalidOperationException("Expected native card ancestor");
    }
    private static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var item in Logical(child)) yield return item; }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var item in Visuals(child)) yield return item; }
    }
}
