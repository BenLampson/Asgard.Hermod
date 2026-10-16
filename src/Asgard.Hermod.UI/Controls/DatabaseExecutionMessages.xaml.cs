namespace Asgard.Hermod.UI.Controls;

/// <summary>固定列宽的安全执行消息阅读器；点击位置复用所属页签命令，不执行 SQL。</summary>
public partial class DatabaseExecutionMessages : UserControl
{
    /// <summary>构造不读取服务器或创建窗口。</summary>
    public DatabaseExecutionMessages() => InitializeComponent();
}
