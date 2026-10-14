using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Asgard.Hermod.UI.Tests;

/// <summary>测试专用的 HWND 交互，消息只发给测试窗口，不生成全桌面键盘输入。</summary>
internal static class WindowInteractionNativeMethods
{
    /// <summary>WS_CAPTION 同时包含边框与对话框边框位。</summary>
    private const uint CaptionStyle = 0x00C00000;
    /// <summary>WS_THICKFRAME 代表原生可缩放边缘。</summary>
    private const uint ResizeFrameStyle = 0x00040000;

    /// <summary>读取 64 位窗口样式。</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPointer(IntPtr window, int index);

    /// <summary>读取 32 位窗口样式。</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    /// <summary>检查模态窗口是否真实禁用了拥有者。</summary>
    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    /// <summary>发送到测试 HWND 的消息队列，使 WPF 经正常键盘预处理流程处理。</summary>
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr value, IntPtr state);

    /// <summary>获取虚拟键的扫描码，构造标准 WM_KEYDOWN 与 WM_KEYUP 参数。</summary>
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>验证 HWND 样式中不存在系统标题栏。</summary>
    internal static bool HasSystemCaption(Window window) => (Style(window) & CaptionStyle) == CaptionStyle;

    /// <summary>验证 NoResize 窗口没有原生可缩放边缘。</summary>
    internal static bool HasResizeFrame(Window window) => (Style(window) & ResizeFrameStyle) != 0;

    /// <summary>读取模态拥有者当前的原生启用状态。</summary>
    internal static bool IsEnabled(Window window) => IsWindowEnabled(Handle(window));

    /// <summary>向当前测试窗口排入一次按键，不绕过输入绑定或默认按钮行为。</summary>
    internal static void PostKey(Window window, uint virtualKey)
    {
        IntPtr handle = Handle(window);
        uint scan = MapVirtualKey(virtualKey, 0);
        uint down = 1u | (scan << 16);
        uint up = down | 0xC0000000;
        if (!PostMessage(handle, 0x0100, new IntPtr(virtualKey), new IntPtr(down))
            || !PostMessage(handle, 0x0101, new IntPtr(virtualKey), new IntPtr(unchecked((int)up))))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    /// <summary>双击消息仅发给被测 HWND，不移动系统鼠标或影响其他应用。</summary>
    internal static void PostDoubleClick(Window window, FrameworkElement target)
    {
        Point origin = window.PointToScreen(new Point());
        Point point = target.PointToScreen(new Point(target.ActualWidth / 3, target.ActualHeight / 2));
        int x = (int)(point.X - origin.X), y = (int)(point.Y - origin.Y);
        nint position = new((y << 16) | (x & 0xffff)); nint handle = Handle(window);
        PostMessage(handle, 0x0201, 1, position); PostMessage(handle, 0x0202, 0, position);
        PostMessage(handle, 0x0203, 1, position); PostMessage(handle, 0x0202, 0, position);
    }
    /// <summary>将 Escape 送到实际 Popup 的 HWND，让 WPF 原生菜单逻辑关闭。</summary>
    internal static void PostPopupEscape(Visual popup)
    {
        var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(popup), exactMatch: false);
        PostMessage(source.Handle, 0x0100, 0x1B, 1); PostMessage(source.Handle, 0x0101, 0x1B, new nint(unchecked((int)0xC0000001)));
    }

    /// <summary>读取已有真实窗口的样式，禁止以零 HWND 执行查询。</summary>
    private static uint Style(Window window)
    {
        IntPtr handle = Handle(window);
        return Environment.Is64BitProcess
            ? unchecked((uint)GetWindowLongPointer(handle, -16).ToInt64())
            : unchecked((uint)GetWindowLong(handle, -16));
    }

    /// <summary>确认 Show 或 ShowDialog 已创建 HWND。</summary>
    private static IntPtr Handle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Assert.NotEqual(IntPtr.Zero, handle);
        return handle;
    }
}
