namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>同名覆盖及安全提示共用明确确认窗体。</summary>
public sealed class ObjectStorageConfirmationDialogViewModel : ObjectStorageDialogViewModel
{
    /// <summary>传入的键会立即复制，确认期间不会随页面导航变化。</summary>
    private ObjectStorageConfirmationDialogViewModel(string title, string heading, string location,
        IReadOnlyList<string> keys, string warning, string confirmLabel, bool isDestructive, bool requiresConfirmation)
    {
        Title = title;
        Heading = heading;
        Location = location;
        Keys = keys.ToArray();
        Warning = warning;
        ConfirmLabel = confirmLabel;
        IsDestructive = isDestructive;
        RequiresConfirmation = requiresConfirmation;
        ConfirmCommand = new RelayCommand(_ => { Result = true; Close(true); },
            _ => !IsDisposed && (Keys.Count > 0 || !RequiresConfirmation));
    }

    /// <summary>窗口标题。</summary>
    public string Title { get; }
    /// <summary>正文确认问题。</summary>
    public string Heading { get; }
    /// <summary>明确的容器与目录位置。</summary>
    public string Location { get; }
    /// <summary>显示完整对象键。</summary>
    public IReadOnlyList<string> Keys { get; }
    /// <summary>设计稿中的结果说明。</summary>
    public string Warning { get; }
    /// <summary>主按钮明确说明操作范围。</summary>
    public string ConfirmLabel { get; }
    /// <summary>覆盖使用警示操作外观。</summary>
    public bool IsDestructive { get; }
    /// <summary>纯提示只有关闭按钮，不需要重复确认。</summary>
    public bool RequiresConfirmation { get; }
    /// <summary>用户明确确认才为真。</summary>
    public bool Result { get; private set; }
    /// <summary>确认只返回意图，窗体不会执行覆盖。</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>逐项询问策略下清楚展示将被覆盖的对象键。</summary>
    public static ObjectStorageConfirmationDialogViewModel ForOverwrite(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new(Text.Get("OverwriteExistingObjects"), Text.Get("OverwriteThisExistingObject"), "", [key],
            Text.Get("ThisObjectAlreadyExistsUploadingWillReplaceItsCurrentContent"), Text.Get("OverwriteAndUpload"), true, true);
    }

    /// <summary>系统剪贴板失败等提示同样复用业务窗体，不使用系统消息框。</summary>
    public static ObjectStorageConfirmationDialogViewModel ForNotice(string title, string message) =>
        new(title, title, "", [], message, Text.Get("Close"), false, false);

    /// <summary>关闭后的确认不得触发另一轮操作。</summary>
    public override void Dispose()
    {
        base.Dispose();
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
