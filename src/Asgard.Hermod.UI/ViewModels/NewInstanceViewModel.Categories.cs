namespace Asgard.Hermod.UI.ViewModels;

public sealed partial class NewInstanceViewModel
{
    /// <summary>分类仅用于展示；产品和已保存配置仍使用原有类型标识。</summary>
    public IReadOnlyList<InstanceTypeDescriptor> Categories
    {
        get
        {
            var result = new List<InstanceTypeDescriptor>();
            if (Types.Any(type => IsDatabase(type.Id))) result.Add(new("database", Text.Get("MainDatabase"), Text.Get("MainDatabaseProducts"), "database"));
            result.AddRange(Types.Where(type => type.Id == "ssh")); result.AddRange(Types.Where(type => type.Id == "storage"));
            if (Types.Any(type => type.Id == "rabbitmq")) result.Add(new("mq", Text.Get("MainMessageQueue"), Text.Get("MainMessageQueueProducts"), "layers"));
            result.AddRange(Types.Where(type => !IsDatabase(type.Id) && type.Id is not ("ssh" or "storage" or "rabbitmq")));
            return result;
        }
    }
    /// <summary>新建流程增加分类选择；编辑不允许改变原有产品。</summary>
    public InstanceTypeDescriptor? SelectedCategory
    {
        get; set
        {
            if (IsEditing || !SetProperty(ref field, value)) return;
            SelectedType = value is null ? null : HasProductSelection ? Products.FirstOrDefault() : Types.FirstOrDefault(type => type.Id == value.Id);
            if (HasProductSelection) CurrentStep = 0;
            RebuildSteps(); OnPropertyChanged(nameof(CurrentStep)); RefreshState();
        }
    }
    /// <summary>产品选择不改变底层服务商选择协议。</summary>
    public InstanceTypeDescriptor? SelectedProduct
    {
        get => SelectedType;
        set
        {
            if (IsEditing || value is null || !Products.Contains(value) || value == SelectedType) return;
            SelectedType = value; CurrentStep = 0; OnPropertyChanged(nameof(CurrentStep)); RefreshState();
        }
    }
    public IReadOnlyList<InstanceTypeDescriptor> Products => Types.Where(type => SelectedCategory?.Id == "database" ? IsDatabase(type.Id) : SelectedCategory?.Id == "mq" && type.Id == "rabbitmq").ToArray();
    private bool HasProductSelection => !IsEditing && SelectedCategory?.Id is "database" or "mq";
    public bool IsProductStep => HasProductSelection && CurrentStep == 0;
    public string CategoryHeading => SelectedCategory?.Name ?? SelectedType?.Name ?? "";
    public string ProductTitle => Text.Get(SelectedCategory?.Id == "mq" ? "RabbitMqChooseProduct" : "DatabaseChooseProduct");
    public string ProductDescription => Text.Get(SelectedCategory?.Id == "mq" ? "RabbitMqChooseProductHint" : "DatabaseChooseProductHint");
    private static bool IsDatabase(string id) => id is "mysql" or "postgresql" or "redis";
}
