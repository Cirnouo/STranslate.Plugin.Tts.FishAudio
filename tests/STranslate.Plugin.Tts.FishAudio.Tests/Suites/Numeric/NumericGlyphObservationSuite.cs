using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class NumericGlyphObservationSuite
{
    // A native line may change grouping without changing any visible glyph.
    // This catches an observer that rejects a separately shaped sign or tail.
    internal static Task EquivalentNativeRunGroupingAsync() => RunOnStaThreadAsync(() =>
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            MeasureGrouping();
        }
        finally { CultureInfo.CurrentCulture = original; }
    });

    private static void MeasureGrouping()
    {
        var settings = new Settings { Volume = -10 };
        using var vm = new SettingsViewModel(CreateContext(settings: settings), settings);
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        SettingsLayoutSuite.Layout(view);
        var editor = Visuals(view).OfType<InlineNumericEditor>().Single(item => item.Unit == "dB");
        var button = (Button)editor.FindName("DisplayButton");
        var number = Visuals(button).OfType<TextBlock>().Single();
        var unit = (TextBlock)editor.FindName("UnitText");
        var nativeNumbers = NumericGlyphObservation.Read(number, editor);
        var nativeUnits = NumericGlyphObservation.Read(unit, editor);
        GroupNative(number);
        GroupNative(unit);
        SettingsLayoutSuite.Layout(view);
        var runs = Drawings(VisualTreeHelper.GetDrawing(number)).ToArray();
        Console.WriteLine($"GROUPING controlled-native number={number.Text} runs={runs.Length} " +
            string.Join("; ", runs.Select(run => $"chars={new string(run.GlyphRun.Characters.ToArray())} glyphs={string.Join(",", run.GlyphRun.GlyphIndices)} baseline={run.GlyphRun.BaselineOrigin} em={run.GlyphRun.FontRenderingEmSize} family={string.Join(",", run.GlyphRun.GlyphTypeface.FamilyNames.Values.Distinct())}")));
        AssertEqual(true, runs.Length > 1, "Controlled native language spans must expose multiple glyph runs");
        AssertEqual(true, Drawings(VisualTreeHelper.GetDrawing(unit)).Count() > 1, "Controlled native suffix must expose a separate trailing fragment");
        var actualNumbers = NumericGlyphObservation.Read(number, editor);
        var actualUnits = NumericGlyphObservation.Read(unit, editor);
        NumericGlyphObservation.AssertText(actualNumbers, "-10.00", "controlled split sign/decimal/tail");
        NumericGlyphObservation.AssertText(actualUnits, "dB", "controlled split unit tail");
        NumericGlyphObservation.Compare(actualNumbers, nativeNumbers, new Vector(), "equivalent native number grouping");
        NumericGlyphObservation.Compare(actualUnits, nativeUnits, new Vector(), "equivalent native unit grouping");
        NumericTypographySuite.AssertResting(editor, number, unit, "controlled-native-grouping");
        var splitReference = new NumericPhraseReference(editor, number, unit, GroupNative);
        splitReference.AssertMatches("controlled-native-reference-grouping");
        AssertSensitivity(actualNumbers, nativeNumbers, actualUnits, nativeUnits, VisualTreeHelper.GetDpi(number).PixelsPerDip);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SettingsLayoutSuite.Layout(view);
        var input = (TextBox)editor.FindName("EditorBox");
        NumericTypographySuite.AssertEditing(editor, input, unit, "controlled-native-grouping");
        var viewport = Visuals(input).OfType<Visual>().Single(element => element.GetType().Name == "TextBoxView");
        splitReference.AssertEditingMatches(input, viewport, "controlled-native-reference-grouping");
        Console.WriteLine("GROUPING complete sign/decimal/tail and font/paint/placement sensitivity passed in resting and editing observers.");
    }

    private static void GroupNative(TextBlock line)
    {
        var spans = line.Inlines.Cast<Run>().Select(run => (run.Text, run.FontSize, run.Foreground)).ToArray();
        line.Inlines.Clear();
        var index = 0;
        foreach (var span in spans)
        foreach (var character in span.Text)
            line.Inlines.Add(new Run(character.ToString())
            {
                FontSize = span.FontSize, Foreground = span.Foreground,
                Language = XmlLanguage.GetLanguage(index++ % 2 == 0 ? "en-US" : "en-GB"),
            });
    }

    private static void AssertSensitivity(NumericGlyphObservation.Glyph[] numbers, NumericGlyphObservation.Glyph[] nativeNumbers,
        NumericGlyphObservation.Glyph[] units, NumericGlyphObservation.Glyph[] nativeUnits, double dpi)
    {
        void Compare(NumericGlyphObservation.Glyph[] changed, string reason) =>
            ExpectMismatch(() => NumericGlyphObservation.Compare(changed, nativeNumbers, new Vector(), "number sensitivity"), reason);
        NumericGlyphObservation.Glyph[] Change(int index, Func<NumericGlyphObservation.Glyph, NumericGlyphObservation.Glyph> mutate)
        {
            var changed = numbers.ToArray();
            changed[index] = mutate(changed[index]);
            return changed;
        }
        Compare(numbers[1..], "complete glyph count");
        Compare(numbers[..^1], "complete glyph count");
        ExpectMismatch(() => NumericGlyphObservation.Compare(numbers, [.. nativeNumbers, nativeNumbers[^1]], new Vector(), "native trailing glyph"), "complete glyph count");
        ExpectMismatch(() => NumericGlyphObservation.AssertText(numbers[1..], "-10.00", "sign sensitivity"), "complete Unicode cluster coverage");
        ExpectMismatch(() => NumericGlyphObservation.AssertText(numbers.Where(glyph => glyph.Text != ".").ToArray(), "-10.00", "decimal sensitivity"), "complete Unicode cluster coverage");
        Compare(Change(0, glyph => glyph with { Baseline = glyph.Baseline + new Vector(0, .02) }), "native baseline");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Baseline = glyph.Baseline + new Vector(.02, 0) }), "horizontal baseline placement");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Index = checked((ushort)(glyph.Index + 1)) }), "native glyph index");
        Compare(Change(numbers.Length - 1, glyph => glyph with { EmSize = glyph.EmSize + 1 }), "native em size");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Origin = glyph.Origin + new Vector(0, .02) }), "vertical offset");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Advance = glyph.Advance + new Vector(.02, 0) }), "horizontal advance");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Brush = new SolidColorBrush(Colors.Red) }), "foreground including alpha");
        var translucent = (SolidColorBrush)numbers[^1].Brush.Clone();
        var color = translucent.Color;
        translucent.Color = Color.FromArgb(color.A == 255 ? (byte)254 : (byte)255, color.R, color.G, color.B);
        Compare(Change(numbers.Length - 1, glyph => glyph with { Brush = translucent }), "foreground including alpha");
        var faded = numbers[^1].Brush.Clone();
        faded.Opacity = .5;
        Compare(Change(numbers.Length - 1, glyph => glyph with { Brush = faded }), "brush opacity");
        Compare(Change(numbers.Length - 1, glyph => glyph with { DrawingOpacity = .5 }), "drawing opacity");
        GlyphTypeface? alternate = null;
        foreach (var family in new[] { "Arial", "Times New Roman" })
            if (new Typeface(family).TryGetGlyphTypeface(out var candidate) && candidate.FontUri != numbers[^1].Typeface.FontUri)
            {
                alternate = candidate;
                break;
            }
        AssertNotNull(alternate, "Typeface sensitivity needs an actual distinct Windows font");
        Compare(Change(numbers.Length - 1, glyph => glyph with { Typeface = alternate! }), "native typeface");
        var simulated = new GlyphTypeface(numbers[^1].Typeface.FontUri, StyleSimulations.BoldSimulation);
        Compare(Change(numbers.Length - 1, glyph => glyph with { Typeface = simulated }), "native font simulations");
        var shiftedTail = units.ToArray();
        var threePixels = 3 / dpi;
        shiftedTail[^1] = shiftedTail[^1] with { Baseline = shiftedTail[^1].Baseline + new Vector(0, threePixels) };
        ExpectMismatch(() => NumericGlyphObservation.Compare(shiftedTail, nativeUnits, new Vector(), "separate suffix tail displacement"), "native baseline");
        shiftedTail[^1] = units[^1] with { Baseline = units[^1].Baseline + new Vector(threePixels, 0) };
        ExpectMismatch(() => NumericGlyphObservation.Compare(shiftedTail, nativeUnits, new Vector(), "separate suffix tail displacement"), "horizontal baseline placement");
        Console.WriteLine("SENSITIVITY rejected omitted sign/decimal/tails, extra native tail, sign/tail shifts, glyph/font/em/offset/advance/brush/opacity mismatches and +3px suffix-tail shifts.");
    }

    private static void ExpectMismatch(Action compare, string reason)
    {
        try { compare(); }
        catch (InvalidOperationException exception) when (exception.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Numeric observer accepted mutation: " + reason);
    }

    private static IEnumerable<GlyphRunDrawing> Drawings(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in Drawings(child)) yield return run;
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
}
