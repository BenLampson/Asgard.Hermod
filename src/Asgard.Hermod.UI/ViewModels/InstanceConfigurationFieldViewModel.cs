namespace Asgard.Hermod.UI.ViewModels;

/// <summary>按连接器声明的字段处理编辑、共用校验和脱敏确认，不持有具体服务商逻辑。</summary>
public sealed class InstanceConfigurationFieldViewModel : ObservableObject
{
    private bool _showValidation;

    /// <summary>每个连接器选择都新建字段状态，不在不同类型之间复用填写值。</summary>
    public InstanceConfigurationFieldViewModel(InstanceConfigurationField definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        Value = definition.DefaultValue;
    }

    /// <summary>原始声明用于共享表单和保存校验。</summary>
    public InstanceConfigurationField Definition { get; }
    /// <summary>写入连接参数的稳定字段标识。</summary>
    public string Key => Definition.Key;
    /// <summary>表单字段名称。</summary>
    public string Label => Definition.Label;
    /// <summary>字段说明只来自连接器声明。</summary>
    public string Hint => Definition.Hint;
    /// <summary>必填标记由连接器声明。</summary>
    public bool IsRequired => Definition.IsRequired;
    /// <summary>输入控件的最大长度；零代表未限制。</summary>
    public int MaxLength => Math.Max(0, Definition.MaxLength);
    /// <summary>普通文字或地址字段。</summary>
    public bool IsText => Definition.Kind == InstanceFieldKind.Text;
    /// <summary>选项控件保存稳定标识。</summary>
    public bool IsChoice => Definition.Kind == InstanceFieldKind.Choice;
    /// <summary>文件控件支持旧路径和连接器导入后的加密保存内容。</summary>
    public bool IsFile => Definition.Kind == InstanceFieldKind.File;
    /// <summary>已导入的敏感文件只显示状态，不显示编码或私钥文本。</summary>
    public bool IsImportedFile => IsFile && IsSensitive && InstanceFileContent.IsImported(Value);
    /// <summary>兼容旧文件路径；重新选择文件后替换已导入内容。</summary>
    public string FileValue { get => IsImportedFile ? Text.Get("SshKeyImported") : Value; set { if (!IsImportedFile) Value = value; } }
    /// <summary>翻译后的选项名称不参与业务判断。</summary>
    public IReadOnlyList<InstanceFieldChoice> Choices => Definition.Choices;
    /// <summary>条件字段只在所选认证方式下参与校验。</summary>
    public bool IsVisible { get; private set; } = true;
    /// <summary>密钥必须通过遮蔽编辑器填写。</summary>
    public bool IsSecret => Definition.Kind == InstanceFieldKind.Secret;
    /// <summary>密钥即使未重复标记敏感，也不能出现在确认明文或错误消息中。</summary>
    public bool IsSensitive => Definition.IsSensitive || IsSecret;
    /// <summary>填写值只交给连接测试与配置服务，不进入导航快照。</summary>
    public string Value
    {
        get;
        set
        {
            if (!SetProperty(ref field, value ?? ""))
                return;
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(ConfirmationValue));
            OnPropertyChanged(nameof(IsImportedFile));
            OnPropertyChanged(nameof(FileValue));
        }
    } = "";
    /// <summary>与持久化服务共用相同字段规则。</summary>
    public bool IsValid => !IsVisible || string.IsNullOrEmpty(InstanceFieldValidation.GetError(Definition, Value));
    /// <summary>用户尝试继续后才显示就地错误，不拼接填写内容。</summary>
    public string Error => _showValidation && IsVisible ? InstanceFieldValidation.GetError(Definition, Value) : "";
    /// <summary>确认页显示普通字段原文、敏感文字的首尾或密钥填写状态。</summary>
    public string ConfirmationValue
    {
        get
        {
            string value = Definition.PreserveWhitespace ? Value : Value.Trim();
            if (value.Length == 0)
                return Text.Get("NotEntered");
            if (IsSecret || IsImportedFile)
                return Text.Get("EnteredHidden");
            if (IsChoice && !IsSensitive)
                return Choices.FirstOrDefault(choice => choice.Id == value)?.Name ?? Text.Get("NotEntered");
            if (!IsSensitive)
                return value;
            return value.Length > 8 ? $"{value[..4]}••••••••{value[^4..]}" : "••••••••";
        }
    }

    /// <summary>验证错误在对应字段下显示，不需要业务向导理解字段格式。</summary>
    public void ShowValidation()
    {
        _showValidation = true;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>依赖变化不删除输入；隐藏字段不会进入连接快照。</summary>
    internal void UpdateVisibility(IReadOnlyDictionary<string, string> parameters)
    {
        bool visible = InstanceFieldValidation.IsApplicable(Definition, parameters);
        if (IsVisible == visible) return;
        IsVisible = visible;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>阻止诊断或默认文本模板意外输出填写值。</summary>
    public override string ToString() => $"Instance field ({Key}, value hidden)";
}
