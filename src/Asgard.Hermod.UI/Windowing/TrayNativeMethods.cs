using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>托盘所需 Win32 边界，句柄由唯一托盘实例创建和回收。</summary>
internal static class TrayNativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    internal static extern bool Notify(uint message, ref TrayNotifyData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    internal static extern int GetRect(ref TrayIconIdentifier identifier, out NativeRectangle rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    internal static extern nint CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    /// <summary>窗口移到目标显示器后直接读取原生 DPI，避免布局前 WPF DPI 仍缓存旧值。</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);
}
