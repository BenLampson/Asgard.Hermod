namespace Asgard.Hermod.UI.Controls;

/// <summary>批量和树形选择的半选框，半选时继续点击会全选，而不是清空。</summary>
public class SelectionCheckBox : CheckBox
{
    /// <summary>派生控件显式复用统一复选框样式。</summary>
    public SelectionCheckBox() => SetResourceReference(StyleProperty, typeof(CheckBox));
    /// <summary>未选及半选转为全选，使用当前值保留业务属性绑定。</summary>
    protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
}
