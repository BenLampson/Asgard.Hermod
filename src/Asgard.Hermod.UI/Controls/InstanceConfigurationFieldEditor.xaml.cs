namespace Asgard.Hermod.UI.Controls;

/// <summary>封装连接器声明字段的通用表单控件，不在窗口中重复密钥和验证外观。</summary>
public partial class InstanceConfigurationFieldEditor : UserControl
{
    /// <summary>字段状态从所属表单项继承，所有编辑控件复用共享表单组件。</summary>
    public InstanceConfigurationFieldEditor() => InitializeComponent();

    /// <summary>共用选择器传递文件路径，由连接器在测试前导入需要可移植保存的内容。</summary>
    private void OnBrowseFile(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: InstanceConfigurationFieldViewModel field }) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Title = Text.Format("ChooseFileForField", field.Label), CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) field.Value = picker.FileName;
    }
}
