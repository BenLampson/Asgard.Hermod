namespace Asgard.Hermod.ObjectStorage.UI.Services;

/// <summary>存储类型的工作页入口，具体服务商由应用层对象存储服务选择。</summary>
public sealed class ObjectStoragePageProvider(IObjectStorageService storage,
    IObjectStorageDialogService dialogs, IInstanceEditor editor) : IInstancePageProvider
{
    /// <inheritdoc />
    public string TypeId => "storage";
    /// <inheritdoc />
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) =>
        new ObjectStorageWorkspaceViewModel(instance, storage, dialogs, editor);
}
