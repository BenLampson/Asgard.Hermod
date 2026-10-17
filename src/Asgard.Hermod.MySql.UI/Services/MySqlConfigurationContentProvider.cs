using Asgard.Hermod.MySql.UI.Views;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>通过公共向导插槽提供 MySQL 连接正文。</summary>
public sealed class MySqlConfigurationContentProvider : IInstanceConfigurationContentProvider
{
    /// <summary>公共注册表使用的稳定实例类型标识。</summary>
    public string TypeId => MySqlIds.Type;
    /// <summary>公共向导建议尺寸，仍受当前屏幕约束。</summary>
    public Size PreferredWizardSize => new(912, 740);
    /// <summary>在公共向导中创建模块正文。</summary>
    public FrameworkElement CreateContent(NewInstanceViewModel model) => new MySqlConnectionEditor(model);
}
