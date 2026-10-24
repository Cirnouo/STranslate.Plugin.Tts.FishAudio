using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static TestAssertions;

internal static class NumericAlignmentLifecycleSuite
{
    internal static Task RunAsync() => StaTestHost.RunOnStaThreadAsync(() =>
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var settings = new Settings { Volume = -2 };
            var context = ContextProxy.CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            using var vm = new SettingsViewModel(context, settings);
            var view = SettingsLayoutSuite.CreateThemedView(vm);
            // LayoutUpdated is dispatcher-wide: a newly constructed logical
            // control may not yet have an applied visual tree at all.
            var unmaterialized = new InlineNumericEditor { Unit = "dB", Minimum = -10, Value = -10 };
            SettingsLayoutSuite.Layout(view);
            AssertEqual(new Size(), unmaterialized.RenderSize, "Unmaterialized peer must remain unmeasured during another view's layouts");
            var editor = Descendants(view).OfType<InlineNumericEditor>().Single(e => e.Unit == "dB");
            var button = (Button)editor.FindName("DisplayButton");
            var input = (TextBox)editor.FindName("EditorBox");
            var unit = (TextBlock)editor.FindName("UnitText");
            var oldFont = NumericGlyphObservation.Read(button, editor)[0].Typeface.FontUri;
            var unchangedText = button.Content.ToString();
            // Existing nonempty old-font drawing, same number. Queue multiple
            // changes before the lower-priority alignment callback can publish.
            button.FontFamily = new FontFamily("Arial");
            input.FontFamily = new FontFamily("Arial");
            view.UpdateLayout();
            button.FontStyle = FontStyles.Italic;
            input.FontStyle = FontStyles.Italic;
            view.UpdateLayout();
            SettingsLayoutSuite.Layout(view);
            AssertEqual(unchangedText, button.Content.ToString(), "Font readiness must be tested with the same nonempty string");
            AssertEqual(false, oldFont == NumericGlyphObservation.Read(button, editor)[0].Typeface.FontUri,
                "Readiness scenario must actually replace the native fallback typeface");
            NumericBottomSuite.AssertAligned(button, unit, editor, "same-text/new-native-font");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettingsLayoutSuite.Layout(view);
            input.Text = "-10,00";
            view.UpdateLayout();
            input.Text = "-0.01";
            view.UpdateLayout();
            input.Text = "0,00";
            SettingsLayoutSuite.Layout(view);
            NumericGlyphObservation.AssertText(NumericGlyphObservation.Read(input, editor), "0,00", "latest queued draft");
            NumericBottomSuite.AssertAligned(input, unit, editor, "latest queued draft");
            AssertEqual(0, storage.SaveCount, "Rapid noncommitting drafts and font changes must never save");
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            SettingsLayoutSuite.Layout(view);

            // Explicit unhosted lifecycle events test ownership; not installed-host evidence.
            editor.Value = -.01;
            view.UpdateLayout();
            editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, editor));
            var retiredShift = ((TranslateTransform)unit.RenderTransform).Y;
            editor.Value = -10;
            SettingsLayoutSuite.Layout(view);
            AssertEqual(retiredShift, ((TranslateTransform)unit.RenderTransform).Y, "Retired callback must not publish after unload");
            editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, editor));
            SettingsLayoutSuite.Layout(view);
            NumericBottomSuite.AssertAligned(button, unit, editor, "reload newest committed value");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettingsLayoutSuite.Layout(view);
            var committedAnchor = ((TranslateTransform)unit.RenderTransform).Y;
            foreach (var text in new[] { "", "-", "," })
            {
                input.Text = text;
                SettingsLayoutSuite.Layout(view);
                AssertEqual(committedAnchor, ((TranslateTransform)unit.RenderTransform).Y, "Reloaded partial draft uses latest committed anchor");
            }
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakePresentationSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            SettingsLayoutSuite.Layout(view);

            button.Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                  <Border Padding="2,0" BorderThickness="1"><ContentPresenter VerticalAlignment="Bottom" HorizontalAlignment="Right" /></Border>
                </ControlTemplate>
                """);
            SettingsLayoutSuite.Layout(view);
            NumericBottomSuite.AssertAligned(button, unit, editor, "replacement template numeric position");
            var stableShift = ((TranslateTransform)unit.RenderTransform).Y;
            var loadedOperations = 0;
            DispatcherHookEventHandler posted = (_, args) => { if (args.Operation.Priority == DispatcherPriority.Loaded) loadedOperations++; };
            Dispatcher.CurrentDispatcher.Hooks.OperationPosted += posted;
            try
            {
                for (var i = 0; i < 5; i++) SettingsLayoutSuite.Layout(view);
            }
            finally { Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= posted; }
            AssertEqual(stableShift, ((TranslateTransform)unit.RenderTransform).Y, "Stable layouts must not accumulate unit displacement");
            AssertEqual(0, loadedOperations, "Unchanged contexts and render-only translation must not queue alignment repeatedly");
            Console.WriteLine("Numeric alignment readiness/lifecycle passed: same-text new font, rapid draft replacement, retired callback, reload, template position and no drift/requeue.");
        }
        finally { CultureInfo.CurrentCulture = original; }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
