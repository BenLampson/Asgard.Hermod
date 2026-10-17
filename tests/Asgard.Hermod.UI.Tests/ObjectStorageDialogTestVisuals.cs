using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>对象操作模态用例共享拥有者、通用窗体尺寸与实际按钮查找。</summary>
internal static class ObjectStorageDialogTestVisuals
{
    /// <summary>保留真实 HWND，完全透明且不操作真实用户文件。</summary>
    internal static HermodWindow CreateOwner() => new()
    {
        Content = new Grid(),
        Width = 800,
        Height = 640,
        Opacity = 0,
        ShowInTaskbar = false,
        ShowActivated = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 100,
        Top = 100,
    };

    /// <summary>共用标题与操作栏不能被各操作窗体重新实现。</summary>
    internal static void AssertCommonFrame(HermodWindow window, HermodWindow owner, double width, double height)
    {
        Assert.Same(owner, window.Owner);
        Assert.False(WindowInteractionNativeMethods.IsEnabled(owner));
        Assert.Equal(width, window.ActualWidth);
        Assert.Equal(height, window.ActualHeight);
        Assert.Equal(36d, WindowTestVisuals.Caption(window).ActualHeight);
        Assert.Equal(56d, WindowTestVisuals.Footer(window).ActualHeight);
    }

    /// <summary>沿真实显示树找出模型命令所绑定的唯一按钮。</summary>
    internal static Button CommandButton(Window window, ICommand command) =>
        Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, command));

    /// <summary>外部关闭也必须经过共用标题栏的真实路由命令。</summary>
    internal static Button CaptionClose(Window window) => WindowTestVisuals.CaptionButton(window, "CloseButton");
}
