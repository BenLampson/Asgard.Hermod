using Asgard.Hermod.MySql.UI.Views;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>通过公共向导插槽提供 MySQL 连接正文。</summary>
public sealed class MySqlConfigurationContentProvider : IInstanceConfigurationContentProvider
{
    public string TypeId => MySqlIds.Type;
    public Size PreferredWizardSize => new(912, 740);
    public FrameworkElement CreateContent(NewInstanceViewModel model) => new MySqlConnectionEditor(model);
}
