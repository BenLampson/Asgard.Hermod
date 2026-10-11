namespace Asgard.Hermod.UI.ViewModels;

/// <summary>集中管理受校验的步骤导航、脱敏确认信息和绑定通知。</summary>
public sealed partial class NewInstanceViewModel
{
    /// <summary>只有明确选择类型和服务商才可进入信息填写。</summary>
    public bool HasProviderSelection => SelectedType is not null && SelectedProvider is not null
        && SelectedType.Id == SelectedProvider.Type.Id && Providers.Contains(SelectedProvider);
    /// <summary>明确选择类型后才展示下一级服务商。</summary>
    public bool HasTypeSelection => SelectedType is not null;
    /// <summary>连接所需信息是否完整。</summary>
    public bool IsInformationComplete => HasProviderSelection && string.IsNullOrEmpty(ValidateName())
        && Fields.All(item => item.IsValid);
    /// <summary>成功只绑定当前配置版本，旧异步结果不能解锁新凭据。</summary>
    public bool HasSuccessfulConnectionTest => TestState == InstanceConnectionTestState.Succeeded
        && _verifiedConfigurationRevision == _configurationRevision && IsInformationComplete;
    /// <summary>填写页面允许触发校验，其余步骤必须先满足业务条件。</summary>
    public bool CanGoNext => !_disposed && !IsTesting && !IsSaving && (CurrentStep switch
    {
        1 => HasProviderSelection,
        2 => true,
        3 or 4 => HasSuccessfulConnectionTest,
        _ => false,
    });
    /// <summary>测试和保存期间不能切换步骤。</summary>
    public bool CanGoPrevious => !_disposed && !IsTesting && !IsSaving && CurrentStep > 1;
    /// <summary>测试只能从信息已完整的测试步骤开始。</summary>
    public bool CanTest => !_disposed && !IsTesting && !IsSaving && CurrentStep == 3 && IsInformationComplete;
    /// <summary>提交必须处于确认页，并且当前配置已经验证成功。</summary>
    public bool CanCreate => !_disposed && !IsTesting && !IsSaving && CurrentStep == 5
        && HasSuccessfulConnectionTest && SaveLocation == InstanceSaveLocation.Local;
    /// <summary>创建期间冻结表单信息。</summary>
    public bool IsInformationEditable => !_disposed && !IsSaving;
    /// <summary>页脚的当前步骤提示。</summary>
    public string StepCountText => $"第 {CurrentStep} / 5 步";
    /// <summary>页脚只有一个默认按钮，在末步切换为真实保存命令。</summary>
    public ICommand ContinueCommand => CurrentStep == 5 ? SubmitCommand : NextCommand;
    /// <summary>创建期间展示设计稿指定的提交状态。</summary>
    public string ContinueLabel => CurrentStep != 5 ? "下一步"
        : IsEditing ? IsSaving ? "保存中…" : "保存修改" : IsSaving ? "创建中…" : "创建实例";
    /// <summary>第一步不展示返回按钮。</summary>
    public bool HasPreviousStep => CurrentStep > 1;
    /// <summary>类型选择内容是否可见。</summary>
    public bool IsTypeStep => CurrentStep == 1;
    /// <summary>连接信息内容是否可见。</summary>
    public bool IsInformationStep => CurrentStep == 2;
    /// <summary>连接测试内容是否可见。</summary>
    public bool IsTestStep => CurrentStep == 3;
    /// <summary>保存范围内容是否可见。</summary>
    public bool IsSaveLocationStep => CurrentStep == 4;
    /// <summary>脱敏确认内容是否可见。</summary>
    public bool IsConfirmationStep => CurrentStep == 5;
    /// <summary>当前服务商的展示名称。</summary>
    public string ProviderName => SelectedProvider?.Name ?? "选择服务商";
    /// <summary>确认页展示类型和服务商。</summary>
    public string ProviderSummary => $"{SelectedType?.Name ?? "选择类型"} / {ProviderName}";
    /// <summary>真实测试成功后才展示通过。</summary>
    public string TestSummary => HasSuccessfulConnectionTest ? "通过" : "尚未通过";
    /// <summary>测试状态标题对应设计稿的四种情况。</summary>
    public string TestHeading => TestState switch
    {
        InstanceConnectionTestState.Running => "测试连接 · 测试中",
        InstanceConnectionTestState.Succeeded => "连接成功",
        InstanceConnectionTestState.Failed => "测试连接 · 验证失败",
        _ => "测试连接 · 尚未测试",
    };
    /// <summary>首次测试和重新测试复用同一命令。</summary>
    public string TestActionLabel => TestState == InstanceConnectionTestState.NotStarted ? "开始测试" : "重新测试";
    /// <summary>失败时显示凭据与 RAM 权限的修正指引。</summary>
    public bool HasTestFailure => TestState == InstanceConnectionTestState.Failed;
    /// <summary>决定是否展示服务商安全错误码。</summary>
    public bool HasTestErrorCode => !string.IsNullOrEmpty(TestErrorCode);
    /// <summary>步骤标题沿用 Pencil 中文原文。</summary>
    public string StepTitle => CurrentStep switch
    {
        1 => "选择类型与服务商",
        2 => "连接信息",
        3 => "连接测试",
        4 => "保存到哪里？",
        _ => IsEditing ? "确认修改" : "确认创建",
    };
    /// <summary>步骤说明沿用 Pencil 中文原文。</summary>
    public string StepDescription => CurrentStep switch
    {
        1 => IsEditing ? "当前实例的类型与服务商已固定。" : "先选择实例类型，再选择对应的服务商。",
        2 => $"填写实例名称与 {ProviderName} 的连接信息。",
        3 => "验证连接，不修改远程资源。",
        4 => "选择这个实例的保存位置。",
        _ => IsEditing ? "检查信息，确认后保存修改。" : "检查信息，确认后创建实例。",
    };

