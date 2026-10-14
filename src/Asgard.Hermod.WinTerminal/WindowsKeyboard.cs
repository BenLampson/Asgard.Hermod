namespace Asgard.Hermod.WinTerminal;

/// <summary>读取当前 Windows 键盘布局以编码左 Alt 的 Meta 输入。</summary>
internal static class WindowsKeyboard
{
    /// <summary>保留 Shift/CapsLock，清除 Alt 状态后取得字符；不改变系统死键状态。</summary>
    internal static string? GetTextWithoutAlt(Key key, ModifierKeys? modifiers = null)
    {
        // WPF 已按 NumLock 把导航键与数字分开；数字小键盘不依赖 ToUnicode 的线程状态。
        if (key is >= Key.NumPad0 and <= Key.NumPad9 && modifiers is { } numericModifiers
            && !numericModifiers.HasFlag(ModifierKeys.Control) && !numericModifiers.HasFlag(ModifierKeys.Alt))
            return ((char)('0' + key - Key.NumPad0)).ToString();
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
            return null;
        byte[] state = new byte[256];
        if (!GetKeyboardState(state))
            return null;
        state[0x12] = state[0xA4] = state[0xA5] = 0;
        if (modifiers is { } actual)
        {
            state[0x10] = state[0xA0] = state[0xA1] = actual.HasFlag(ModifierKeys.Shift) ? (byte)0x80 : (byte)0;
            state[0x11] = state[0xA2] = state[0xA3] = actual.HasFlag(ModifierKeys.Control) ? (byte)0x80 : (byte)0;
            // WPF 在 NumLock 关闭时给出导航键；收到 NumPad 键意味着这里应翻译为数字。
            if (key is >= Key.NumPad0 and <= Key.NumPad9) state[0x90] = 1;
        }
        var result = new StringBuilder(8);
        int count = ToUnicode((uint)virtualKey, MapVirtualKey((uint)virtualKey, 0), state, result, result.Capacity, 4);
        return count > 0 ? result.ToString() : null;
    }

    /// <summary>取得各虚拟键的按下与切换状态。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    /// <summary>将虚拟键映射为扫描码。</summary>
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint key, uint kind);

    /// <summary>使用当前键盘布局生成 Unicode 文本。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicode(uint key, uint scan, byte[] state, [Out] StringBuilder text, int capacity, uint flags);
}
