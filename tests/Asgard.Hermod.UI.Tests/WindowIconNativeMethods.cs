using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>读取 WPF 交给 Windows 的原生图标，验证任务栏使用的真实位图。</summary>
internal static class WindowIconNativeMethods
{
    /// <summary>WM_GETICON 返回窗口持有的图标句柄；测试只读取，不销毁该句柄。</summary>
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
