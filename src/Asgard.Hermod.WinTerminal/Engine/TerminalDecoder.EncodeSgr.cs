// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine;

/// <summary>将 VT/ANSI 解析事件应用到终端缓冲区、光标、样式和工作模式。</summary>
public sealed partial class TerminalDecoder
{
    /// <summary>处理 EncodeSgr 对应的终端协议或渲染逻辑。</summary>
    private string EncodeSgr()
    {
        CharAttributes a = State.Attributes;
        List<int> p = [];
        if (a.Bold)
            p.Add(1);
        if (a.Faint)
            p.Add(2);
        if (a.Italic)
            p.Add(3);
        if (a.Fraktur)
            p.Add(20);
        switch (a.UnderlineStyle)
        {
            case Underline.Single:
                p.Add(4);
                break;
            case Underline.Double:
                p.Add(21);
                break;
            case Underline.Curly:
                p.Add(4);
                p.Add(3);
                break;
            case Underline.Dotted:
                p.Add(4);
                p.Add(4);
                break;
            case Underline.Dashed:
                p.Add(4);
                p.Add(5);
                break;
        }

        if (a.Blink == Blink.Slow)
            p.Add(5);
        if (a.Blink == Blink.Rapid)
            p.Add(6);
        if (a.Inverse)
            p.Add(7);
        if (a.Conceal)
            p.Add(8);
        if (a.Strikethrough)
            p.Add(9);
        if (a.Framed)
            p.Add(51);
        if (a.Encircled)
            p.Add(52);
        if (a.Overline)
            p.Add(53);
        if (a.DoublyUnderlined && a.UnderlineStyle != Underline.Double)
            p.Add(21);
        EncodeColor(p, ColorTarget.Foreground, a);
        EncodeColor(p, ColorTarget.Background, a);
        EncodeColor(p, ColorTarget.Underline, a);
        if (p.Count == 0)
            p.Add(0);
        return string.Join(";", p);
    }

    /// <summary>处理 EncodeColor 对应的终端协议或渲染逻辑。</summary>
    private static void EncodeColor(List<int> p, ColorTarget target, in CharAttributes a)
    {
        ColorSource source;
        int index;
        TerminalColor rgb;
        switch (target)
        {
            case ColorTarget.Foreground:
                source = a.ForegroundSource;
                index = a.ForegroundIndex;
                rgb = a.ForegroundRgb;
                break;
            case ColorTarget.Background:
                source = a.BackgroundSource;
                index = a.BackgroundIndex;
                rgb = a.BackgroundRgb;
                break;
            case ColorTarget.Underline:
                source = a.UnderlineSource;
                index = a.UnderlineIndex;
                rgb = a.UnderlineRgb;
                break;
            default:
                return;
        }

        int prefix = target switch
        {
            ColorTarget.Foreground => 38,
            ColorTarget.Background => 48,
            ColorTarget.Underline => 58,
            _ => 38,
        };
        switch (source)
        {
            case ColorSource.Default:
                p.Add(target == ColorTarget.Foreground ? 39 : target == ColorTarget.Background ? 49 : 59);
                break;
            case ColorSource.Indexed:
                if (target == ColorTarget.Foreground)
                {
                    if (index < 8)
                    {
                        p.Add(30 + index);
                        break;
                    }

                    if (index < 16)
                    {
                        p.Add(90 + index - 8);
                        break;
                    }
                }
                else if (target == ColorTarget.Background)
                {
                    if (index < 8)
                    {
                        p.Add(40 + index);
                        break;
                    }

                    if (index < 16)
                    {
                        p.Add(100 + index - 8);
                        break;
                    }
                }

                p.Add(prefix);
                p.Add(5);
                p.Add(index);
                break;
            case ColorSource.Direct:
                p.Add(prefix);
                p.Add(2);
                p.Add(rgb.R);
                p.Add(rgb.G);
                p.Add(rgb.B);
                break;
        }
    }
}
