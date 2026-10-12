using Asgard.Hermod.UI.Controls;
using Microsoft.Win32;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>规则编辑器使用设置窗口作为模态所有者。</summary>
public partial class FileOpenRuleWindow : HermodDialog
{
    private readonly FileOpenRuleEditorViewModel _model;

    /// <summary>系统文件选择只接受存在的 exe 文件。</summary>
    public FileOpenRuleWindow(FileOpenRuleEditorViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        model.BrowseRequested += OnBrowseRequested;
        Loaded += (_, _) => ExtensionInput.Focus();
        Closed += (_, _) =>
        {
            model.CloseRequested -= OnCloseRequested;
            model.BrowseRequested -= OnBrowseRequested;
        };
    }

    /// <summary>保存完成或取消后把结果交回设置页。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;

    /// <summary>只传递程序地址，不解析或保存额外命令参数。</summary>
    private void OnBrowseRequested(object? sender, EventArgs args)
    {
        try
        {
            var picker = new OpenFileDialog { Title = Text.Get("SelectFileProgram"), Filter = Text.Get("ExecutableFiles"), CheckFileExists = true };
            if (picker.ShowDialog(this) == true) _model.ProgramPath = picker.FileName;
        }
        catch (Exception) { _model.ReportBrowseError(); }
    }
}
