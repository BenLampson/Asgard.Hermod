namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>只收集单级目录名称，实际创建和同名检查由对象服务负责。</summary>
public sealed class ObjectStorageFolderDialogViewModel : ObjectStorageDialogViewModel
{
    private bool _showValidation;

    /// <summary>展示当前容器与父目录，不把输入拼接为本地文件系统路径。</summary>
    public ObjectStorageFolderDialogViewModel(ObjectStorageBucket bucket, string prefix)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentNullException.ThrowIfNull(prefix);
        Location = bucket.Name + (prefix.Length == 0 ? " /" : " / " + prefix.TrimEnd('/'));
        ConfirmCommand = new RelayCommand(_ => Confirm(), _ => !IsDisposed);
    }

    /// <summary>当前目录位置。</summary>
    public string Location { get; }
    /// <summary>用户填写的单个目录名称。</summary>
    public string Name
    {
        get;
        set
        {
            if (!SetProperty(ref field, value ?? ""))
                return;
            OnPropertyChanged(nameof(Error));
        }
    } = "";
    /// <summary>点击创建后显示就地校验，不回显非法控制字符。</summary>
    public string Error => _showValidation ? ValidateName(Name) : "";
    /// <summary>确认后的有效名称，取消时为空。</summary>
    public string? Result { get; private set; }
    /// <summary>创建只确认名称，窗体不会直接写入 OSS。</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>目录名称不能表示路径、空白或点目录。</summary>
    public static string ValidateName(string value)
    {
        string name = value.Trim();
        if (name.Length == 0)
            return Text.Get("EnterAFolderName");
        if (name.Contains('/') || name.Contains('\\'))
            return Text.Get("NamesCannotContainOr");
        if (value.Any(char.IsControl))
            return Text.Get("NamesCannotContainControlCharacters");
        if (name.All(character => character == '.'))
            return Text.Get("NamesCannotConsistOnlyOfDots");
        return "";
    }

    /// <summary>外部关闭后创建命令失效。</summary>
    public override void Dispose()
    {
        base.Dispose();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>先验证，只有有效输入才给宿主返回确认结果。</summary>
    private void Confirm()
    {
        _showValidation = true;
        OnPropertyChanged(nameof(Error));
        if (ValidateName(Name).Length != 0)
            return;
        Result = Name.Trim();
        Close(true);
    }
}
