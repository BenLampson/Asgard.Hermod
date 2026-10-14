using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Interop;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>唯一托盘菜单；两项稳定操作，支持动态主题、键盘和朝向图标的箭头。</summary>
public partial class TrayMenuWindow : Window
{
    private readonly Action _toggle;
    private readonly Action _exit;
    private bool _closing;

    /// <summary>调用者按主窗体真实状态选择显示或隐藏，不比较译文控制逻辑。</summary>
    public TrayMenuWindow(bool showMain, Action toggle, Action exit)
    {
        InitializeComponent(); _toggle = toggle; _exit = exit;
        ToggleLabel.Text = Text.Get(showMain ? "ShowMainWindow" : "HideMainWindow");
        AutomationProperties.SetName(ToggleButton, ToggleLabel.Text);
        ToggleButton.Tag = showMain ? "tray.main.show" : "tray.main.hide";
        ExitButton.Tag = "tray.application.exit";
    }

    /// <summary>先把 HWND 移到图标显示器，让 WPF 取得真实 DPI，再定位菜单和箭头。</summary>
    public void ShowAt(Rect icon)
    {
        var helper = new WindowInteropHelper(this); nint handle = helper.EnsureHandle();
        TrayNativeMethods.SetWindowPos(handle, 0, (int)icon.Left, (int)icon.Top, 0, 0, 0x15);
        var point = new NativePoint { X = (int)(icon.Left + icon.Width / 2), Y = (int)(icon.Top + icon.Height / 2) };
        nint monitor = WindowNativeMethods.MonitorFromPoint(point, 2);
        var info = new NativeMonitorInformation { Size = Marshal.SizeOf<NativeMonitorInformation>() };
        if (!WindowNativeMethods.GetMonitorInfo(monitor, ref info)) return;
        double scale = TrayNativeMethods.GetDpiForWindow(handle) / 96d;
        var work = new Rect(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right - info.WorkArea.Left, info.WorkArea.Bottom - info.WorkArea.Top);
        var placement = TrayMenuPlacement.Calculate(icon, work, new Size(Width, Height), scale);
        double arrow = placement.ArrowOffset;
        string geometry = placement.ArrowOnTop
            ? FormattableString.Invariant($"M {arrow - 8},10 L {arrow},1 L {arrow + 8},10")
            : FormattableString.Invariant($"M {arrow - 8},132 L {arrow},141 L {arrow + 8},132");
        Arrow.Data = Geometry.Parse(geometry);
        // Geometry.Parse 的数字必须使用不变区域格式。
        Canvas.SetLeft(ArrowJoin, arrow - 7.5); Canvas.SetTop(ArrowJoin, placement.ArrowOnTop ? 10 : 131);
        TrayNativeMethods.SetWindowPos(handle, new nint(-1), (int)placement.Position.X, (int)placement.Position.Y,
            (int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale), 0x10);
        Show(); Activate(); TrayNativeMethods.SetForegroundWindow(handle); ToggleButton.Focus();
    }

    private void OnToggle(object sender, RoutedEventArgs args) { Close(); _toggle(); }
    private void OnExit(object sender, RoutedEventArgs args) { Close(); _exit(); }
    private void OnDeactivated(object? sender, EventArgs args) { if (!_closing) Close(); }
    /// <summary>Close 会同步触发 Deactivated，必须在此之前阻止重复关闭。</summary>
    protected override void OnClosing(CancelEventArgs args) { _closing = true; base.OnClosing(args); }
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        else if (args.Key is Key.Up or Key.Down)
        {
            if (ToggleButton.IsKeyboardFocusWithin) ExitButton.Focus(); else ToggleButton.Focus();
            args.Handled = true;
        }
    }
}
