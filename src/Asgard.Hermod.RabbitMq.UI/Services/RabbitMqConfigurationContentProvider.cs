using Asgard.Hermod.RabbitMq.UI.Views;

namespace Asgard.Hermod.RabbitMq.UI.Services;

/// <summary>复用公共流程、测试、托管和标签保存。</summary>
public sealed class RabbitMqConfigurationContentProvider : IInstanceConfigurationContentProvider
{
    /// <inheritdoc />
    public string TypeId => RabbitMqIds.Type;
    /// <inheritdoc />
    public Size PreferredWizardSize => new(820, 700);
    /// <inheritdoc />
    public FrameworkElement CreateContent(NewInstanceViewModel model) => new RabbitMqConnectionEditor(model);
}
