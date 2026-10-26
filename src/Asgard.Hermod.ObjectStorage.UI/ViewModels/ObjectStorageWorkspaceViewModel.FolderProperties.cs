namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

public sealed partial class ObjectStorageWorkspaceViewModel
{
    /// <summary>工具栏统计当前目录，行菜单统计该行目录，绝不从显示名称推断路径。</summary>
    public AsyncRelayCommand FolderPropertiesCommand { get; private set; } = null!;

    private void InitializeFolderPropertiesCommand() => FolderPropertiesCommand = new AsyncRelayCommand(async parameter =>
    {
        if (!CanOperate || SelectedBucket is not { } bucket) return;
        string prefix = parameter is ObjectStorageEntryViewModel { IsDirectory: true } row ? row.Key : Prefix;
        await InteractAsync(() => _dialogs.ShowFolderPropertiesAsync(bucket, prefix,
            (progress, token) => _service.GetFolderStatisticsAsync(_instance.Id, bucket, prefix, progress, token), _lifetime.Token));
    }, parameter => CanOperate && (parameter is null or ObjectStorageEntryViewModel { IsDirectory: true }));
}
