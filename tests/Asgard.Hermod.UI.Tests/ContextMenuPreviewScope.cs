using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>实际打开产品菜单并屏蔽桌面显示，截图只读取已布局的菜单面板。</summary>
internal sealed class ContextMenuPreviewScope : IDisposable
{
    private readonly ResourceDictionary _resources = Application.Current.Resources;
    private readonly bool _hadLocalStyle;
    private readonly Style _originalStyle;

    /// <summary>只覆盖测试中的透明度，模板、命令、焦点和弹层生命周期仍使用产品实现。</summary>
    internal ContextMenuPreviewScope()
    {
        _hadLocalStyle = _resources.Keys.Cast<object>().Contains(typeof(ContextMenu));
        _originalStyle = Assert.IsType<Style>(Application.Current.FindResource(typeof(ContextMenu)));
        var hidden = new Style(typeof(ContextMenu), _originalStyle);
        hidden.Setters.Add(new Setter(UIElement.OpacityProperty, 0d));
        _resources[typeof(ContextMenu)] = hidden;
    }

    /// <summary>通过真实菜单的自动化入口点击，覆盖命令参数与菜单关闭行为。</summary>
    internal static async Task InvokeAsync(MenuItem item)
    {
        var peer = new MenuItemAutomationPeer(item);
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        invoke.Invoke();
        await WpfTestFixture.DrainDispatcherAsync();
    }

    /// <summary>按当前屏幕缩放输出实际模板，而非根据预期值重画菜单。</summary>
    internal static void Save(ContextMenu menu, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var chrome = Assert.IsType<Border>(menu.Template.FindName("MenuChrome", menu));
        var dpi = VisualTreeHelper.GetDpi(chrome);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chrome.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(chrome.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(chrome);
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>恢复应用样式，避免此测试的可见性设置影响后续窗口。</summary>
    public void Dispose()
    {
        if (_hadLocalStyle) _resources[typeof(ContextMenu)] = _originalStyle;
        else _resources.Remove(typeof(ContextMenu));
    }
}
