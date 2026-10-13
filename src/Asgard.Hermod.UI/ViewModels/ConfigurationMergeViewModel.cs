namespace Asgard.Hermod.UI.ViewModels;

/// <summary>仅呈现有分歧的字段，关闭窗口保留双方数据。</summary>
public sealed class ConfigurationMergeViewModel : ObservableObject, IDisposable
{
    /// <summary>每个冲突都明确选择后才允许继续。</summary>
    public ConfigurationMergeViewModel(IReadOnlyList<ConfigurationConflict> conflicts)
    {
        Items = conflicts.Select(item => new ConfigurationConflictViewModel(item)).ToArray();
        ConfirmCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, true), _ => Items.Count > 0 && Items.All(item => item.SelectedIndex is 0 or 1));
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
        foreach (var item in Items) item.PropertyChanged += OnChoiceChanged;
    }
    /// <summary>没有明文凭据的冲突项。</summary>
    public IReadOnlyList<ConfigurationConflictViewModel> Items { get; }
    /// <summary>返回按稳定键记录的选择。</summary>
    public IReadOnlyDictionary<string, bool> Choices => Items.ToDictionary(item => item.Key, item => item.SelectedIndex == 1);
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }
    public event EventHandler<bool>? CloseRequested;
    /// <summary>每次选择后更新确认按钮。</summary>
    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs args) => ConfirmCommand.NotifyCanExecuteChanged();
    /// <inheritdoc />
    public void Dispose() { foreach (var item in Items) item.PropertyChanged -= OnChoiceChanged; }
}
