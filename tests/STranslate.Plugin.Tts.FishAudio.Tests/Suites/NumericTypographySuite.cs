using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;

internal static class NumericTypographySuite
{
    internal static async Task RunAsync(string? proofDirectory)
    {
        if (proofDirectory is not null)
        {
            Directory.CreateDirectory(proofDirectory);
            var assembly = typeof(InlineNumericEditor).Assembly;
            File.WriteAllText(Path.Combine(proofDirectory, "proof-assembly.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                fixture = "Offline compiled native theme; not installed-host acceptance",
                assembly = Path.GetFileName(assembly.Location),
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                mvid = assembly.ManifestModule.ModuleVersionId,
                dpi = 1.25,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (var culture in new[] { "en-US", "de-DE" })
        foreach (var dark in new[] { false, true })
            await RunOnStaThreadAsync(() =>
            {
                var original = CultureInfo.CurrentCulture;
                try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); Measure(dark, proofDirectory); }
                finally { CultureInfo.CurrentCulture = original; }
            });
    }

    private static void Measure(bool dark, string? proofDirectory)
    {
        var settings = new Settings { Speed = .87, Volume = -2 };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        SettingsLayoutSuite.Layout(view);
        var editors = Visuals(view).OfType<InlineNumericEditor>().ToArray();
        foreach (var editor in editors)
        {
            Click((Button)editor.FindName("DisplayButton"));
            SettingsLayoutSuite.Layout(view);
            PressKey((TextBox)editor.FindName("EditorBox"), System.Windows.Input.Key.Escape);
        }
        typeof(VisualTreeHelper).GetMethod("SetRootDpi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [view, new DpiScale(1.25, 1.25)]);
        SettingsLayoutSuite.Layout(view);
        var contracts = editors.Select(editor => (Editor: editor, Unit: (TextBlock)editor.FindName("UnitText")))
            .Where(pair => pair.Unit.Visibility == Visibility.Visible).ToArray();
        var contractFailures = contracts.Where(pair => pair.Unit.FontSize != 11 ||
            !Equals(pair.Unit.Foreground, pair.Editor.FindResource("TextFillColorSecondaryBrush")))
            .Select(pair => $"{pair.Unit.Text}: size={pair.Unit.FontSize}, secondary={Equals(pair.Unit.Foreground, pair.Editor.FindResource("TextFillColorSecondaryBrush"))}").ToArray();
        TestAssertions.AssertEqual("", string.Join("; ", contractFailures),
            "Both descriptive units must use 11 DIP and the secondary theme brush");
        foreach (var sample in new[] { (Speed: .87, Volume: -2d), (Speed: 1d, Volume: -10d) })
        {
            vm.Speed = sample.Speed;
            vm.Volume = sample.Volume;
            SettingsLayoutSuite.Layout(view);
            foreach (var editor in editors)
            {
                var property = BindingOperations.GetBinding(editor, InlineNumericEditor.ValueProperty)!.Path.Path;
                var button = (Button)editor.FindName("DisplayButton");
                var unit = Visuals(editor).OfType<TextBlock>().Single(block => block.Name == "UnitText");
                var label = Visuals(button).OfType<TextBlock>().Single();
                Console.WriteLine($"METRICS {(dark ? "dark" : "light")}/{property} button={Font(button)} label={Font(label)} unit={Font(unit)} unitMargin={unit.Margin} labelBox={Bounds(label, editor)} unitBox={Bounds(unit, editor)}");
                DumpGlyphs(label, editor, "resting");
                DumpGlyphs(unit, editor, "unit");
                AssertResting(editor, label, unit, property);
                var reference = unit.Visibility == Visibility.Visible ? new NumericPhraseReference(editor, label, unit) : null;
                var proofName = $"{CultureInfo.CurrentCulture.Name}-{(dark ? "dark" : "light")}-{property}-{label.Text}";
                if (proofDirectory is not null) reference?.SavePair(Path.Combine(proofDirectory, proofName + "-static.reference.png"), false);
                Click(button);
                SettingsLayoutSuite.Layout(view);
                var input = (TextBox)editor.FindName("EditorBox");
                var viewport = Visuals(input).OfType<FrameworkElement>().Single(element => element.GetType().Name == "TextBoxView");
                Console.WriteLine($"METRICS input={Font(input)} inputBox={Bounds(input, editor)} textView={Bounds(viewport, editor)} textAlignment={input.TextAlignment} dpi={VisualTreeHelper.GetDpi(viewport).DpiScaleX}");
                TestAssertions.AssertEqual(1.25, VisualTreeHelper.GetDpi(viewport).PixelsPerDip, "Proof native editor child must receive actual 125% DPI");
                DumpGlyphs(viewport, editor, "editing");
                AssertEditing(editor, input, unit, property);
                if (proofDirectory is not null) reference?.SavePair(Path.Combine(proofDirectory, proofName + "-edit.reference.png"), true);
                PressKey(input, System.Windows.Input.Key.Escape);
            }
        }
        AssertThemeTransition(view, editors, dark);
    }

    internal static void AssertThemeTransition(FrameworkElement view, InlineNumericEditor[] editors, bool dark)
    {
        var resources = view.Resources.MergedDictionaries.OfType<ThemeResources>().Single();
        foreach (var targetDark in new[] { !dark, dark })
        {
            resources.RequestedTheme = targetDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ThemeManager.SetRequestedTheme(view, targetDark ? ElementTheme.Dark : ElementTheme.Light);
            SettingsLayoutSuite.Layout(view, view.ActualWidth);
            foreach (var editor in editors)
            {
                var button = (Button)editor.FindName("DisplayButton");
                var number = Visuals(button).OfType<TextBlock>().Single();
                var unit = (TextBlock)editor.FindName("UnitText");
                AssertResting(editor, number, unit, $"theme-transition/{targetDark}");
            }
        }
    }

    internal static void AssertUnitRoutes(FrameworkElement view, InlineNumericEditor editor, TextBlock unit, Func<int> saves, string context)
    {
        if (unit.Visibility != Visibility.Visible) return;
        var point = unit.TranslatePoint(new Point(unit.ActualWidth / 2, unit.ActualHeight / 2), editor);
        var hit = VisualTreeHelper.HitTest(editor, point)?.VisualHit;
        TestAssertions.AssertEqual(unit, hit, context + ": actual suffix coordinates must hit the inert unit");
        for (var parent = VisualTreeHelper.GetParent(hit!); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            TestAssertions.AssertEqual(false, parent is Button, context + ": suffix coordinates must remain outside button ancestry");
        var beforeValue = editor.Value;
        var beforeSaves = saves();
        var input = (TextBox)editor.FindName("EditorBox");
        var target = (UIElement)hit!;
        target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
        target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent });
        target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent });
        TestAssertions.AssertEqual(Visibility.Collapsed, input.Visibility, context + ": suffix click route must not enter editing");
        var bubbles = 0;
        System.Windows.Input.MouseWheelEventHandler listener = (_, _) => bubbles++;
        view.MouseWheel += listener;
        try
        {
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            target.RaiseEvent(wheel);
            if (!wheel.Handled) { wheel.RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent; target.RaiseEvent(wheel); }
            TestAssertions.AssertEqual(false, wheel.Handled, context + ": suffix wheel must retain ordinary scrolling");
            TestAssertions.AssertEqual(1, bubbles, context + ": suffix wheel must bubble through the actual settings view");
        }
        finally { view.MouseWheel -= listener; }
        TestAssertions.AssertEqual(beforeValue, editor.Value, context + ": suffix input must not adjust the value");
        TestAssertions.AssertEqual(beforeSaves, saves(), context + ": suffix input must not save settings");
    }

    // Catches an oversized multiplication sign, a separately centered smaller
    // unit line, a font mismatch or a gap larger than ordinary word spacing.
    internal static void AssertResting(InlineNumericEditor editor, TextBlock number, TextBlock unit, string context)
    {
        var button = (Button)editor.FindName("DisplayButton");
        TestAssertions.AssertEqual(editor.FontSize, button.FontSize, $"{context}: display must inherit the native numeric size");
        TestAssertions.AssertEqual(button.FontSize, number.FontSize, $"{context}: number must retain the native font size");
        TestAssertions.AssertEqual(editor.FindResource("TextFillColorPrimaryBrush"), number.Foreground, $"{context}: number must retain primary foreground");
        if (unit.Visibility != Visibility.Visible) return;
        var numberRun = Glyphs(VisualTreeHelper.GetDrawing(number)).Single();
        var unitRun = Glyphs(VisualTreeHelper.GetDrawing(unit)).Single();
        var numberBaseline = number.TransformToAncestor(editor).Transform(numberRun.BaselineOrigin);
        var unitBaseline = unit.TransformToAncestor(editor).Transform(unitRun.BaselineOrigin);
        TestAssertions.AssertEqual(number.FontFamily, unit.FontFamily, $"{context}: number and unit must resolve the same font family");
        TestAssertions.AssertEqual(number.FontWeight, unit.FontWeight, $"{context}: number and unit must resolve the same font weight");
        TestAssertions.AssertEqual(numberRun.GlyphTypeface.FamilyNames.Values.First(), unitRun.GlyphTypeface.FamilyNames.Values.First(),
            $"{context}: actual number and unit glyphs must resolve a compatible font family");
        TestAssertions.AssertEqual(numberRun.GlyphTypeface.Weight, unitRun.GlyphTypeface.Weight,
            $"{context}: actual number and unit glyphs must resolve a compatible font weight");
        TestAssertions.AssertEqual(true, Math.Abs(numberBaseline.Y - unitBaseline.Y) < .01,
            $"{context}: unit and number must share a typographic baseline; number={numberBaseline.Y:F4}, unit={unitBaseline.Y:F4}");
        TestAssertions.AssertEqual(11d, unitRun.FontRenderingEmSize,
            $"{context}: both units must use the smaller 11-DIP descriptive size");
        TestAssertions.AssertEqual(true, unitRun.FontRenderingEmSize < numberRun.FontRenderingEmSize,
            $"{context}: both units must be smaller than the native number");
        var numberDrawing = GlyphDrawings(VisualTreeHelper.GetDrawing(number)).Single();
        var unitDrawing = GlyphDrawings(VisualTreeHelper.GetDrawing(unit)).Single();
        TestAssertions.AssertEqual(editor.FindResource("TextFillColorPrimaryBrush"), numberDrawing.ForegroundBrush,
            $"{context}: the native number must retain its primary foreground");
        TestAssertions.AssertEqual(editor.FindResource("TextFillColorSecondaryBrush"), unitDrawing.ForegroundBrush,
            $"{context}: both units must render with the dynamic secondary foreground");
        var space = new FormattedText(" ", CultureInfo.CurrentCulture, number.FlowDirection,
            new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch), number.FontSize,
            number.Foreground, VisualTreeHelper.GetDpi(number).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var gap = unitBaseline.X - Bounds(number, editor).Right;
        TestAssertions.AssertEqual(true, gap > 0 && gap <= space + .01,
            $"{context}: number and separate unit must have at most an ordinary same-font space; gap={gap:F3}, space={space:F3}");
        new NumericPhraseReference(editor, number, unit).AssertMatches(context);
        var hit = VisualTreeHelper.HitTest(editor, unit.TranslatePoint(new Point(unit.ActualWidth / 2, unit.ActualHeight / 2), editor));
        TestAssertions.AssertEqual(unit, hit?.VisualHit, $"{context}: the unit's visual hit must remain outside the numeric button");
    }

    internal static void AssertEditing(InlineNumericEditor editor, TextBox input, TextBlock unit, string context)
    {
        if (unit.Visibility != Visibility.Visible) return;
        var viewport = Visuals(input).OfType<Visual>().Single(element => element.GetType().Name == "TextBoxView");
        var number = new[] { viewport }.Concat(Visuals(viewport).OfType<Visual>())
            .SelectMany(owner => Glyphs(VisualTreeHelper.GetDrawing(owner)).Select(run => (Owner: owner, Run: run))).Single();
        TestAssertions.AssertEqual(input.FontSize, number.Run.FontRenderingEmSize, $"{context}: editor must retain its native numeric size");
        var unitDrawing = GlyphDrawings(VisualTreeHelper.GetDrawing(unit)).Single();
        TestAssertions.AssertEqual(11d, unitDrawing.GlyphRun.FontRenderingEmSize, $"{context}: editing must preserve the 11-DIP unit");
        TestAssertions.AssertEqual(editor.FindResource("TextFillColorSecondaryBrush"), unitDrawing.ForegroundBrush, $"{context}: editing must preserve the dynamic secondary unit");
        var numberBaseline = number.Owner.TransformToAncestor(editor).Transform(number.Run.BaselineOrigin);
        var unitBaseline = unit.TransformToAncestor(editor).Transform(Glyphs(VisualTreeHelper.GetDrawing(unit)).Single().BaselineOrigin);
        TestAssertions.AssertEqual(true, Math.Abs(numberBaseline.Y - unitBaseline.Y) < .01,
            $"{context}: centering the native edit draft must preserve the separate unit baseline");
    }

    private static string Font(DependencyObject element) => $"{element.GetValue(TextBlock.FontFamilyProperty)}/{element.GetValue(TextBlock.FontSizeProperty)}/{element.GetValue(TextBlock.FontWeightProperty)}/{element.GetValue(TextBlock.FontStyleProperty)}";
    private static Rect Bounds(FrameworkElement element, UIElement root) => new(element.TranslatePoint(new Point(), root), element.RenderSize);

    private static void DumpGlyphs(Visual visual, UIElement root, string name)
    {
        foreach (var glyph in Glyphs(VisualTreeHelper.GetDrawing(visual)))
            Console.WriteLine($"GLYPH {name} baseline={visual.TransformToAncestor(root).Transform(glyph.BaselineOrigin)} em={glyph.FontRenderingEmSize} family={glyph.GlyphTypeface.FamilyNames.Values.First()} weight={glyph.GlyphTypeface.Weight} lineBounds={glyph.ComputeAlignmentBox()} inkBounds={glyph.ComputeInkBoundingBox()}");
        foreach (var child in Visuals(visual).OfType<Visual>())
            foreach (var glyph in Glyphs(VisualTreeHelper.GetDrawing(child)))
                Console.WriteLine($"GLYPH {name} baseline={child.TransformToAncestor(root).Transform(glyph.BaselineOrigin)} em={glyph.FontRenderingEmSize} family={glyph.GlyphTypeface.FamilyNames.Values.First()} weight={glyph.GlyphTypeface.Weight} lineBounds={glyph.ComputeAlignmentBox()} inkBounds={glyph.ComputeInkBoundingBox()}");
    }

    private static IEnumerable<GlyphRunDrawing> GlyphDrawings(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in GlyphDrawings(child)) yield return run;
    }

    private static IEnumerable<GlyphRun> Glyphs(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in Glyphs(child)) yield return run;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void PressKey(TextBox input, System.Windows.Input.Key key) => input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new FakePresentationSource(), 0, key) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
