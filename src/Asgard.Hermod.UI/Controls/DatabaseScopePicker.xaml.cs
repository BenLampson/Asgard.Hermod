namespace Asgard.Hermod.UI.Controls;

/// <summary>数据库范围唯一入口；增删仍使用模块的完整确认与草稿保护。</summary>
public partial class DatabaseScopePicker : UserControl
{
    /// <summary>不读取数据库或推断范围，只呈现当前 Session。</summary>
    public DatabaseScopePicker() => InitializeComponent();

    private void OnPopupOpened(object? sender, EventArgs args)
    {
        if (Window.GetWindow(this) is not { } window) return;
        ScopeContent.Width = Math.Clamp(window.ActualWidth - 48, 240, 540);
        ScopePopup.HorizontalOffset = window.ActualWidth - Trigger.TranslatePoint(new(), window).X - ScopeContent.Width - 42;
        if (ScopeContent.FindName("ChoicesViewport") is ScrollViewer choices) choices.MaxHeight = Math.Max(76, Math.Min(400, window.ActualHeight - 360));
    }
}
