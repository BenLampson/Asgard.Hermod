namespace Asgard.Hermod.UI.ViewModels;

/// <summary>显示冻结的访问请求；下载位置只能由用户通过目录选择器决定。</summary>
public sealed class McpApprovalViewModel : ObservableObject
{
    private string _downloadFolder;
    /// <summary>确认窗口不保存配对令牌或连接凭据。</summary>
    public McpApprovalViewModel(McpApprovalRequest request)
    {
        Request = request;
        _downloadFolder = request.SuggestedDownloadFolder.Length > 0 ? request.SuggestedDownloadFolder
            : request.ShowDownloadFolder ? WindowsDownloadFolder.GetDefault() : "";
    }
    /// <summary>确认时显示的实例和操作。</summary>
    public McpApprovalRequest Request { get; }
    /// <summary>说明本连接中实例级确认的有效范围。</summary>
    public string Hint => Text.Get("McpApprovalHint");
    /// <summary>对象存储实例允许在首次确认时选择默认下载目录。</summary>
    public bool ShowDownloadFolder => Request.ShowDownloadFolder || Request.RequiresDownloadFolder;
    /// <summary>本次下载将写入的文件夹。</summary>
    public string DownloadFolder
    {
        get => _downloadFolder;
        set { if (SetProperty(ref _downloadFolder, value)) OnPropertyChanged(nameof(CanApprove)); }
    }
    /// <summary>单文件下载展示确认的文件名，文件夹下载不显示空名称。</summary>
    public string DownloadFileLabel => Request.DownloadFileName.Length == 0 ? "" : Text.Format("McpApprovalDownloadFile", Request.DownloadFileName);
    /// <summary>对象存储实例需要一个默认下载目录。</summary>
    public bool CanApprove => !ShowDownloadFolder || !string.IsNullOrWhiteSpace(DownloadFolder);
}
