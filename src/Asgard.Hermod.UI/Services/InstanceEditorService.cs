using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>由宿主决定新建实例窗口的所有者。</summary>
public sealed class InstanceEditorService : IInstanceEditor
{
    private readonly Func<Window?> _getOwner;
    private readonly IInstanceProviderRegistry _providers;
    private readonly IInstanceConfigurationService _configurations;
    private readonly IAccountService? _accounts;
    private readonly IConfigurationSyncService? _sync;

    /// <summary>延迟获取已启动的主窗口。</summary>
    public InstanceEditorService(Func<Window?> getOwner, IInstanceProviderRegistry providers,
        IInstanceConfigurationService configurations, IAccountService? accounts = null, IConfigurationSyncService? sync = null)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configurations);
        _getOwner = getOwner;
        _providers = providers;
        _configurations = configurations;
        _accounts = accounts; _sync = sync;
    }

    /// <inheritdoc />
    public async Task<InstanceSummary?> CreateAsync()
    {
        if (_sync is not null) await _sync.PrepareWorkspaceAsync();
        using var model = new NewInstanceViewModel(_providers, _configurations, _accounts, _sync);
        var dialog = new NewInstanceWindow(model) { Owner = _getOwner() };
        InstanceSummary? created = dialog.ShowDialog() == true ? model.CreatedInstance : null;
        return created;
    }

    /// <inheritdoc />
    public async Task<InstanceSummary?> EditAsync(string instanceId)
    {
        if (_sync is not null) await _sync.PrepareWorkspaceAsync();
        var existing = _configurations.GetInstances().SingleOrDefault(instance => instance.Id == instanceId)
            ?? throw Text.Exception("ThisInstanceNoLongerExistsRefreshTheInstanceList");
        var connection = await _configurations.GetConnectionAsync(instanceId)
            ?? throw Text.Exception("CannotReadThisInstanceConfigurationReopenAndTryAgain");
        using var model = new NewInstanceViewModel(_providers, _configurations, existing, connection, _accounts, _sync);
        var dialog = new NewInstanceWindow(model) { Owner = _getOwner() };
        return dialog.ShowDialog() == true ? model.CreatedInstance : null;
    }
}
