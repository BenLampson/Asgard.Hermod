namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>单个文件的有界传输状态；失败消息只使用安全资源。</summary>
public sealed class SshTransferItem(string name, bool upload) : ObservableObject
{
    /// <summary>用户文件名不翻译。</summary>
    public string Name { get; } = name;
    /// <summary>传输方向。</summary>
    public string Direction => Text.Get(upload ? "SshFilesUpload" : "SshFilesDownload");
    /// <summary>真实已传输字节。</summary>
    public long Bytes { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Progress)); } }
    /// <summary>进度展示。</summary>
    public string Progress => Text.Format("SshTransferBytes", Bytes);
    /// <summary>状态资源键独立于显示文字。</summary>
    public string StatusKey { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Status)); } } = "SshTransferRunning";
    /// <summary>本地化状态。</summary>
    public string Status => Text.Get(StatusKey);
}
