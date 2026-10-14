namespace Asgard.Hermod.UI.ViewModels;

/// <summary>现有实例复用同一受验证的表单与流程，保持原实例身份和类型。</summary>
public sealed partial class NewInstanceViewModel
{
    /// <summary>预填当前配置；保存前仍必须测试，更新时不会创建另一个实例。</summary>
    public NewInstanceViewModel(IInstanceProviderRegistry registry, IInstanceConfigurationService configurations,
        InstanceSummary existing, InstanceConnectionOptions initialConnection, IAccountService? accounts = null,
        IConfigurationSyncService? sync = null, IReadOnlyList<ManagedTag>? tags = null,
        IReadOnlyList<string>? selectedTagIds = null) : this(registry, configurations, accounts, sync, tags)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(initialConnection);
        if (string.IsNullOrWhiteSpace(existing.Id))
            throw new ArgumentException(Text.Get("TheInstanceToEditHasNoIdentifier"), nameof(existing));
        if (existing.HasConfiguration && (existing.TypeId != initialConnection.TypeId
            || existing.ProviderId != initialConnection.ProviderId))
            throw new ArgumentException(Text.Get("TheInstanceDoesNotMatchTheConnectionConfiguration"), nameof(initialConnection));
        SelectedType = Types.SingleOrDefault(type => type.Id == initialConnection.TypeId)
            ?? throw new ArgumentException(Text.Get("TheCurrentInstanceTypeIsUnavailable"), nameof(initialConnection));
        SelectedProvider = Providers.SingleOrDefault(provider => provider.Id == initialConnection.ProviderId)
            ?? throw new ArgumentException(Text.Get("TheCurrentInstanceProviderIsUnavailable"), nameof(initialConnection));
        Name = existing.Name;
        foreach (var field in Fields)
            field.Value = initialConnection.Parameters.TryGetValue(field.Key, out string? value) ? value : field.Definition.DefaultValue;
        _editingInstance = existing;
        TagSelection.Restore(selectedTagIds ?? []);
        RebuildSteps();
        RefreshState();
    }

    /// <summary>编辑状态由宿主传入的已保存实例确定。</summary>
    public bool IsEditing => _editingInstance is not null;
    /// <summary>共享标题栏区分添加与编辑操作。</summary>
    public string WindowTitle => IsEditing ? Text.Get("EditInstance") : Text.Get("AddInstance2");
    /// <summary>向导侧栏标题与实际提交操作一致。</summary>
    public string WizardHeading => IsEditing ? Text.Get("EditInstance") : Text.Get("AddInstance");
    /// <summary>编辑现有实例只修改连接参数，类型与服务商不可变。</summary>
    public bool IsTypeSelectionEditable => IsInformationEditable && !IsEditing;
    /// <summary>只有声明可切换连接路径的类型能在编辑时改服务商。</summary>
    public bool IsProviderSelectionEditable => IsInformationEditable && (!IsEditing || SelectedType?.AllowProviderChangeOnEdit == true);
    /// <summary>类型决定编辑时的保存范围提示页是否需要显示。</summary>
    private bool OmitEditSaveLocation => IsEditing && SelectedType?.OmitSaveLocationOnEdit == true;
    /// <summary>末步编号来自当前流程，不要求公共向导固定五步。</summary>
    private int ConfirmationStep => CombinesProviderAndInformation ? 5 : OmitEditSaveLocation ? 4 : 5;
    /// <summary>确认页说明提交后的结果。</summary>
    public string ConfirmationHint => IsEditing ? Text.Get(OmitEditSaveLocation ? "ConfigurationAppliesToNextConnection"
        : "TheUpdatedConnectionInformationWillBeUsedAfterSaving") : Text.Get("TheConnectionWillOpenInTheInstanceListAfterCreation");
}
