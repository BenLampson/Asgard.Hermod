namespace Asgard.Hermod.UI.Controls;

/// <summary>为模态业务表单提供统一窗口外观和所属窗口行为。</summary>
public class HermodDialog : HermodWindow
{
    /// <summary>使用设计稿的紧凑标题与标准尺寸，在所属窗口中央打开，不占用任务栏。</summary>
    public HermodDialog()
    {
        CaptionHeight = 36;
        ResizeMode = ResizeMode.CanMinimize;
        Width = 620;
        Height = 460;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
