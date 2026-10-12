using System.IO;
using Asgard.Hermod.UI.Views.Dialogs;
using Microsoft.Win32;

namespace Asgard.Hermod.ObjectStorage.UI.Services;

/// <summary>集中封装对象操作窗体、目的明确的系统文件选择以及显式剪贴板操作。</summary>
public sealed class WindowsObjectStorageDialogService : IObjectStorageDialogService
{
    private readonly Func<Window?> _owner;
    private Window? _activeDialog;

    /// <summary>所属窗口由宿主按当前主窗口状态提供。</summary>
    public WindowsObjectStorageDialogService(Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>完成队列检查后只返回任务输入，服务不直接上传任何对象。</summary>
    public Task<ObjectStorageUploadSelection?> SelectUploadAsync(ObjectStorageBucket bucket, string prefix)
    {
        using var model = new ObjectStorageUploadViewModel(bucket, prefix);
        var window = new ObjectStorageUploadWindow(model);
        EventHandler selectFiles = async (_, _) =>
        {
            try
            {
                var picker = new OpenFileDialog
                {
                    Title = "选择上传文件",
                    Multiselect = true,
                    CheckFileExists = true,
                    DereferenceLinks = false,
                    Filter = "所有文件|*.*",
                };
                if (picker.ShowDialog(window) == true)
                    await model.AddPathsAsync(picker.FileNames);
            }
            catch (Exception) { model.ReportSelectionError(); }
        };
        EventHandler selectFolder = async (_, _) =>
        {
            try
            {
                var picker = new OpenFolderDialog { Title = "选择上传文件夹" };
                if (picker.ShowDialog(window) == true)
                    await model.AddPathsAsync([picker.FolderName]);
            }
            catch (Exception) { model.ReportSelectionError(); }
        };
        model.SelectFilesRequested += selectFiles;
        model.SelectFolderRequested += selectFolder;
        try
        {
            bool accepted = Show(window) == true;
            return Task.FromResult(accepted ? model.Result : null);
        }
        finally
        {
            model.SelectFilesRequested -= selectFiles;
            model.SelectFolderRequested -= selectFolder;
        }
    }

    /// <summary>建议名称只作为文件名交给保存 API，本地覆盖由系统保存窗体确认。</summary>
    public Task<string?> SelectDownloadPathAsync(string suggestedName)
    {
        try
        {
            var picker = new SaveFileDialog
            {
                Title = "保存对象",
                FileName = DownloadFileName(suggestedName),
                Filter = "所有文件|*.*",
                OverwritePrompt = true,
                AddExtension = true,
            };
            return Task.FromResult(ShowPicker(picker) == true ? picker.FileName : null);
        }
        catch (Exception)
        {
            ShowNotice("选择下载位置失败", "无法打开保存窗口，请稍后重试。");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>批量下载选择目录，远程对象键由执行服务安全映射到此目录。</summary>
    public Task<string?> SelectDownloadFolderAsync()
    {
        try
        {
            var picker = new OpenFolderDialog { Title = "选择下载文件夹" };
            return Task.FromResult(ShowPicker(picker) == true ? picker.FolderName : null);
        }
        catch (Exception)
        {
            ShowNotice("选择下载位置失败", "无法打开文件夹选择窗口，请稍后重试。");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>收集有效的单级名称，创建与同名检查仍由对象服务执行。</summary>
    public Task<string?> RequestFolderNameAsync(ObjectStorageBucket bucket, string prefix)
    {
        using var model = new ObjectStorageFolderDialogViewModel(bucket, prefix);
        return Task.FromResult(Show(new ObjectStorageFolderWindow(model)) == true ? model.Result : null);
    }

    /// <summary>只确认服务已枚举的键清单，窗体不扩大前缀删除范围。</summary>
    public Task<bool> ConfirmDeleteAsync(ObjectStorageBucket bucket, string prefix, ObjectStorageDeletePlan plan)
    {
        using var model = ObjectStorageConfirmationDialogViewModel.ForDelete(bucket, prefix, plan);
        return Task.FromResult(Show(new ObjectStorageConfirmationWindow(model)) == true && model.Result);
    }

    /// <summary>默认策略下每一个同名对象都由用户主动确认覆盖。</summary>
    public Task<bool> ConfirmOverwriteAsync(string key)
    {
        using var model = ObjectStorageConfirmationDialogViewModel.ForOverwrite(key);
        return Task.FromResult(Show(new ObjectStorageConfirmationWindow(model)) == true && model.Result);
    }

    /// <summary>生成回调只由按钮触发，签名链接只在点击复制后写入剪贴板。</summary>
    public Task ShowTemporaryLinkAsync(string key, Func<TimeSpan, Task<string>> generate)
    {
        using var model = new ObjectStorageTemporaryLinkDialogViewModel(key, generate, TryCopyText);
        Show(new ObjectStorageTemporaryLinkWindow(model));
        return Task.CompletedTask;
    }

    /// <summary>调用方只能在显式复制操作后调用；失败使用共享业务窗体安全提示。</summary>
    public void CopyText(string text)
    {
        if (!TryCopyText(text))
            ShowNotice("复制失败", "无法写入剪贴板，请稍后重试。");
    }

    /// <summary>为嵌套提示保留当前所属窗体，关闭后恢复上一层。</summary>
    private bool? Show(ObjectStorageDialogWindow window)
    {
        Window? previous = _activeDialog;
        window.Owner = previous ?? _owner();
        _activeDialog = window;
        try { return window.ShowDialog(); }
        finally { _activeDialog = previous; }
    }

    /// <summary>系统选择不会导航其他应用，也不拼接脚本命令。</summary>
    private bool? ShowPicker(CommonDialog picker)
    {
        Window? owner = _activeDialog ?? _owner();
        return owner is null ? picker.ShowDialog() : picker.ShowDialog(owner);
    }

    /// <summary>提示也使用统一模态容器，避免不同外观的系统消息框。</summary>
    private void ShowNotice(string title, string message)
    {
        using var model = ObjectStorageConfirmationDialogViewModel.ForNotice(title, message);
        Show(new ObjectStorageConfirmationWindow(model) { Width = 460, Height = 260 });
    }

    /// <summary>剪贴板被其他程序占用时返回失败，不泄露系统异常。</summary>
    private static bool TryCopyText(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (Exception) { return false; }
    }

    /// <summary>移除本地不允许的字符和目录部分，远程键不能成为任意本地绝对路径。</summary>
    private static string DownloadFileName(string suggestedName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string filename = Path.GetFileName(suggestedName);
        filename = new string(filename.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim(' ', '.');
        return filename.Length == 0 ? "download" : filename;
    }
}
