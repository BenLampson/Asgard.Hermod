namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

public sealed partial class ObjectStorageWorkspaceViewModel
{
    /// <summary>刷新本目录或重试容器读取。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>继续请求同一查询的下一批对象。</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }
    /// <summary>选择文件和处理同名策略。</summary>
    public AsyncRelayCommand UploadCommand { get; }
    /// <summary>创建当前目录下的占位文件夹。</summary>
    public AsyncRelayCommand NewFolderCommand { get; }
    /// <summary>下载当前或指定对象。</summary>
    public AsyncRelayCommand DownloadCommand { get; }
    /// <summary>下载所有勾选对象及目录。</summary>
    public AsyncRelayCommand BatchDownloadCommand { get; }
    /// <summary>先枚举再确认删除的完整范围。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
    /// <summary>生成有限期只读签名 URL。</summary>
    public AsyncRelayCommand TemporaryLinkCommand { get; }
    /// <summary>复制对象键。</summary>
    public RelayCommand CopyKeyCommand { get; }
    /// <summary>复制只读文本预览。</summary>
    public RelayCommand CopyPreviewCommand { get; }
    /// <summary>清空当前页的勾选。</summary>
    public RelayCommand ClearSelectionCommand { get; }
    /// <summary>暂时收起对象详情，保留所选对象。</summary>
    public RelayCommand CloseDetailsCommand { get; }
    /// <summary>从列表工具栏展开或收起详情。</summary>
    public AsyncRelayCommand ToggleDetailsCommand { get; }
    /// <summary>关闭操作失败提示。</summary>
    public RelayCommand ClearErrorCommand { get; }
    /// <summary>清空当前目录搜索。</summary>
    public AsyncRelayCommand ClearSearchCommand { get; }
    /// <summary>编辑已有连接，成功后重新读取真实容器。</summary>
    public AsyncRelayCommand EditConnectionCommand { get; }
}
