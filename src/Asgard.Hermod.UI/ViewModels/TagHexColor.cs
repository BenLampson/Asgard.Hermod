namespace Asgard.Hermod.UI.ViewModels;

/// <summary>将设计允许的短、长 HEX 转为一致的六位大写颜色。</summary>
internal static class TagHexColor
{
    /// <summary>只接受带井号的三位或六位 RGB，非法输入不产生可用预览。</summary>
    internal static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        var text = input?.Trim();
        if (text is null || text.Length is not (4 or 7) || text[0] != '#'
            || text.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
            return false;

        normalized = text.Length == 7 ? text.ToUpperInvariant()
            : $"#{text[1]}{text[1]}{text[2]}{text[2]}{text[3]}{text[3]}".ToUpperInvariant();
        return true;
    }
}
