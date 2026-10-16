namespace Asgard.Hermod.UI.Tests;

/// <summary>读取 WPF 真正提交的字形与基线；不根据 FontFamily 声明推断最终字体。</summary>
public static class OffscreenGlyphMeasurement
{
    /// <summary>从指定控件读取实际字形的绝对基线、字号和字体文件。</summary>
    public static IReadOnlyList<(Point Baseline, double Size, string Font)> Read(Visual target, Visual root)
    {
        var result = new List<(Point, double, string)>();
        Visit(target);
        return result;
        void Visit(Visual visual)
        {
            if (VisualTreeHelper.GetDrawing(visual) is { } drawing) Draw(drawing, Matrix.Identity, visual);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
                if (VisualTreeHelper.GetChild(visual, i) is Visual child) Visit(child);
        }
        void Draw(Drawing drawing, Matrix matrix, Visual visual)
        {
            if (drawing is GlyphRunDrawing glyph)
            {
                Point origin = matrix.Transform(glyph.GlyphRun.BaselineOrigin);
                origin = visual.TransformToAncestor(root).Transform(origin);
                result.Add((origin, glyph.GlyphRun.FontRenderingEmSize, glyph.GlyphRun.GlyphTypeface.FontUri.OriginalString));
            }
            else if (drawing is DrawingGroup group)
            {
                var nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(matrix);
                foreach (var child in group.Children) Draw(child, nested, visual);
            }
        }
    }
}
