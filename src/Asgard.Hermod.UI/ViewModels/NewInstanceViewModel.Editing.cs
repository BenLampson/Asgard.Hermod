namespace Asgard.Hermod.UI.ViewModels;

/// <summary>现有实例复用同一受验证的表单与流程，保持原实例身份和类型。</summary>
public sealed partial class NewInstanceViewModel
{
    /// <summary>预填当前配置；保存前仍必须测试，更新时不会创建另一个实例。</summary>
    public NewInstanceViewModel(IInstanceProviderRegistry registry, IInstanceConfigurationService configurations,
        InstanceSummary existing, InstanceConnectionOptions initialConnection) : this(registry, configurations)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(initialConnection);
        if (string.IsNullOrWhiteSpace(existing.Id))
            throw new ArgumentException("待编辑实例缺少标识。", nameof(existing));
        if (existing.HasConfiguration && (existing.TypeId != initialConnection.TypeId
            || existing.ProviderId != initialConnection.ProviderId))
            throw new ArgumentException("实例与连接配置不匹配。", nameof(initialConnection));
        SelectedType = Types.SingleOrDefault(type => type.Id == initialConnection.TypeId)
            ?? throw new ArgumentException("当前实例类型不可用。", nameof(initialConnection));
        SelectedProvider = Providers.SingleOrDefault(provider => provider.Id == initialConnection.ProviderId)
            ?? throw new ArgumentException("当前实例服务商不可用。", nameof(initialConnection));
        Name = existing.Name;
        foreach (var field in Fields)
            field.Value = initialConnection.Parameters.TryGetValue(field.Key, out string? value) ? value : "";
        _editingInstance = existing;
        Steps = [new(1, "选择类型"), new(2, "输入信息"), new(3, "测试连接"),
            new(4, "保存位置"), new(5, "确认修改")];
        RefreshState();
    }

    /// <summary>编辑状态由宿主传入的已保存实例确定。</summary>
    public bool IsEditing => _editingInstance is not null;
    /// <summary>共享标题栏区分添加与编辑操作。</summary>
    public string WindowTitle => IsEditing ? "编辑实例" : "添加 Instance";
    /// <summary>向导侧栏标题与实际提交操作一致。</summary>
    public string WizardHeading => IsEditing ? "编辑实例" : "添加实例";
    /// <summary>编辑现有实例只修改连接参数，类型与服务商不可变。</summary>
    public bool IsTypeSelectionEditable => IsInformationEditable && !IsEditing;
    /// <summary>确认页说明提交后的结果。</summary>
    public string ConfirmationHint => IsEditing ? "保存后将使用更新后的连接信息。" : "创建后将在实例列表中打开此连接。";
}
