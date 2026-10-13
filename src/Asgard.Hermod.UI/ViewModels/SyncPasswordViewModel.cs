namespace Asgard.Hermod.UI.ViewModels;

/// <summary>单次上传或下载的密码确认状态，释放时清除密码且不持久化。</summary>
public sealed class SyncPasswordViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    /// <summary>根据操作显示加密或解密说明；下载必须额外确认替换本地数据。</summary>
    public SyncPasswordViewModel(bool download, bool automatic = false)
    {
        IsDownload = download;
        IsAutomatic = automatic;
        ConfirmCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, true), _ => CanConfirm);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !_disposed);
    }

    /// <summary>通知模态窗口确认或取消，不直接调用同步服务。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>稳定操作标识，界面文案不参与业务判断。</summary>
    public bool IsDownload { get; }
    /// <summary>自动同步配置只需一次解锁，明确说明本机保护保存。</summary>
    public bool IsAutomatic { get; }
    /// <summary>标题与确认按钮明确指出将执行的动作。</summary>
    public string Title => Text.Get(IsAutomatic ? "SyncEnable" : IsDownload ? "DownloadWorkspace" : "UploadWorkspace");
    /// <summary>解释密码的用途、保存方式与当前操作的影响。</summary>
    public string Explanation => Text.Get(IsAutomatic ? "SyncEnableExplanation" : IsDownload ? "SyncDownloadPasswordExplanation" : "SyncUploadPasswordExplanation");
    /// <summary>自动与旧手动模式分别说明密码保存方式，避免相互矛盾。</summary>
    public string PasswordHelp => Text.Get(IsAutomatic ? "SyncRememberedPasswordHelp" : "SyncPasswordHelp");
    /// <summary>只在这次弹窗中使用的密码，不用于下一次操作。</summary>
    public string Password
    {
        get;
        set { if (SetProperty(ref field, value)) ConfirmCommand.NotifyCanExecuteChanged(); }
    } = "";
    /// <summary>用户明确同意下载替换全部本地实例和标签。</summary>
    public bool ConfirmRestore
    {
        get;
        set { if (SetProperty(ref field, value)) ConfirmCommand.NotifyCanExecuteChanged(); }
    }
    /// <summary>长度有效且下载影响已确认时才允许提交。</summary>
    public bool CanConfirm => !_disposed && Password.Length is >= 12 and <= 128 && (!IsDownload || ConfirmRestore);
    /// <summary>确认本次操作。</summary>
    public RelayCommand ConfirmCommand { get; }
    /// <summary>取消后不执行任何上传或下载。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>同步或取消后及时清除密码与确认状态。</summary>
    public void Dispose()
    {
        _disposed = true;
        Password = "";
        ConfirmRestore = false;
        ConfirmCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
