namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>结构按真实对象类型分区；DDL 独占正文空间，不与字段表格相互挤压。</summary>
public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>分区使用稳定标识，名称来自公共资源。</summary>
    public IReadOnlyList<InstanceFieldChoice> StructureSections { get; }
    /// <summary>每个对象页签独立保留当前结构分区。</summary>
    public string StructureSection
    {
        get;
        set
        {
            if (!StructureSections.Any(section => section.Id == value) || !SetProperty(ref field, value)) return;
            RefreshStructure();
        }
    } = "columns";
    /// <summary>只显示当前分区的元数据，避免列、索引和外键混在同一表中。</summary>
    public IReadOnlyList<PostgreSqlObject> StructureRows => Structure.Where(item => item.Kind == (StructureSection switch
    {
        "indexes" => PostgreSqlObjectKind.Index,
        "foreign-keys" => PostgreSqlObjectKind.ForeignKey,
        "triggers" => PostgreSqlObjectKind.Trigger,
        "parameters" => PostgreSqlObjectKind.Parameter,
        _ => PostgreSqlObjectKind.Column,
    })).ToArray();
    /// <summary>DDL 原文单独展示，不改变执行或数据编辑状态。</summary>
    public bool IsDefinitionSelected => StructureSection == "definition";
    /// <summary>只有读取完成且没有错误时，才能声明当前分区为空。</summary>
    public bool IsEmptyStructure => !IsDefinitionSelected && !IsBusy && !HasError && StructureRows.Count == 0;
    /// <summary>对象类型决定可用分区，视图不伪造索引或外键。</summary>
    private static IReadOnlyList<InstanceFieldChoice> CreateStructureSections(PostgreSqlObjectKind kind)
    {
        InstanceFieldChoice definition = new("definition", Text.Get("PostgreSqlObjectDefinition"));
        return kind switch
        {
            PostgreSqlObjectKind.Procedure or PostgreSqlObjectKind.Function => [new("parameters", Text.Get("PostgreSqlParameters")), definition],
            PostgreSqlObjectKind.Event or PostgreSqlObjectKind.Trigger => [definition],
            PostgreSqlObjectKind.View => [new("columns", Text.Get("PostgreSqlColumns")), definition],
            _ => [new("columns", Text.Get("PostgreSqlColumns")), new("indexes", Text.Get("PostgreSqlIndexes")),
                new("foreign-keys", Text.Get("PostgreSqlForeignKeys")), new("triggers", Text.Get("PostgreSqlTriggers")), definition],
        };
    }
    /// <summary>刷新只通知结构展示，不重建数据结果、会话或草稿。</summary>
    private void RefreshStructure()
    {
        foreach (string property in new[] { nameof(StructureRows), nameof(IsDefinitionSelected), nameof(IsEmptyStructure) }) OnPropertyChanged(property);
    }
}
