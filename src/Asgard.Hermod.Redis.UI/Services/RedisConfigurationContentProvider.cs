using Asgard.Hermod.Redis.UI.Views;

namespace Asgard.Hermod.Redis.UI.Services;

/// <summary>通过公共插槽注册信息页，不增加重复保存流程。</summary>
public sealed class RedisConfigurationContentProvider : IInstanceConfigurationContentProvider
{
    /// <inheritdoc />
    public string TypeId => RedisIds.Type;
    /// <inheritdoc />
    public Size PreferredWizardSize => new(912, 720);
    /// <inheritdoc />
    public FrameworkElement CreateContent(NewInstanceViewModel model) => new RedisConnectionEditor(model);
}
