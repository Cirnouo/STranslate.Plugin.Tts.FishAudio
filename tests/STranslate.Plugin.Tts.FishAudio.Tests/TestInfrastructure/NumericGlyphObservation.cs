using System.Windows;
using System.Windows.Media;
using static TestAssertions;

// Drawing/run boundaries are a formatter detail. Observe every Unicode cluster
// and glyph, retaining its actual font, drawing paint and transformed placement.
internal static class NumericGlyphObservation
{
    internal sealed record Glyph(int CharacterStart, string Text, ushort Index, Point Baseline, Point Origin,
        Vector Advance, GlyphTypeface Typeface, double EmSize, int BidiLevel, bool IsSideways,
        Brush Brush, double DrawingOpacity, Matrix Transform);

    internal static Glyph[] Read(Visual owner, Visual root)
    {
        var glyphs = new List<Glyph>();
        var characterStart = 0;
        var opacity = 1d;
        for (var parent = VisualTreeHelper.GetParent(owner); parent is Visual visual && parent != root; parent = VisualTreeHelper.GetParent(parent))
            opacity *= VisualTreeHelper.GetOpacity(visual);
        foreach (var placement in Runs(owner, root, opacity))
        {
            var run = placement.Drawing.GlyphRun;
            AssertEqual(true, run.Characters is { Count: > 0 }, "Numeric drawing must retain original Unicode characters");
            var text = new string(run.Characters!.ToArray());
            var map = run.ClusterMap?.ToArray() ?? Enumerable.Range(0, text.Length).Select(index => checked((ushort)index)).ToArray();
            AssertEqual(text.Length, map.Length, "Every numeric character must have a glyph cluster");
            var clusters = map.Select((glyph, character) => (Glyph: (int)glyph, Character: character))
                .GroupBy(item => item.Glyph).OrderBy(group => group.Key).ToArray();
            AssertEqual(0, clusters[0].Key, "Numeric clusters must cover the first glyph");
            var advance = 0d;
            for (var index = 0; index < run.GlyphIndices.Count; index++)
            {
                var cluster = clusters.Last(group => group.Key <= index);
                var start = cluster.Min(item => item.Character);
                AssertEqual(cluster.Count(), cluster.Max(item => item.Character) - start + 1, "Numeric cluster characters must be contiguous");
                var baseline = run.BaselineOrigin + new Vector(advance, 0);
                var offset = run.GlyphOffsets is { Count: > 0 } ? run.GlyphOffsets[index] : new Point();
                glyphs.Add(new(characterStart + start, text.Substring(start, cluster.Count()), run.GlyphIndices[index],
                    placement.Transform.Transform(baseline), placement.Transform.Transform(baseline + new Vector(offset.X, -offset.Y)),
                    placement.Transform.Transform(new Vector(run.AdvanceWidths[index], 0)), run.GlyphTypeface,
                    run.FontRenderingEmSize, run.BidiLevel, run.IsSideways, placement.Drawing.ForegroundBrush,
                    placement.Opacity, placement.Transform));
                advance += run.AdvanceWidths[index];
            }
            AssertEqual(true, clusters.All(cluster => cluster.Key < run.GlyphIndices.Count), "Numeric clusters must not point beyond the glyph tail");
            characterStart += text.Length;
        }
        return glyphs.ToArray();
    }

    internal static void AssertText(Glyph[] glyphs, string expected, string context)
    {
        var clusters = glyphs.GroupBy(glyph => glyph.CharacterStart).OrderBy(group => group.Key).ToArray();
        var position = 0;
        foreach (var cluster in clusters)
        {
            AssertEqual(position, cluster.Key, context + ": complete Unicode cluster coverage");
            var texts = cluster.Select(glyph => glyph.Text).Distinct().ToArray();
            AssertEqual(1, texts.Length, context + ": consistent Unicode cluster mapping");
            position += texts[0].Length;
        }
        AssertEqual(expected, string.Concat(clusters.Select(group => group.Select(glyph => glyph.Text).Distinct().Single())), context + ": complete Unicode text including sign and decimal separator");
    }

    internal static Glyph[] Slice(Glyph[] glyphs, int start, int length, string expected, string context)
    {
        AssertEqual(false, glyphs.Any(glyph => glyph.CharacterStart < start && glyph.CharacterStart + glyph.Text.Length > start ||
            glyph.CharacterStart < start + length && glyph.CharacterStart + glyph.Text.Length > start + length),
            context + ": phrase boundary must not discard part of a native cluster");
        var selected = glyphs.Where(glyph => glyph.CharacterStart >= start && glyph.CharacterStart < start + length)
            .Select(glyph => glyph with { CharacterStart = glyph.CharacterStart - start }).ToArray();
        AssertText(selected, expected, context);
        return selected;
    }

