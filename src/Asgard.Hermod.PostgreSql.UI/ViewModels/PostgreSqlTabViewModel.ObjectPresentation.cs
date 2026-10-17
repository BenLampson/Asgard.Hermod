namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>对象页使用独立展示状态；切换数据、结构与 DDL 不执行 SQL。</summary>
public sealed partial class PostgreSqlTabViewModel
{
    private int _loadedPageOffset;
    private int _loadedPageSize = 200;
    /// <summary>行号基于上次成功读取的偏移，失败请求不会为旧快照改号。</summary>
    public int LoadedRowOffset => _loadedPageOffset;
    /// <summary>服务器实际对象类型，不从名称或翻译文字猜测。</summary>
    public string ObjectTypeName => Text.Get(Kind switch
    {
        PostgreSqlObjectKind.Table => "DatabaseObjectBaseTable",
        PostgreSqlObjectKind.View => "DatabaseObjectView",
        PostgreSqlObjectKind.Procedure => "DatabaseObjectProcedure",
        PostgreSqlObjectKind.Function => "DatabaseObjectFunction",
        _ => "DatabaseObjectDefinition",
    });
    /// <summary>有数据的对象使用三种入口，纯元数据对象不伪造数据页。</summary>
    public IReadOnlyList<InstanceFieldChoice> ObjectSections => IsMetadataOnly
        ? StructureSections.Any(choice => choice.Id != "definition") ? [new("structure", Text.Get("DatabaseObjectStructure")), new("definition", Text.Get("DatabaseObjectDdl"))] : [new("definition", Text.Get("DatabaseObjectDdl"))]
        : [new("data", Text.Get("DatabaseObjectData")), new("structure", Text.Get("DatabaseObjectStructure")), new("definition", Text.Get("DatabaseObjectDdl"))];
    /// <summary>DDL 已在顶层导航中提供，结构下方只保留真实结构分区。</summary>
    public IReadOnlyList<InstanceFieldChoice> MetadataSections => StructureSections.Where(choice => choice.Id != "definition").ToArray();
    /// <summary>兼容现有 data/structure 内部状态，DDL 导航只切换已返回的定义。</summary>
    public string ObjectSection
    {
        get => (IsMetadataOnly || Section == "structure") && IsDefinitionSelected ? "definition" : IsMetadataOnly ? "structure" : Section;
        set
        {
            if (!ObjectSections.Any(choice => choice.Id == value)) return;
            if (value == "definition") { StructureSection = "definition"; Section = "structure"; }
            else { if (value == "structure" && IsDefinitionSelected) StructureSection = MetadataSections.FirstOrDefault()?.Id ?? "definition"; Section = value; }
            RefreshObjectPresentation();
        }
    }
    /// <summary>仅表或视图的数据分区提供筛选、分页和当前页导出。</summary>
    public bool IsDataObjectSection => !IsMetadataOnly && Section == "data";
    /// <summary>结构和 DDL 的工具栏使用明确的刷新说明。</summary>
    public string ObjectRefreshLabel => Text.Get(IsDataObjectSection ? "DatabaseObjectRefresh" : "DatabaseObjectRefreshStructure");
    /// <summary>复制对象原名不会修改查询上下文。</summary>
    public string ObjectCopyNameLabel => Text.Get(Kind == PostgreSqlObjectKind.Table ? "DatabaseObjectCopyTableName" : "DatabaseObjectCopyName");
    /// <summary>导出只使用保留的本地结果，断线后仍可以保存已经加载的证据。</summary>
    public bool CanExportObject => !IsBusy && SelectedResult is not null;
    /// <summary>完整定义阅读不执行 SQL，也不主动读取服务器。</summary>
    public RelayCommand ShowDefinitionCommand => field ??= new(_ => ObjectSection = "definition", _ => !IsBusy && Definition.Length > 0);
    /// <summary>页码和每页行数属于上次成功结果，失败请求不能替旧数据改写范围。</summary>
    public string ObjectFooterText => !IsDataObjectSection
        ? IsDefinitionSelected ? Text.Get("DatabaseObjectDdlReadOnly") : Text.Format(IsColumnsSection ? "DatabaseObjectFieldsLoaded" : "DatabaseObjectItemsLoaded", StructureRows.Count)
        : OutcomeUnknown ? Text.Get("DatabaseObjectOutcomeUnknown") : HasDataDraft ? DraftSummary : SelectedResult is { } result
            ? result.Rows.Count == 0 ? Text.Format("DatabaseObjectEmptyPage", _loadedPageSize) : Text.Format("DatabaseObjectPageRange", _loadedPageOffset + 1, _loadedPageOffset + result.Rows.Count, _loadedPageSize) : Status;
    /// <summary>剪贴板失败只展示固定安全资源，不将系统异常原文传入界面。</summary>
    public void ReportClipboardFailure() { Error = Text.Get("DatabaseClipboardFailed"); Refresh(); }
    private void RefreshObjectPresentation()
    {
        foreach (string name in new[] { nameof(ObjectSection), nameof(IsDataObjectSection), nameof(ObjectRefreshLabel), nameof(ObjectCopyNameLabel), nameof(CanExportObject), nameof(ObjectFooterText) }) OnPropertyChanged(name);
        ShowDefinitionCommand.NotifyCanExecuteChanged();
    }
    private async Task RefreshObjectAsync()
    {
        if (IsDataObjectSection) await ReadAsync();
        else await RunAsync(async token => { var session = await SessionAsync(token); await ReadStructureCoreAsync(session, token); });
    }
    private async Task ReadStructureCoreAsync(IPostgreSqlSession session, CancellationToken token)
    {
        var structure = new List<PostgreSqlObject>();
        foreach (var kind in IsMetadataOnly ? new[] { PostgreSqlObjectKind.Parameters } : Kind == PostgreSqlObjectKind.View ? [PostgreSqlObjectKind.Columns] : new[] { PostgreSqlObjectKind.Columns, PostgreSqlObjectKind.Indexes, PostgreSqlObjectKind.ForeignKeys, PostgreSqlObjectKind.Triggers })
        {
            try { var page = await session.ListAsync(new(kind, "", Database, Name, Schema: Schema), 0, token); structure.AddRange(page.Items); }
            catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); }
        }
        Structure = structure; OnPropertyChanged(nameof(Structure)); RefreshStructure();
        try { Definition = await session.ReadDefinitionAsync(new(Kind, Name, Database, Schema: Schema), token); OnPropertyChanged(nameof(Definition)); }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); }
    }
}
