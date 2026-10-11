// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine;

/// <summary>将 VT/ANSI 解析事件应用到终端缓冲区、光标、样式和工作模式。</summary>
public sealed partial class TerminalDecoder
{
    /// <summary>处理 SetGraphicRendition 对应的终端协议或渲染逻辑。</summary>
    private void SetGraphicRendition(in CsiParams p)
    {
        if (p.IsEmpty)
        {
            State.Attributes.Reset();
            return;
        }

        ref CharAttributes a = ref State.Attributes;
        for (int i = 0; i < p.Length;)
        {
            int v = p[i];
            switch (v)
            {
                case 0:
                    a.Reset();
                    i++;
                    break;
                case 1:
                    a.Bold = true;
                    i++;
                    break;
                case 2:
                    a.Faint = true;
                    i++;
                    break;
                case 3:
                    a.Italic = true;
                    i++;
                    break;
                case 4:
                    {
                        if (p.IsSubParam(i + 1))
                        {
                            a.UnderlineStyle = ToUnderlineStyle(p[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            a.UnderlineStyle = Underline.Single;
                            i++;
                        }

                        break;
                    }

                case 5:
                    a.Blink = Blink.Slow;
                    i++;
                    break;
                case 6:
                    a.Blink = Blink.Rapid;
                    i++;
                    break;
                case 7:
                    a.Inverse = true;
                    i++;
                    break;
                case 8:
                    a.Conceal = true;
                    i++;
                    break;
                case 9:
                    a.Strikethrough = true;
                    i++;
                    break;
                case 20:
                    a.Fraktur = true;
                    i++;
                    break;
                case 21:
                    a.UnderlineStyle = Underline.Double;
                    a.DoublyUnderlined = true;
                    i++;
                    break;
                case 22:
                    a.Bold = false;
                    a.Faint = false;
                    i++;
                    break;
                case 23:
                    a.Italic = false;
                    a.Fraktur = false;
                    i++;
                    break;
                case 24:
                    a.UnderlineStyle = Underline.None;
                    a.DoublyUnderlined = false;
                    i++;
                    break;
                case 25:
                    a.Blink = Blink.None;
                    i++;
                    break;
                case 27:
                    a.Inverse = false;
                    i++;
                    break;
                case 28:
                    a.Conceal = false;
                    i++;
                    break;
                case 29:
                    a.Strikethrough = false;
                    i++;
                    break;
                case 51:
                    a.Framed = true;
                    i++;
                    break;
                case 52:
                    a.Encircled = true;
                    i++;
                    break;
                case 53:
                    a.Overline = true;
                    i++;
                    break;
                case 54:
                    a.Framed = false;
                    a.Encircled = false;
                    i++;
                    break;
                case 55:
                    a.Overline = false;
                    i++;
                    break;
                case 39:
                    a.ForegroundSource = ColorSource.Default;
                    i++;
                    break;
                case 49:
                    a.BackgroundSource = ColorSource.Default;
                    i++;
                    break;
                case 59:
                    a.UnderlineSource = ColorSource.Default;
                    i++;
                    break;
                case 99:
                    a.ForegroundSource = ColorSource.Default;
                    i++;
                    break;
                case 109:
                    a.BackgroundSource = ColorSource.Default;
                    i++;
                    break;
                case >= 30 and <= 37:
                    a.ForegroundSource = ColorSource.Indexed;
                    a.ForegroundIndex = v - 30;
                    i++;
                    break;
                case >= 90 and <= 97:
                    a.ForegroundSource = ColorSource.Indexed;
                    a.ForegroundIndex = v - 90 + 8;
                    i++;
                    break;
                case >= 40 and <= 47:
                    a.BackgroundSource = ColorSource.Indexed;
                    a.BackgroundIndex = v - 40;
                    i++;
                    break;
                case >= 100 and <= 107:
                    a.BackgroundSource = ColorSource.Indexed;
                    a.BackgroundIndex = v - 100 + 8;
                    i++;
                    break;
                case 38:
                    i = ConsumeColor(p, i, ColorTarget.Foreground, ref a);
                    break;
                case 48:
                    i = ConsumeColor(p, i, ColorTarget.Background, ref a);
                    break;
                case 58:
                    i = ConsumeColor(p, i, ColorTarget.Underline, ref a);
                    break;
                default:
                    i++;
                    break;
            }
        }
    }

    /// <summary>处理 ToUnderlineStyle 对应的终端协议或渲染逻辑。</summary>
    private static Underline ToUnderlineStyle(int code) => code switch
    {
        0 => Underline.None,
        1 => Underline.Single,
        2 => Underline.Double,
        3 => Underline.Curly,
        4 => Underline.Dotted,
        5 => Underline.Dashed,
        _ => Underline.Single,
    };
    /// <summary>表示 ColorTarget 的终端协议数据。</summary>
    private enum ColorTarget
    {
        Foreground,
        Background,
        Underline
    }

    /// <summary>处理 ConsumeColor 对应的终端协议或渲染逻辑。</summary>
    private static int ConsumeColor(in CsiParams p, int i, ColorTarget target, ref CharAttributes a)
    {
        if (i + 1 >= p.Length)
            return i + 1;
        int spec = p[i + 1];
        if (spec == 5)
        {
            int idx = p.At(i + 2, 0);
            AssignIndexed(target, ref a, idx);
            return i + 3;
        }

        if (spec == 2)
        {
            int r, g, b, next;
            if (p.IsSubParam(i + 1))
            {
                int start = i + 2;
                int end = start;
                while (end < p.Length && p.IsSubParam(end))
                    end++;
                int count = end - start;
                if (count >= 4)
                {
                    r = p.At(start + 1, 0);
                    g = p.At(start + 2, 0);
                    b = p.At(start + 3, 0);
                }
                else
                {
                    r = p.At(start, 0);
                    g = p.At(start + 1, 0);
                    b = p.At(start + 2, 0);
                }

                next = end;
            }
            else
            {
                r = p.At(i + 2, 0);
                g = p.At(i + 3, 0);
                b = p.At(i + 4, 0);
                next = i + 5;
            }

            AssignDirect(target, ref a, TerminalColor.FromArgb(ClampByte(r), ClampByte(g), ClampByte(b)));
            return next;
        }

        return i + 2;
    }

    /// <summary>处理 AssignIndexed 对应的终端协议或渲染逻辑。</summary>
    private static void AssignIndexed(ColorTarget target, ref CharAttributes a, int idx)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                a.ForegroundSource = ColorSource.Indexed;
                a.ForegroundIndex = idx;
                break;
            case ColorTarget.Background:
                a.BackgroundSource = ColorSource.Indexed;
                a.BackgroundIndex = idx;
                break;
            case ColorTarget.Underline:
                a.UnderlineSource = ColorSource.Indexed;
                a.UnderlineIndex = idx;
                break;
        }
    }

    /// <summary>处理 AssignDirect 对应的终端协议或渲染逻辑。</summary>
    private static void AssignDirect(ColorTarget target, ref CharAttributes a, TerminalColor color)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                a.ForegroundSource = ColorSource.Direct;
                a.ForegroundRgb = color;
                break;
            case ColorTarget.Background:
                a.BackgroundSource = ColorSource.Direct;
                a.BackgroundRgb = color;
                break;
            case ColorTarget.Underline:
                a.UnderlineSource = ColorSource.Direct;
                a.UnderlineRgb = color;
                break;
        }
    }

    /// <summary>处理 ClampByte 对应的终端协议或渲染逻辑。</summary>
    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);
}
