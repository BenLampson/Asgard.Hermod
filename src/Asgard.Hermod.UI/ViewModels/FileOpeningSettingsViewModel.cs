namespace Asgard.Hermod.UI.ViewModels;

/// <summary>规则列表只在数据库写入成功后更新，错误不暴露数据库路径。</summary>
public sealed class FileOpeningSettingsViewModel : ObservableObject
{
    private readonly IFileOpenRuleStore _store;
    private string _message = "";

    /// <summary>编辑窗体由所属设置窗口协调，状态只负责持久化与列表。</summary>
    public FileOpeningSettingsViewModel(IFileOpenRuleStore store)
    {
        _store = store;
        AddCommand = new RelayCommand(_ => EditRequested?.Invoke(this, null));
        EditCommand = new RelayCommand(item => { if (item is FileOpenRule rule) EditRequested?.Invoke(this, rule); });
        RemoveCommand = new RelayCommand(item =>
        {
            if (item is not FileOpenRule rule) return;
            try { _store.Remove(rule.Extension); Rules.Remove(rule); Message = Text.Get("FileRuleRemoved"); }
            catch (Exception exception) { Message = Text.Error(exception, "FileRuleSaveFailed"); }
            OnPropertyChanged(nameof(IsEmpty));
        });
        ReloadCommand = new RelayCommand(_ => Reload());
        Reload();
    }

    /// <summary>请求打开新增或编辑规则窗口。</summary>
    public event EventHandler<FileOpenRule?>? EditRequested;
    /// <summary>来自本机持久化的真实规则。</summary>
    public ObservableCollection<FileOpenRule> Rules { get; } = [];
    /// <summary>空态只在没有已配置规则时显示。</summary>
    public bool IsEmpty => Rules.Count == 0;
    /// <summary>加载和移除结果使用安全共享资源。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>添加规则入口。</summary>
    public RelayCommand AddCommand { get; }
    /// <summary>编辑所选行。</summary>
    public RelayCommand EditCommand { get; }
    /// <summary>移除所选后缀的本地关联。</summary>
    public RelayCommand RemoveCommand { get; }
    /// <summary>读取失败后可重试。</summary>
    public RelayCommand ReloadCommand { get; }

    /// <summary>返回编辑状态，使窗口与列表共用持久化边界。</summary>
    public FileOpenRuleEditorViewModel CreateEditor(FileOpenRule? rule) => new(_store, rule);

    /// <summary>重新读取持久化规则，失败时保留既有列表。</summary>
    public void Reload()
    {
        try
        {
            var saved = _store.Load();
            Rules.Clear();
            foreach (var rule in saved) Rules.Add(rule);
            Message = "";
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception exception) { Message = Text.Error(exception, "FileRuleLoadFailed"); }
    }
}
