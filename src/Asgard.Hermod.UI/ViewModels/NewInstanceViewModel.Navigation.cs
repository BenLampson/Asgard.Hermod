namespace Asgard.Hermod.UI.ViewModels;

/// <summary>集中管理受校验的步骤导航、脱敏确认信息和绑定通知。</summary>
public sealed partial class NewInstanceViewModel
{
    /// <summary>省略自动保存范围提示不影响独立连接方式选择；确认页仍展示真实位置。</summary>
    private bool SkipsSaveLocation => SelectedType?.OmitSaveLocation == true || OmitEditSaveLocation;
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
        0 => IsProductStep && SelectedType is not null,
        1 => HasProviderSelection,
        2 => true,
        3 => HasSuccessfulConnectionTest,
        4 => !SkipsSaveLocation && HasSuccessfulConnectionTest,
        _ => false,
    });
    /// <summary>测试和保存期间不能切换步骤。</summary>
    public bool CanGoPrevious => !_disposed && !IsTesting && !IsSaving && HasTypeSelection
        && (!IsEditing || CurrentStep != Steps.FirstOrDefault()?.Id);
    /// <summary>测试只能从信息已完整的测试步骤开始。</summary>
    public bool CanTest => !_disposed && !IsTesting && !IsSaving && CurrentStep == 3 && IsInformationComplete;
    /// <summary>提交必须处于确认页，并且当前配置已经验证成功。</summary>
    public bool CanCreate => !_disposed && !IsTesting && !IsSaving && IsConfirmationStep
        && HasSuccessfulConnectionTest && !TagSelection.HasOpenPicker;
    /// <summary>创建期间冻结表单信息。</summary>
    public bool IsInformationEditable => !_disposed && !IsSaving;
    /// <summary>页脚的当前步骤提示。</summary>
    public string StepCountText => HasTypeSelection ? Text.Format("WizardStepOfTotal", Steps.FirstOrDefault(step => step.Id == CurrentStep)?.Number ?? 1, Steps.Count) : "";
    /// <summary>页脚只有一个默认按钮，在末步切换为真实保存命令。</summary>
    public ICommand ContinueCommand => IsConfirmationStep ? SubmitCommand : NextCommand;
    /// <summary>创建期间展示设计稿指定的提交状态。</summary>
    public string ContinueLabel => !IsConfirmationStep ? Text.Get("Next")
        : IsEditing ? IsSaving ? Text.Get("Saving") : Text.Get("SaveChanges") : IsSaving ? Text.Get("Creating") : Text.Get("InstanceConfirmAddition");
    /// <summary>添加流程的首步可以返回类型入口，编辑模式不允许更换类型。</summary>
    public bool HasPreviousStep => HasTypeSelection && (!IsEditing || CurrentStep != Steps.FirstOrDefault()?.Id);
    /// <summary>未选择类型时只展示公共入口，不显示任何流程进度。</summary>
    public bool IsChoosingType => !HasTypeSelection;
    /// <summary>所选类型的服务商选择页是否可见。</summary>
    public bool IsProviderStep => HasTypeSelection && CurrentStep == 1;
    /// <summary>连接信息内容是否可见。</summary>
    public bool IsInformationStep => CurrentStep == 2;
    /// <summary>连接测试内容是否可见。</summary>
    public bool IsTestStep => CurrentStep == 3;
    /// <summary>保存范围内容是否可见。</summary>
    public bool IsSaveLocationStep => CurrentStep == 4 && !SkipsSaveLocation;
    /// <summary>脱敏确认内容是否可见。</summary>
    public bool IsConfirmationStep => CurrentStep == ConfirmationStep;
    /// <summary>当前服务商的展示名称。</summary>
    public string ProviderName => SelectedProvider?.Name ?? Text.Get("ChooseProvider");
    /// <summary>确认页展示类型和服务商。</summary>
    public string ProviderSummary => $"{SelectedType?.Name ?? Text.Get("ChooseType")} / {ProviderName}";
    /// <summary>真实测试成功后才展示通过。</summary>
    public string TestSummary => HasSuccessfulConnectionTest ? Text.Get("Passed") : Text.Get("NotYetPassed");
    /// <summary>测试状态标题对应设计稿的四种情况。</summary>
    public string TestHeading => TestState switch
    {
        InstanceConnectionTestState.Running => Text.Get("TestConnectionTesting"),
        InstanceConnectionTestState.Succeeded => Text.Get("ConnectionSuccessful"),
        InstanceConnectionTestState.Failed => Text.Get("TestConnectionVerificationFailed"),
        _ => Text.Get("TestConnectionNotTested"),
    };
    /// <summary>所选服务商声明测试的访问范围与临时资源操作。</summary>
    public string TestDescription => SelectedProvider?.ConnectionTestDescription
        ?? Text.Get("ChooseAProviderToSeeTheConnectionTestDetails");
    /// <summary>首次测试和重新测试复用同一命令。</summary>
    public string TestActionLabel => TestState == InstanceConnectionTestState.NotStarted ? Text.Get("StartTest") : Text.Get("TestAgain");
    /// <summary>失败时显示凭据与 RAM 权限的修正指引。</summary>
    public bool HasTestFailure => TestState == InstanceConnectionTestState.Failed;
    /// <summary>决定是否展示服务商安全错误码。</summary>
    public bool HasTestErrorCode => !string.IsNullOrEmpty(TestErrorCode);
    /// <summary>步骤标题沿用 Pencil 中文原文。</summary>
    public string StepTitle => CurrentStep switch
    {
        0 => ProductTitle,
        1 => Text.Get(SelectedType?.ProviderSelectionTitleResourceKey ?? "ChooseProvider"),
        2 => Text.Get("ConnectionInformation"),
        3 => Text.Get("ConnectionTest"),
        4 when !SkipsSaveLocation => Text.Get("SaveLocation"),
        _ => IsEditing ? Text.Get("ConfirmChanges") : Text.Get("InstanceConfirmAddition"),
    };
    /// <summary>步骤说明匹配当前操作，具体测试范围由服务商描述。</summary>
    public string StepDescription => CurrentStep switch
    {
        0 => ProductDescription,
        1 => Text.Get(SelectedType?.ProviderSelectionDescriptionResourceKey ?? "ChooseProviderForInstanceType"),
        2 => Text.Format("EnterTheInstanceNameAndConnectionInformationFor", ProviderName),
        3 => Text.Get("VerifyConnectionInformationAndAccessPermissions"),
        4 when !SkipsSaveLocation => Text.Get("SyncSaveAutomaticHint"),
        _ => Text.Get("InstanceConfirmTagsDescription"),
    };

    /// <summary>连接流程自行声明步骤；类型入口与公共布局都不预设连接步骤。</summary>
    private void RebuildSteps()
    {
        List<InstanceWizardStep> steps = [];
        if (SelectedType is not null)
        {
            if (HasProductSelection) steps.Add(new(0, Text.Get("InstanceChooseProduct"), 1));
            steps.Add(new(1, Text.Get(SelectedType.ProviderSelectionTitleResourceKey), steps.Count + 1));
            steps.Add(new(2, Text.Get("ConnectionInformation"), steps.Count + 1));
            steps.Add(new(3, Text.Get("TestConnection"), steps.Count + 1));
            if (!SkipsSaveLocation) steps.Add(new(4, Text.Get("SaveLocation"), steps.Count + 1));
            steps.Add(new(ConfirmationStep, Text.Get(IsEditing ? "ConfirmChanges" : "InstanceConfirmAddition"), steps.Count + 1));
        }
        Steps = steps;
        OnPropertyChanged(nameof(Steps));
    }

    /// <summary>首步返回公共类型入口并清除原类型配置；编辑模式始终保留已有类型。</summary>
    private void GoPrevious()
    {
        if (!CanGoPrevious)
            return;
        if (CurrentStep == Steps[0].Id)
        {
            if (SelectedCategory is not null) SelectedCategory = null;
            else SelectedType = null;
        }
        else
            SetCurrentStep(Steps.TakeWhile(step => step.Id != CurrentStep).Last().Id);
    }

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
        if (Steps.SkipWhile(step => step.Id != CurrentStep).Skip(1).FirstOrDefault() is { } next)
            SetCurrentStep(next.Id);
    }

    /// <summary>名称规则与配置服务一致，最终保存之前就能定位字段错误。</summary>
    private string ValidateName() => InstanceFieldValidation.GetError(
        new InstanceConfigurationField("name", Text.Get("InstanceName"), MaxLength: 128), Name);

    /// <summary>内部步骤切换统一更新侧栏和正文绑定。</summary>
    private void SetCurrentStep(int step)
    {
        if (!Steps.Any(item => item.Id == step)) return;
        CurrentStep = step;
        OnPropertyChanged(nameof(CurrentStep));
        RefreshState();
    }

    /// <summary>任何连接信息变化都会取消旧测试并作废成功结果。</summary>
    private void InvalidateConnection()
    {
        _verifiedConnection = null;
        _configurationRevision++;
        _verifiedConfigurationRevision = -1;
        _testCancellation?.Cancel();
        TestState = InstanceConnectionTestState.NotStarted;
        TestMessage = Text.Get("TheConnectionInformationHasChangedTestAgain");
        TestErrorCode = "";
        TestChecks = [];
        SaveError = "";
        RefreshState();
    }

    /// <summary>统一刷新所有计算属性及命令启用条件，避免步骤状态与按钮不同步。</summary>
    private void RefreshState()
    {
        TagSelection.SetEditable(IsConfirmationStep && IsInformationEditable);
        foreach (var step in Steps)
            step.Update(CurrentStep);
        foreach (string property in new[] { nameof(NameError), nameof(HasTypeSelection), nameof(IsChoosingType),
            nameof(IsProductStep), nameof(SelectedProduct), nameof(Products), nameof(CategoryHeading), nameof(ProductTitle), nameof(ProductDescription),
            nameof(HasProviderSelection), nameof(IsInformationComplete), nameof(HasSuccessfulConnectionTest),
            nameof(CanGoNext), nameof(CanGoPrevious), nameof(CanTest), nameof(CanCreate), nameof(IsInformationEditable),
            nameof(IsTypeSelectionEditable), nameof(IsProviderSelectionEditable), nameof(HasHostingProviderSelection), nameof(WindowTitle), nameof(WizardHeading), nameof(ConfirmationHint),
            nameof(StepCountText), nameof(ContinueCommand), nameof(ContinueLabel), nameof(HasPreviousStep),
            nameof(IsProviderStep), nameof(ProviderLabel), nameof(IsInformationStep), nameof(IsTestStep), nameof(IsSaveLocationStep),
            nameof(IsConfirmationStep), nameof(ProviderName), nameof(ProviderSummary),
            nameof(TestSummary), nameof(TestHeading), nameof(TestDescription), nameof(TestActionLabel),
            nameof(HasTestFailure), nameof(HasTestErrorCode), nameof(StepTitle), nameof(StepDescription),
            nameof(IsLocalSave), nameof(CanSaveToAccount), nameof(SaveLocationLabel), nameof(SaveLocationDescription),
            nameof(TestState), nameof(TestMessage), nameof(TestErrorCode), nameof(TestChecks),
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

    /// <summary>类型可以将服务商列表表达为连接方式列表。</summary>
    public string ProviderLabel => Text.Get(SelectedType?.ProviderLabelResourceKey ?? "Provider");
}
