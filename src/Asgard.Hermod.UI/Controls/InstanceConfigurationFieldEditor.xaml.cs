namespace Asgard.Hermod.UI.Controls;

/// <summary>封装连接器声明字段的通用表单控件，不在窗口中重复密钥和验证外观。</summary>
public partial class InstanceConfigurationFieldEditor : UserControl
{
    /// <summary>字段状态从所属表单项继承，所有编辑控件复用共享表单组件。</summary>
    public InstanceConfigurationFieldEditor() => InitializeComponent();
}
