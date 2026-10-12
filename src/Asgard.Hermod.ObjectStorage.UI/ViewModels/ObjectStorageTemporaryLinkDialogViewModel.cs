namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>临时链接仅在用户点击生成后请求，签名 URL 不自动复制或打开。</summary>
public sealed class ObjectStorageTemporaryLinkDialogViewModel : ObjectStorageDialogViewModel
{
    private readonly Func<TimeSpan, Task<string>> _generate;
    private readonly Func<string, bool> _copy;
    private int _revision;

    /// <summary>生成与剪贴板都通过宿主提供的明确操作回调。</summary>
    public ObjectStorageTemporaryLinkDialogViewModel(string key, Func<TimeSpan, Task<string>> generate,
        Func<string, bool> copy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(copy);
        Key = key;
        _generate = generate;
        _copy = copy;
        SelectedLifetime = Lifetimes[0];
        GenerateCommand = new AsyncRelayCommand(_ => GenerateAsync(), _ => !IsDisposed && !IsGenerating);
        CopyCommand = new RelayCommand(_ => CopyLink(), _ => !IsDisposed && !IsGenerating && Link.Length > 0);
    }

    /// <summary>完整目标对象键。</summary>
    public string Key { get; }
    /// <summary>设计稿只提供十五分钟与一小时。</summary>
    public IReadOnlyList<ObjectStorageLinkLifetime> Lifetimes { get; } =
        [new(TimeSpan.FromMinutes(15), Text.Get("15Minutes")), new(TimeSpan.FromHours(1), Text.Get("1Hour"))];
    /// <summary>改变有效期后旧链接失效，迟到结果不会显示为新有效期的链接。</summary>
    public ObjectStorageLinkLifetime SelectedLifetime
    {
        get;
        set
        {
            if (value is null || !Lifetimes.Contains(value) || !SetProperty(ref field, value))
                return;
            _revision++;
            Link = "";
            Error = "";
            CopyStatus = "";
            if (GenerateCommand is not null)
                RefreshCommands();
        }
    } = null!;
    /// <summary>生成期间禁止重复操作。</summary>
    public bool IsGenerating { get; private set; }
    /// <summary>原样显示签名 URL，不进入导航信息或日志。</summary>
    public string Link { get; private set; } = "";
    /// <summary>适配器安全消息或固定的失败说明。</summary>
    public string Error { get; private set; } = "";
    /// <summary>剪贴板回调成功后才显示完成。</summary>
    public string CopyStatus { get; private set; } = "";
    /// <summary>按钮在真实请求期间提示生成中。</summary>
    public string GenerateLabel => IsGenerating ? Text.Get("Generating") : Text.Get("GenerateLink");
    /// <summary>避免生成中的有效期选择与请求快照不一致。</summary>
    public bool CanSelectLifetime => !IsDisposed && !IsGenerating;
    /// <summary>只在用户请求时生成签名链接。</summary>
    public AsyncRelayCommand GenerateCommand { get; }
    /// <summary>只在用户点击复制时写入剪贴板。</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>关闭后不呈现迟到链接，也不留下可以再次执行的复制入口。</summary>
    public override void Dispose()
    {
        if (IsDisposed)
            return;
        base.Dispose();
        _revision++;
        Link = "";
        IsGenerating = false;
        RefreshCommands();
    }

    /// <summary>持有有效期版本，重复操作、切换有效期或关闭都不能接收旧结果。</summary>
    private async Task GenerateAsync()
    {
        int revision = _revision;
        TimeSpan lifetime = SelectedLifetime.Value;
        IsGenerating = true;
        Link = "";
        Error = "";
        CopyStatus = "";
        RefreshCommands();
        try
        {
            string link = await _generate(lifetime);
            if (IsDisposed || revision != _revision)
                return;
            if (string.IsNullOrWhiteSpace(link))
                Error = Text.Get("CannotGenerateATemporaryLinkTryAgainLater");
            else
                Link = link;
        }
        catch (Exception exception)
        {
            if (!IsDisposed && revision == _revision)
                Error = Text.Error(exception, "CannotGenerateATemporaryLinkTryAgainLater");
        }
        finally
        {
            if (!IsDisposed)
            {
                IsGenerating = false;
                RefreshCommands();
            }
        }
    }

    /// <summary>复制失败不显示系统异常，成功提示只由实际剪贴板结果决定。</summary>
    private void CopyLink()
    {
        try
        {
            bool copied = _copy(Link);
            CopyStatus = copied ? Text.Get("LinkCopied") : "";
            Error = copied ? "" : Text.Get("CannotWriteToTheClipboardTryAgainLater");
        }
        catch (Exception)
        {
            CopyStatus = "";
            Error = Text.Get("CannotWriteToTheClipboardTryAgainLater");
        }
        RefreshCommands();
    }

    /// <summary>状态和命令启用条件一起更新。</summary>
    private void RefreshCommands()
    {
        foreach (string property in new[] { nameof(IsGenerating), nameof(Link), nameof(Error), nameof(CopyStatus),
            nameof(GenerateLabel), nameof(CanSelectLifetime) })
            OnPropertyChanged(property);
        GenerateCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }
}
