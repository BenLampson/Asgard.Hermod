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
    private readonly IEnumerable<IInstanceConfigurationContentProvider> _contents;
    private readonly ITagManagementService? _tags;

    /// <summary>延迟获取已启动的主窗口。</summary>
    public InstanceEditorService(Func<Window?> getOwner, IInstanceProviderRegistry providers,
        IInstanceConfigurationService configurations, IAccountService? accounts = null, IConfigurationSyncService? sync = null,
        IEnumerable<IInstanceConfigurationContentProvider>? contents = null, ITagManagementService? tags = null)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(configurations);
        _getOwner = getOwner;
        _providers = providers;
        _configurations = configurations;
        _accounts = accounts; _sync = sync;
        _contents = contents ?? [];
        _tags = tags;
    }

    /// <inheritdoc />
    public async Task<InstanceSummary?> CreateAsync()
    {
        if (_sync is not null) await _sync.PrepareWorkspaceAsync();
        var tags = _tags is null ? null : await _tags.GetTagsAsync();
        using var model = new NewInstanceViewModel(_providers, _configurations, _accounts, _sync, tags);
        var dialog = new NewInstanceWindow(model, _contents) { Owner = _getOwner() };
        return ShowAndClose(dialog, model);
    }

    /// <inheritdoc />
    public async Task<InstanceSummary?> EditAsync(string instanceId)
    {
        if (_sync is not null) await _sync.PrepareWorkspaceAsync();
        var existing = _configurations.GetInstances().SingleOrDefault(instance => instance.Id == instanceId)
            ?? throw Text.Exception("ThisInstanceNoLongerExistsRefreshTheInstanceList");
        var connection = await _configurations.GetConnectionAsync(instanceId)
            ?? throw Text.Exception("CannotReadThisInstanceConfigurationReopenAndTryAgain");
        var tags = _tags is null ? null : await _tags.GetTagsAsync();
        var tagIds = _tags is null ? null : await _configurations.GetTagIdsAsync(instanceId);
        using var model = new NewInstanceViewModel(_providers, _configurations, existing, connection, _accounts, _sync, tags, tagIds);
        var dialog = new NewInstanceWindow(model, _contents) { Owner = _getOwner() };
        return ShowAndClose(dialog, model);
    }

    /// <summary>ShowDialog 抛错时不会自动销毁窗口；先清理向导，再让宿主显示原始安全错误。</summary>
    private static InstanceSummary? ShowAndClose(NewInstanceWindow dialog, NewInstanceViewModel model)
    {
        try { return dialog.ShowDialog() == true ? model.CreatedInstance : null; }
        finally { dialog.CloseAfterFailure(); }
    }
}
