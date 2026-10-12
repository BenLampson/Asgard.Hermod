namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>一行对象的展示与批量勾选，详情选择由页面单独管理。</summary>
public sealed class ObjectStorageEntryViewModel : ObservableObject
{
    private bool _isChecked;

    /// <summary>保存安全快照，并把勾选变化通知页面。</summary>
    public ObjectStorageEntryViewModel(ObjectStorageEntry entry, Action selectionChanged)
    {
        Entry = entry;
        PropertyChanged += (_, args) => { if (args.PropertyName == nameof(IsChecked)) selectionChanged(); };
    }

    /// <summary>本次列表返回的不可变对象快照。</summary>
    public ObjectStorageEntry Entry { get; }
    /// <summary>完整对象键用于下载、删除及复制。</summary>
    public string Key => Entry.Key;
    /// <summary>对象行显示文件或目录名称。</summary>
    public string Name => ObjectStoragePresentation.Name(Key);
    /// <summary>目录前缀只在双击时进入。</summary>
    public bool IsDirectory => Entry.IsDirectory;
    /// <summary>对象类型对应的官方图标。</summary>
    public string Icon => ObjectStoragePresentation.Icon(Key, IsDirectory);
    /// <summary>目录不显示虚构的汇总大小。</summary>
    public string Size => IsDirectory ? "—" : ObjectStoragePresentation.Size(Entry.Size);
    /// <summary>目录没有对象存储类型。</summary>
    public string StorageClass => IsDirectory ? "—" : ObjectStoragePresentation.StorageClass(Entry.StorageClass);
    /// <summary>对象最后更新时间。</summary>
    public string Modified => ObjectStoragePresentation.Modified(Entry.LastModified);
    /// <summary>批量操作的勾选状态不改变详情中的当前对象。</summary>
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}
