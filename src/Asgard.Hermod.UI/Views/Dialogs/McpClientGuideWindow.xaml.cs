using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>在独立指南中展示配对配置与开场提示，仅在用户点击时复制令牌。</summary>
public partial class McpClientGuideWindow : HermodDialog
{
    /// <summary>与当前设置页共用接入状态和复制命令。</summary>
    public McpClientGuideWindow(McpSettingsViewModel model)
    {
        InitializeComponent(); DataContext = model;
    }
}