    /// <summary>推进之前显示字段错误，并在测试步骤阻止绕过验证。</summary>
    private void GoNext()
    {
        if (!CanGoNext)
            return;
        if (CurrentStep == 2)
        {
            _showValidation = true;
            foreach (var field in Fields)
                field.ShowValidation();
            RefreshState();
            if (!IsInformationComplete)
                return;
        }
        SetCurrentStep(CurrentStep + 1);
    }

    /// <summary>名称规则与配置服务一致，最终保存之前就能定位字段错误。</summary>
    private string ValidateName() => InstanceFieldValidation.GetError(
        new InstanceConfigurationField("name", "实例名称", MaxLength: 128), Name);

    /// <summary>内部步骤切换统一更新侧栏和正文绑定。</summary>
    private void SetCurrentStep(int step)
    {
        CurrentStep = Math.Clamp(step, 1, 5);
        OnPropertyChanged(nameof(CurrentStep));
        RefreshState();
    }

    /// <summary>任何连接信息变化都会取消旧测试并作废成功结果。</summary>
    private void InvalidateConnection()
    {
        _configurationRevision++;
        _verifiedConfigurationRevision = -1;
        _testCancellation?.Cancel();
        TestState = InstanceConnectionTestState.NotStarted;
        TestMessage = "连接信息已修改，请重新测试。";
        TestErrorCode = "";
        TestChecks = [];
        SaveError = "";
        RefreshState();
    }

    /// <summary>统一刷新所有计算属性及命令启用条件，避免步骤状态与按钮不同步。</summary>
    private void RefreshState()
    {
        foreach (var step in Steps)
            step.Update(CurrentStep);
        foreach (string property in new[] { nameof(NameError), nameof(HasTypeSelection),
            nameof(HasProviderSelection), nameof(IsInformationComplete), nameof(HasSuccessfulConnectionTest),
            nameof(CanGoNext), nameof(CanGoPrevious), nameof(CanTest), nameof(CanCreate), nameof(IsInformationEditable),
            nameof(IsTypeSelectionEditable), nameof(WindowTitle), nameof(WizardHeading), nameof(ConfirmationHint),
            nameof(StepCountText), nameof(ContinueCommand), nameof(ContinueLabel), nameof(HasPreviousStep),
            nameof(IsTypeStep), nameof(IsInformationStep), nameof(IsTestStep), nameof(IsSaveLocationStep),
            nameof(IsConfirmationStep), nameof(ProviderName), nameof(ProviderSummary),
            nameof(TestSummary), nameof(TestHeading), nameof(TestActionLabel),
            nameof(HasTestFailure), nameof(HasTestErrorCode), nameof(StepTitle), nameof(StepDescription),
            nameof(IsLocalSave), nameof(TestState), nameof(TestMessage), nameof(TestErrorCode), nameof(TestChecks),
            nameof(IsTesting), nameof(IsSaving), nameof(SaveError), nameof(CreatedInstance) })
            OnPropertyChanged(property);
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ModifyInformationCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        CancelTestCommand.NotifyCanExecuteChanged();
        SubmitCommand.NotifyCanExecuteChanged();
    }
}
