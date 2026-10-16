namespace Asgard.Hermod.UI.Controls;

/// <summary>数据库范围唯一入口；增删仍使用模块的完整确认与草稿保护。</summary>
public partial class DatabaseScopePicker : UserControl
{
    /// <summary>不读取数据库或推断范围，只呈现当前 Session。</summary>
    public DatabaseScopePicker() => InitializeComponent();
}
