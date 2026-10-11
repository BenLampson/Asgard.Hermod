namespace Asgard.Hermod.UI.Controls;

/// <summary>提供统一的弹窗操作区域，业务内容仍使用 Content 自由组合。</summary>
public class DialogActionBar : ContentControl
{
    /// <summary>模板放入主题字典，业务按钮保留所属窗口的名称作用域。</summary>
    static DialogActionBar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogActionBar),
        new FrameworkPropertyMetadata(typeof(DialogActionBar)));
}
