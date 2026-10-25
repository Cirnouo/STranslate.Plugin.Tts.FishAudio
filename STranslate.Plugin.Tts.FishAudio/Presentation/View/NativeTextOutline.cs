using System.Windows;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.View;

// Reads native shaped text for measurement only; the native controls own drawing.
internal static class NativeTextOutline
{
    internal static bool TryBottom(Visual owner, Visual root, string expected, out double bottom)
    {
        bottom = 0;
        if (!root.IsAncestorOf(owner)) return false;
        var bounds = Rect.Empty;
        var text = new System.Text.StringBuilder();
        ReadVisual(owner);
        bottom = bounds.Bottom;
        return !bounds.IsEmpty && text.ToString() == expected;

        void ReadVisual(Visual visual)
        {
            var mapping = visual.TransformToAncestor(root);
            var origin = mapping.Transform(new Point());
            var x = mapping.Transform(new Point(1, 0)) - origin;
            var y = mapping.Transform(new Point(0, 1)) - origin;
            ReadDrawing(VisualTreeHelper.GetDrawing(visual), new Matrix(x.X, x.Y, y.X, y.Y, origin.X, origin.Y));
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                if (VisualTreeHelper.GetChild(visual, i) is Visual child) ReadVisual(child);
        }

        void ReadDrawing(Drawing? drawing, Matrix transform)
        {
            if (drawing is GlyphRunDrawing glyph)
            {
                if (glyph.GlyphRun.Characters is { } characters) text.Append(characters.ToArray());
                bounds.Union(new MatrixTransform(transform).TransformBounds(glyph.GlyphRun.BuildGeometry().Bounds));
            }
            else if (drawing is DrawingGroup group)
            {
                var combined = group.Transform?.Value ?? Matrix.Identity;
                combined.Append(transform);
                foreach (var child in group.Children) ReadDrawing(child, combined);
            }
        }
    }
}
