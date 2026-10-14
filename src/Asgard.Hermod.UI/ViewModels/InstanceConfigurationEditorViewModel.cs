namespace Asgard.Hermod.UI.ViewModels;

/// <summary>统一连接表单的字段投影与监听；输入只更新字段状态，不替换绑定集合或重建编辑器。</summary>
public class InstanceConfigurationEditorViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<InstanceConfigurationFieldViewModel> _fields = [];
    private readonly ObservableCollection<InstanceConfigurationFieldViewModel> _visibleFields = [];
    private readonly InstanceConfigurationFieldViewModel _absent = new(new("absent", "", IsRequired: false));
    private IReadOnlyList<InstanceConfigurationFieldViewModel> _source = [];
    private bool _attached;

    /// <summary>建立固定集合；派生类型完成自身初始化后调用 Attach，避免构造期间调用虚方法。</summary>
    public InstanceConfigurationEditorViewModel(NewInstanceViewModel wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        Wizard = wizard;
        Fields = new(_fields);
        VisibleFields = new(_visibleFields);
    }

    /// <summary>校验、连接测试和持久化始终由公共向导管理。</summary>
    public NewInstanceViewModel Wizard { get; }
    /// <summary>供表单绑定的固定集合，模块只能声明筛选条件，不能替换或重置集合。</summary>
    public ReadOnlyObservableCollection<InstanceConfigurationFieldViewModel> Fields { get; }
    /// <summary>供确认页绑定的固定集合，成员只随字段实际可见性变化。</summary>
    public ReadOnlyObservableCollection<InstanceConfigurationFieldViewModel> VisibleFields { get; }

    /// <summary>加载或重新进入视图时恢复监听，并同步离开期间更换的字段与状态。</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        Wizard.PropertyChanged += OnWizardChanged;
        ObserveFields();
    }

    /// <summary>卸载时解除所有订阅；保留集合供缓存视图重新 Attach。</summary>
    public void Dispose()
    {
        if (!_attached) return;
        _attached = false;
        Wizard.PropertyChanged -= OnWizardChanged;
        foreach (var item in _source) item.PropertyChanged -= OnFieldChanged;
    }

    /// <summary>模块只声明当前分区包含哪些字段；条件隐藏由字段自身处理，避免销毁编辑状态。</summary>
    protected virtual bool IncludesField(InstanceConfigurationFieldViewModel item) => true;
    /// <summary>字段源更换或重新挂载后，模块可以更新分区标题和具名字段绑定。</summary>
    protected virtual void OnFieldsChanged() { }
    /// <summary>模块只更新依赖此值的摘要或提示，无需重建字段列表。</summary>
    protected virtual void OnFieldValueChanged(InstanceConfigurationFieldViewModel item) { }
    /// <summary>具名布局也从同一字段源取值；缺失字段不会暴露资源键或残留旧凭据。</summary>
    protected InstanceConfigurationFieldViewModel GetField(string key) => _source.FirstOrDefault(item => item.Key == key) ?? _absent;

    /// <summary>分区变化时按对象身份增量同步；仍在列表内的字段保留对应控件和焦点。</summary>
    protected void RefreshFields()
    {
        Synchronize(_fields, _source.Where(IncludesField).ToArray());
        RefreshVisibleFields();
    }

    /// <summary>只有源集合发生变化才重新订阅，普通状态通知不会触发投影刷新。</summary>
    private void OnWizardChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NewInstanceViewModel.Fields)) ObserveFields();
    }

    /// <summary>先解除旧字段，再绑定新字段，避免切换服务商后继续监听旧凭据。</summary>
    private void ObserveFields()
    {
        foreach (var item in _source) item.PropertyChanged -= OnFieldChanged;
        _source = Wizard.Fields;
        foreach (var item in _source) item.PropertyChanged += OnFieldChanged;
        OnFieldsChanged();
        RefreshFields();
    }

    /// <summary>值通知与可见性通知分开处理；输入时不修改表单集合。</summary>
    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not InstanceConfigurationFieldViewModel item) return;
        if (args.PropertyName == nameof(InstanceConfigurationFieldViewModel.Value)) OnFieldValueChanged(item);
        if (args.PropertyName == nameof(InstanceConfigurationFieldViewModel.IsVisible)) RefreshVisibleFields();
    }

    /// <summary>确认列表随实际可见性变化，值和错误提示由现有字段绑定自行更新。</summary>
    private void RefreshVisibleFields() => Synchronize(_visibleFields, _fields.Where(item => item.IsVisible).ToArray());

    /// <summary>不发送 Reset，也不替换 ItemsSource；相同字段保留实例与顺序，只有成员变化才增删或移动。</summary>
    private static void Synchronize(ObservableCollection<InstanceConfigurationFieldViewModel> target,
        IReadOnlyList<InstanceConfigurationFieldViewModel> desired)
    {
        for (int index = target.Count - 1; index >= 0; index--)
            if (!desired.Contains(target[index])) target.RemoveAt(index);
        for (int index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            int existing = target.IndexOf(desired[index]);
            if (existing < 0) target.Insert(index, desired[index]);
            else target.Move(existing, index);
        }
    }
}
