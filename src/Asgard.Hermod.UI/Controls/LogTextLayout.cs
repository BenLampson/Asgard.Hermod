namespace Asgard.Hermod.UI.Controls;

/// <summary>自定义行高时不用 TextBox 按默认字体高度估算的行索引 API。</summary>
internal static class LogTextLayout
{
    internal static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int index = 0; index < text.Length; index++) if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }
    internal static IEnumerable<LogDisplayLine> VisibleLines(TextBox input, int[] starts)
    {
        string text = input.Text;
        if (text.Length == 0 || !input.IsArrangeValid) yield break;
        double height = TextBlock.GetLineHeight(input);
        if (double.IsNaN(height)) yield break;
        int start = input.GetCharacterIndexFromPoint(new Point(0, 0), true);
        if (start < 0 || start >= text.Length) yield break;
        var bounds = input.GetRectFromCharacterIndex(start);
        double viewportHeight = input.ViewportHeight + input.Padding.Top + input.Padding.Bottom;
        for (int row = 0; row < 200 && !bounds.IsEmpty && bounds.Top < viewportHeight; row++)
        {
            int physical = Array.BinarySearch(starts, start); if (physical < 0) physical = ~physical - 1;
            int physicalEnd = physical + 1 < starts.Length ? starts[physical + 1] : text.Length;
            int next = input.GetCharacterIndexFromPoint(new Point(0, bounds.Top + height + bounds.Height / 2), true);
            var nextBounds = next >= 0 && next < text.Length ? input.GetRectFromCharacterIndex(next) : Rect.Empty;
            bool hasNext = next > start && !nextBounds.IsEmpty && nextBounds.Top > bounds.Top + height / 2;
            int end = hasNext ? Math.Min(physicalEnd, next) : physicalEnd;
            while (end > start && text[end - 1] is '\r' or '\n') end--;
            // TextBox 的顶部内边距也参与可见字形裁剪；不额外绘制原生文本已跳过的上一行。
            if (bounds.Bottom > input.Padding.Top) yield return new(start, end, starts[physical], physical + 1, bounds);
            if (!hasNext) yield break;
            start = next; bounds = nextBounds;
        }
    }
}