    internal static void Compare(Glyph[] actual, Glyph[] native, Vector translation, string context)
    {
        AssertEqual(native.Length, actual.Length, context + ": complete glyph count including every tail");
        for (var index = 0; index < native.Length; index++)
        {
            var a = actual[index];
            var n = native[index];
            var item = $"{context}/glyph-{index}";
            AssertEqual(n.CharacterStart, a.CharacterStart, item + ": Unicode cluster position");
            AssertEqual(n.Text, a.Text, item + ": Unicode cluster text");
            AssertEqual(n.Index, a.Index, item + ": native glyph index");
            AssertEqual(n.EmSize, a.EmSize, item + ": native em size");
            AssertEqual(true, n.Typeface.FontUri == a.Typeface.FontUri, item + ": native typeface");
            AssertEqual(n.Typeface.StyleSimulations, a.Typeface.StyleSimulations, item + ": native font simulations");
            AssertEqual(n.Typeface.Style, a.Typeface.Style, item + ": native style");
            AssertEqual(n.Typeface.Weight, a.Typeface.Weight, item + ": native weight");
            AssertEqual(n.Typeface.Stretch, a.Typeface.Stretch, item + ": native stretch");
            AssertEqual(n.BidiLevel, a.BidiLevel, item + ": native bidi level");
            AssertEqual(n.IsSideways, a.IsSideways, item + ": native glyph orientation");
            AssertEqual(true, a.Brush is SolidColorBrush && n.Brush is SolidColorBrush, item + ": native solid foreground");
            AssertEqual(((SolidColorBrush)n.Brush).Color, ((SolidColorBrush)a.Brush).Color, item + ": native foreground including alpha");
            AssertEqual(n.Brush.Opacity, a.Brush.Opacity, item + ": native brush opacity");
            AssertEqual(n.DrawingOpacity, a.DrawingOpacity, item + ": native drawing opacity");
            Near(n.Baseline.X + translation.X, a.Baseline.X, item + ": native horizontal baseline placement");
            Near(n.Baseline.Y + translation.Y, a.Baseline.Y, item + ": native baseline");
            Near(n.Origin.X + translation.X, a.Origin.X, item + ": native glyph horizontal offset");
            Near(n.Origin.Y + translation.Y, a.Origin.Y, item + ": native glyph vertical offset");
            Near(n.Advance.X, a.Advance.X, item + ": native horizontal advance");
            Near(n.Advance.Y, a.Advance.Y, item + ": native vertical advance");
            AssertEqual((n.Transform.M11, n.Transform.M12, n.Transform.M21, n.Transform.M22),
                (a.Transform.M11, a.Transform.M12, a.Transform.M21, a.Transform.M22), item + ": native glyph transform");
        }
    }

    internal static string Describe(Glyph[] glyphs) => string.Join("; ", glyphs.Select(glyph =>
        FormattableString.Invariant($"char={glyph.CharacterStart}:{string.Join(",", glyph.Text.Select(character => $"U+{(int)character:X4}"))} glyph={glyph.Index} baseline={glyph.Baseline.X:F5},{glyph.Baseline.Y:F5} origin={glyph.Origin.X:F5},{glyph.Origin.Y:F5} em={glyph.EmSize} font={string.Join(",", glyph.Typeface.FamilyNames.Values.Distinct())}/{Path.GetFileName(glyph.Typeface.FontUri.LocalPath)}/{glyph.Typeface.Style}/{glyph.Typeface.Weight}/{glyph.Typeface.Stretch} brush={glyph.Brush} brushOpacity={glyph.Brush.Opacity} drawingOpacity={glyph.DrawingOpacity}")));

    private static IEnumerable<(GlyphRunDrawing Drawing, Matrix Transform, double Opacity)> Runs(Visual owner, Visual root, double opacity = 1)
    {
        var mapping = owner.TransformToAncestor(root);
        var origin = mapping.Transform(new Point());
        var x = mapping.Transform(new Point(1, 0)) - origin;
        var y = mapping.Transform(new Point(0, 1)) - origin;
        var transform = new Matrix(x.X, x.Y, y.X, y.Y, origin.X, origin.Y);
        opacity *= VisualTreeHelper.GetOpacity(owner);
        foreach (var drawing in Drawings(VisualTreeHelper.GetDrawing(owner), transform, opacity)) yield return drawing;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
            if (VisualTreeHelper.GetChild(owner, index) is Visual child)
                foreach (var drawing in Runs(child, root, opacity)) yield return drawing;
    }

    private static IEnumerable<(GlyphRunDrawing Drawing, Matrix Transform, double Opacity)> Drawings(Drawing? drawing, Matrix transform, double opacity)
    {
        if (drawing is GlyphRunDrawing glyph) yield return (glyph, transform, opacity);
        if (drawing is DrawingGroup group)
        {
            var childTransform = group.Transform?.Value ?? Matrix.Identity;
            childTransform.Append(transform);
            foreach (var child in group.Children)
                foreach (var placement in Drawings(child, childTransform, opacity * group.Opacity)) yield return placement;
        }
    }

    private static void Near(double expected, double actual, string context) =>
        AssertEqual(true, Math.Abs(expected - actual) < .01, $"{context}: expected={expected:F5}, actual={actual:F5}");
}
