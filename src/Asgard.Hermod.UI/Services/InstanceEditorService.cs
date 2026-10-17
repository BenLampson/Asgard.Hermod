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
        var expectedAccount = _accounts?.Account;
        dialog.DeleteRequested = () => Task.FromResult(new DeleteInstanceDialog(existing.Name,
            () => _configurations.DeleteAsync(instanceId, new InstanceDeleteRequest(expectedAccount)))
        { Owner = dialog }.ShowDialog() == true);
        return ShowAndClose(dialog, model);
    }

    /// <inheritdoc />
    public Task<InstanceSummary?> RenameAsync(string instanceId) => EditMetadataAsync(instanceId, true, true);
    /// <inheritdoc />
    public Task<InstanceSummary?> EditTagsAsync(string instanceId) => EditMetadataAsync(instanceId, true);
    private async Task<InstanceSummary?> EditMetadataAsync(string instanceId, bool editTags, bool editNameAndTags = false)
    {
        if (_sync is not null) await _sync.PrepareWorkspaceAsync();
        var expectedAccount = _accounts?.Account;
        var existing = _configurations.GetInstances().SingleOrDefault(instance => instance.Id == instanceId)
            ?? throw Text.Exception("ThisInstanceNoLongerExistsRefreshTheInstanceList");
        var tags = editTags && _tags is not null ? await _tags.GetTagsAsync() : null;
        var selectedIds = editTags ? await _configurations.GetTagIdsAsync(instanceId) : null;
        InstanceSummary? result = null;
        using var model = new MetadataEditorViewModel(Text.Get(editTags && !editNameAndTags ? "EditInstanceTags" : "EditInstanceDetails"), Text.Get("InstanceName"), existing.Name, 128,
            async (name, ids, hosting, token) => result = await _configurations.UpdateMetadataAsync(instanceId, new()
            {
                Name = editTags && !editNameAndTags ? null : name,
                TagIds = editTags ? ids : null,
                HostingProviderId = hosting,
                ExpectedAccount = expectedAccount,
                CheckExpectedAccount = true
            }, token),
            isTagEditing: editTags, tags: tags, selectedIds: selectedIds, editNameAndTags: editNameAndTags,
            editHostingProvider: editNameAndTags && InstanceHostingProviders.Supports(existing.TypeId), hostingProviderId: existing.HostingProviderId);
        var dialog = new MetadataEditorWindow(model) { Owner = _getOwner() };
        try { return dialog.ShowDialog() == true ? result : null; }
        finally { dialog.Close(); }
    }

    /// <summary>ShowDialog 抛错时不会自动销毁窗口；先清理向导，再让宿主显示原始安全错误。</summary>
    private static InstanceSummary? ShowAndClose(NewInstanceWindow dialog, NewInstanceViewModel model)
    {
        try { return dialog.ShowDialog() == true ? model.CreatedInstance : null; }
        finally { dialog.CloseAfterFailure(); }
    }
}
