namespace Asgard.Hermod.UI.Controls;

/// <summary>对象正文继承当前工作区的导航命令；离屏或独立挂载时允许没有工作区。</summary>
public static class DatabaseObjectScope
{
    /// <summary>返回所属脚本 Session，不创建或重新执行 SQL。</summary>
    public static readonly DependencyProperty ReturnToScriptCommandProperty = DependencyProperty.RegisterAttached("ReturnToScriptCommand", typeof(ICommand), typeof(DatabaseObjectScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取最近工作区的返回命令。</summary>
    public static ICommand? GetReturnToScriptCommand(DependencyObject target) => (ICommand?)target.GetValue(ReturnToScriptCommandProperty);
    /// <summary>由实际工作区提供已有导航命令。</summary>
    public static void SetReturnToScriptCommand(DependencyObject target, ICommand? command) => target.SetValue(ReturnToScriptCommandProperty, command);
}
