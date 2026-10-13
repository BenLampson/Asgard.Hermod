namespace Asgard.Hermod.UI.ViewModels;

/// <summary>现有实例复用同一受验证的表单与流程，保持原实例身份和类型。</summary>
public sealed partial class NewInstanceViewModel
{
    /// <summary>预填当前配置；保存前仍必须测试，更新时不会创建另一个实例。</summary>
    public NewInstanceViewModel(IInstanceProviderRegistry registry, IInstanceConfigurationService configurations,
        InstanceSummary existing, InstanceConnectionOptions initialConnection, IAccountService? accounts = null,
        IConfigurationSyncService? sync = null) : this(registry, configurations, accounts, sync)
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
            field.Value = initialConnection.Parameters.TryGetValue(field.Key, out string? value) ? value : "";
        _editingInstance = existing;
        Steps = [new(1, Text.Get("ChooseType")), new(2, Text.Get("EnterInformation")), new(3, Text.Get("TestConnection")),
            new(4, Text.Get("SaveLocation")), new(5, Text.Get("ConfirmChanges"))];
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
    /// <summary>确认页说明提交后的结果。</summary>
    public string ConfirmationHint => IsEditing ? Text.Get("TheUpdatedConnectionInformationWillBeUsedAfterSaving") : Text.Get("TheConnectionWillOpenInTheInstanceListAfterCreation");
}
