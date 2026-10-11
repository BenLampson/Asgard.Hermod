namespace Asgard.Hermod.UI.Controls;

/// <summary>使半选标签点击后选择整枝，同时保留原生键盘和自动化勾选行为。</summary>
public sealed class TagTreeCheckBox : CheckBox
{
    /// <summary>未选和半选均转为全选，原生属性绑定负责把操作交付给树节点。</summary>
    protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
}
