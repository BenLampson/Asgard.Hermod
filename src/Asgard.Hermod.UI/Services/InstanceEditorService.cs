using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主决定新建实例窗口的所有者。</summary>
public sealed class InstanceEditorService : IInstanceEditor
{
    private readonly Func<Window?> _getOwner;
    private readonly IInstanceProviderRegistry _providers;
    private readonly IInstanceConfigurationService _configurations;

    /// <summary>延迟获取已启动的主窗口。</summary>
    public InstanceEditorService(Func<Window?> getOwner, IInstanceProviderRegistry providers,
        IInstanceConfigurationService configurations)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configurations);
        _getOwner = getOwner;
        _providers = providers;
        _configurations = configurations;
    }

    /// <inheritdoc />
    public Task<InstanceSummary?> CreateAsync()
    {
        using var model = new NewInstanceViewModel(_providers, _configurations);
        var dialog = new NewInstanceWindow(model) { Owner = _getOwner() };
        InstanceSummary? created = dialog.ShowDialog() == true ? model.CreatedInstance : null;
        return Task.FromResult(created);
    }

    /// <inheritdoc />
    public async Task<InstanceSummary?> EditAsync(string instanceId)
    {
        var existing = _configurations.GetInstances().SingleOrDefault(instance => instance.Id == instanceId)
            ?? throw Text.Exception("ThisInstanceNoLongerExistsRefreshTheInstanceList");
        var connection = await _configurations.GetConnectionAsync(instanceId)
            ?? throw Text.Exception("CannotReadThisInstanceConfigurationReopenAndTryAgain");
        using var model = new NewInstanceViewModel(_providers, _configurations, existing, connection);
        var dialog = new NewInstanceWindow(model) { Owner = _getOwner() };
        return dialog.ShowDialog() == true ? model.CreatedInstance : null;
    }
}
