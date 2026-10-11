namespace Asgard.Hermod.UI.ViewModels;

/// <summary>上传队列只展示已经选定的文件元数据，不读取本地文件内容。</summary>
public sealed class ObjectStorageUploadQueueItemViewModel(ObjectStorageUploadItem item)
{
    /// <summary>确认时原样交给上传服务的本地文件与对象键。</summary>
    public ObjectStorageUploadItem Item { get; } = item;
    /// <summary>完整目标对象键保留所选目录的相对结构。</summary>
    public string Key => Item.Key;
    /// <summary>共享对象图标规则。</summary>
    public string Icon => ObjectStoragePresentation.Icon(Key);
    /// <summary>设计稿中的可读文件大小。</summary>
    public string SizeText => ObjectStoragePresentation.Size(Item.Size);
}
