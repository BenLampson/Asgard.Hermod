using System.IO;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>校验单个后缀与真实可执行文件，保存成功才关闭弹窗。</summary>
public sealed class FileOpenRuleEditorViewModel : ObservableObject
{
    private readonly IFileOpenRuleStore _store;
    private readonly FileOpenRule? _original;
    private string _extension;
    private string _programPath;
    private string _error = "";

    /// <summary>新增默认留空，编辑保留原规则用于原子重命名。</summary>
    public FileOpenRuleEditorViewModel(IFileOpenRuleStore store, FileOpenRule? original = null)
    {
        _store = store; _original = original;
        _extension = original?.Extension ?? "";
        _programPath = original?.ProgramPath ?? "";
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
        BrowseCommand = new RelayCommand(_ => BrowseRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>标题随新增或编辑变化，控制逻辑使用原规则标识。</summary>
    public string Title => Text.Get(_original is null ? "AddFileRule" : "EditFileRule");
    /// <summary>用户输入后缀。</summary>
    public string Extension { get => _extension; set => SetProperty(ref _extension, value); }
    /// <summary>所选程序的绝对路径，不接受命令行模板。</summary>
    public string ProgramPath { get => _programPath; set => SetProperty(ref _programPath, value); }
    /// <summary>字段和保存错误经过安全资源转换。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>保存后的规范化规则。</summary>
    public FileOpenRule? Result { get; private set; }
    /// <summary>保存规则。</summary>
    public RelayCommand SaveCommand { get; }
    /// <summary>取消编辑，不写入数据库。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>选择程序。</summary>
    public RelayCommand BrowseCommand { get; }
    /// <summary>窗口负责系统选择器及所属关系。</summary>
    public event EventHandler? BrowseRequested;
    /// <summary>保存成功或取消后关闭窗口。</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>选择器失败不显示原始系统信息。</summary>
    public void ReportBrowseError() => Error = Text.Get("FileProgramSelectFailed");

    /// <summary>持久化之前检查路径与文件，保存失败保留输入和原规则。</summary>
    private void Save()
    {
        try
        {
            var rule = new FileOpenRule(Extension, ProgramPath.Trim());
            if (!File.Exists(rule.ProgramPath)) throw Text.Exception("FileProgramMissing");
            _store.Save(rule, _original?.Extension);
            Result = rule;
            Error = "";
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception exception) { Error = Text.Error(exception, "FileRuleSaveFailed"); }
    }
}
