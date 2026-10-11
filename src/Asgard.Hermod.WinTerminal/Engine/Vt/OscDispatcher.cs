// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>处理 OSC 标题、调色板、超链接与可选的宿主事件。</summary>
internal static class OscDispatcher
{
    /// <summary>分派 OSC 负载到终端状态与宿主事件。</summary>
    public static void ProcessOsc(TerminalDecoder d, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;
        int command = 0;
        int i = 0;
        while (i < data.Length && data[i] >= (byte)'0' && data[i] <= (byte)'9')
        {
            command = command * 10 + (data[i] - (byte)'0');
            i++;
        }

        ReadOnlySpan<byte> rest = i < data.Length && data[i] == (byte)';' ? data.Slice(i + 1) : ReadOnlySpan<byte>.Empty;
        switch (command)
        {
            case 0:
            case 1:
            case 2:
                d.SetTitle(DecodeUtf8(rest));
                break;
            case 4:
                {
                    int semi = rest.IndexOf((byte)';');
                    if (semi > 0 && int.TryParse(DecodeUtf8(rest.Slice(0, semi)), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                    {
                        TerminalColor? col = ParseColorSpec(DecodeUtf8(rest.Slice(semi + 1)));
                        if (col.HasValue && c >= 0 && c < 256)
                            d.Options.Palette[c] = col.Value;
                    }

                    break;
                }

            case 10:
            case 11:
            case 12:
                {
                    string spec = DecodeUtf8(rest);
                    if (spec == "?")
                        break;
                    TerminalColor? col = ParseColorSpec(spec);
                    if (!col.HasValue)
                        break;
                    if (command == 10)
                        d.Options.DefaultForeground = col.Value;
                    else if (command == 11)
                        d.Options.DefaultBackground = col.Value;
                    break;
                }

            case 8:
                {
                    int semi = rest.IndexOf((byte)';');
                    string uri = semi < 0 ? DecodeUtf8(rest) : DecodeUtf8(rest.Slice(semi + 1));
                    d.State.Attributes.HyperlinkId = string.IsNullOrEmpty(uri) ? (byte)0 : d.RegisterHyperlink(uri);
                    break;
                }

            case 9:
                {
                    string message = DecodeUtf8(rest);
                    if (!string.IsNullOrEmpty(message))
                        d.RaiseNotification(message);
                    break;
                }

            case 52:
                {
                    if (!d.Options.AllowClipboard)
                        break;
                    int semi = rest.IndexOf((byte)';');
                    string selection = semi < 0 ? "c" : DecodeUtf8(rest.Slice(0, semi));
                    string clipData = semi < 0 ? DecodeUtf8(rest) : DecodeUtf8(rest.Slice(semi + 1));
                    d.RaiseClipboardRequested(selection, string.IsNullOrEmpty(clipData) ? null : clipData);
                    break;
                }

            case 133:
                {
                    d.RaiseShellIntegrationMark(DecodeUtf8(rest));
                    break;
                }

            case 1337:
                {
                    d.RaiseIterm2Annotation(DecodeUtf8(rest));
                    break;
                }

            default:
                break;
        }
    }

    /// <summary>处理 DecodeUtf8 对应的终端协议或渲染逻辑。</summary>
    private static string DecodeUtf8(ReadOnlySpan<byte> data) => data.IsEmpty ? string.Empty : Encoding.UTF8.GetString(data);
    /// <summary>解析十六进制或 rgb: 形式的颜色。</summary>
    internal static TerminalColor? ParseColorSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;
        spec = spec.Trim();
        if (spec.StartsWith('#'))
        {
            string hex = spec[1..];
            return hex.Length switch
            {
                3 => TerminalColor.FromArgb(HexDigit(hex[0]) * 17, HexDigit(hex[1]) * 17, HexDigit(hex[2]) * 17),
                6 => TerminalColor.FromArgb(Hex(hex, 0), Hex(hex, 2), Hex(hex, 4)),
                _ => null,
            };
        }

        if (spec.StartsWith("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = spec[4..].Split('/', ':');
            if (parts.Length != 3)
                return null;
            return TerminalColor.FromArgb(ScaledByte(parts[0]), ScaledByte(parts[1]), ScaledByte(parts[2]));
        }

        return null;
    }

    /// <summary>处理 Hex 对应的终端协议或渲染逻辑。</summary>
    private static byte Hex(string s, int offset)
    {
        return (byte)((HexDigit(s[offset]) << 4) | HexDigit(s[offset + 1]));
    }

    /// <summary>处理 HexDigit 对应的终端协议或渲染逻辑。</summary>
    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
    /// <summary>处理 ScaledByte 对应的终端协议或渲染逻辑。</summary>
    private static byte ScaledByte(string component)
    {
        int v = 0;
        foreach (char c in component)
            v = (v << 4) | HexDigit(c);
        return component.Length switch
        {
            0 => 0,
            1 => (byte)(v * 17),
            2 => (byte)v,
            3 => (byte)(v >> 4),
            4 => (byte)(v >> 8),
            _ => (byte)(v >> ((component.Length - 2) * 4)),
        };
    }
}
