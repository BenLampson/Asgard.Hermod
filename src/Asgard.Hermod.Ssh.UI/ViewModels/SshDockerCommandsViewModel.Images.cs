using System.Collections.ObjectModel;
using System.Windows.Data;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>镜像快照和搜索与容器选择、操作输出相互独立。</summary>
public sealed partial class SshDockerCommandsViewModel
{
    /// <summary>当前连接的镜像列表。</summary>
    public ObservableCollection<SshDockerImageSnapshot> Images { get; } = [];
    /// <summary>仅在本机筛选已加载的快照。</summary>
    public ICollectionView ImageView { get; private set; } = null!;
    /// <summary>按仓库、标签或完整 ID 查找镜像。</summary>
    public string ImageSearch
    {
        get;
        set { if (SetProperty(ref field, value)) { ImageView.Refresh(); OnPropertyChanged(nameof(HasNoImages)); OnPropertyChanged(nameof(ImageEmptyMessage)); } }
    } = "";
    /// <summary>读取成功后才将空列表描述为没有结果。</summary>
    public bool HasImageSnapshot
    {
        get;
        private set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(HasNoImages)); OnPropertyChanged(nameof(HasNoImageSnapshot)); } }
    }
    /// <summary>传输或条目上限导致列表不完整。</summary>
    public bool ImagesLimited { get; private set => SetProperty(ref field, value); }
    /// <summary>当前筛选没有匹配的镜像。</summary>
    public bool HasNoImages => HasImageSnapshot && ImageView.IsEmpty && !IsBusy;
    /// <summary>筛选无结果时不暗示服务器没有镜像。</summary>
    public string ImageEmptyMessage => Text.Get(ImageSearch.Length > 0 ? "SshMonitorNoMatches" : "DockerNoImages");
    /// <summary>尚未取得镜像快照。</summary>
    public bool HasNoImageSnapshot => !HasImageSnapshot && !IsBusy;

    private void InitializeImageView()
    {
        ImageView = CollectionViewSource.GetDefaultView(Images);
        ImageView.Filter = item => item is SshDockerImageSnapshot image && (ImageSearch.Length == 0 ||
            image.Repository.Contains(ImageSearch, StringComparison.OrdinalIgnoreCase) || image.Tag.Contains(ImageSearch, StringComparison.OrdinalIgnoreCase) ||
            image.Id.Contains(ImageSearch, StringComparison.OrdinalIgnoreCase));
        ImageView.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoImages));
    }

    private async Task ReadImagesAsync(ISshWorkspaceConnection connection, long generation, CancellationToken token)
    {
        var result = await connection.ExecuteCommandAsync(SshDockerImageReader.Command, token).WaitAsync(token);
        EnsureCurrent(connection, generation, token);
        var items = SshDockerImageReader.Parse(result, out bool limited);
        // 解析完整后才替换快照，失败不会把部分数据误认为完整列表。
        Images.Clear();
        foreach (var item in items) Images.Add(item);
        ImagesLimited = limited;
        HasImageSnapshot = true;
    }

    private void ClearImages() { Images.Clear(); HasImageSnapshot = false; ImagesLimited = false; }
}
