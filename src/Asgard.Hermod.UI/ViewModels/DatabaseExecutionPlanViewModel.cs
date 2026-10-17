namespace Asgard.Hermod.UI.ViewModels;

/// <summary>同一份计划证据上的结构、原文、选择与详情状态；所有动作均为本地阅读。</summary>
public sealed class DatabaseExecutionPlanViewModel : ObservableObject
{
    private readonly IReadOnlyList<DatabasePlanNodeViewModel> _roots;
    private readonly List<DatabasePlanNodeViewModel> _all = [];
    private bool _bulkUpdate;

    /// <summary>为完整快照创建独立的阅读状态，不读取任何外部资源。</summary>
    public DatabaseExecutionPlanViewModel(DatabaseExecutionPlan snapshot)
    {
        Snapshot = snapshot;
        _roots = snapshot.Roots.Select(node => new DatabasePlanNodeViewModel(node, null, RefreshVisibleNodes)).ToArray();
        foreach (var node in _roots) Collect(node);
        HasFieldSummary = snapshot.Format == DatabasePlanFormat.Fields && snapshot.Columns.Contains("table", StringComparer.OrdinalIgnoreCase) && snapshot.Columns.Contains("type", StringComparer.OrdinalIgnoreCase);
        int fieldOffset = string.Join('\t', snapshot.Columns).Length + Environment.NewLine.Length;
        FieldRows = snapshot.Rows.Select(values =>
        {
            string row = string.Join('\t', values.Select(value => value ?? "NULL"));
            int? position = fieldOffset <= snapshot.RawText.Length && snapshot.RawText.AsSpan(fieldOffset).StartsWith(row, StringComparison.Ordinal) ? fieldOffset : null;
            var field = new DatabasePlanFieldRowViewModel(snapshot.Columns, values, position, row.Length);
            fieldOffset += row.Length + Environment.NewLine.Length; return field;
        }).ToArray();
        ViewMode = snapshot.HasStructure ? "structured" : "raw";
        Modes = [new("structured", Text.Get(snapshot.Format == DatabasePlanFormat.Fields ? "PlanFieldsView" : "PlanTreeView"), snapshot.HasStructure, snapshot.Format == DatabasePlanFormat.Fields ? "table-2" : "git-branch"), new("raw", Text.Get("PlanRawView"), true, "code")];
        ExpandAllCommand = new RelayCommand(_ => SetExpanded(true), _ => _roots.Count > 0); CollapseAllCommand = new RelayCommand(_ => SetExpanded(false), _ => _roots.Count > 0);
        ToggleDetailsCommand = new RelayCommand(_ => IsDetailsOpen = !IsDetailsOpen, _ => Snapshot.Format == DatabasePlanFormat.Tree || HasFieldSummary);
        OpenDetailsCommand = new RelayCommand(_ => IsDetailsOpen = true, _ => SelectedNode is not null || SelectedField is not null);
        LocateRawCommand = new RelayCommand(_ =>
        {
            int? start = SelectedNode?.Node.SourceOffset ?? SelectedField?.SourceOffset;
            if (start is null) return; ViewMode = "raw";
            LocateRequested?.Invoke(start.Value, SelectedNode?.Node.SourceLength ?? SelectedField?.SourceLength ?? 0);
        }, _ => SelectedNode?.Node.SourceOffset is not null || SelectedField?.SourceOffset is not null);
        RefreshVisibleNodes(); SelectedNode = _all.FirstOrDefault(); SelectedField = FieldRows.FirstOrDefault();
    }

    /// <summary>本次请求的不可变证据。</summary>
    public DatabaseExecutionPlan Snapshot { get; }
    /// <summary>完整原文，复制与导出仅使用此值。</summary>
    public string RawText => Snapshot.RawText;
    /// <summary>没有服务器文本时不能复制空证据。</summary>
    public bool HasRawText => RawText.Length > 0;
    /// <summary>稳定模式选项。</summary>
    public IReadOnlyList<DatabasePlanMode> Modes { get; }
    /// <summary>当前模式，变化不会触发查询。</summary>
    public string ViewMode
    {
        get; set
        {
            if (value is not ("structured" or "raw") || value == "structured" && !Snapshot.HasStructure || !SetProperty(ref field, value)) return;
            foreach (string property in new[] { nameof(IsRaw), nameof(IsTree), nameof(IsFields), nameof(IsFieldSummary), nameof(IsFieldGrid), nameof(IsDetailsVisible), nameof(CanShowDetails) }) OnPropertyChanged(property);
        }
    } = "raw";
    /// <summary>显示原文。</summary>
    public bool IsRaw => ViewMode == "raw";
    /// <summary>显示服务器执行树。</summary>
    public bool IsTree => ViewMode == "structured" && Snapshot.Format == DatabasePlanFormat.Tree;
    /// <summary>显示传统 EXPLAIN 字段表。</summary>
    public bool IsFields => ViewMode == "structured" && Snapshot.Format == DatabasePlanFormat.Fields;
    /// <summary>具备传统字段表最小列集时展示可读行摘要，不形成树。</summary>
    public bool HasFieldSummary { get; }
    /// <summary>显示已识别字段的摘要行。</summary>
    public bool IsFieldSummary => IsFields && HasFieldSummary;
    /// <summary>其他字段表直接保留全部列。</summary>
    public bool IsFieldGrid => IsFields && !HasFieldSummary;
    /// <summary>传统 EXPLAIN 的原始行顺序。</summary>
    public IReadOnlyList<DatabasePlanFieldRowViewModel> FieldRows { get; }
    /// <summary>当前字段行选择只影响详情阅读。</summary>
    public DatabasePlanFieldRowViewModel? SelectedField
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            NotifySelection();
        }
    }
    /// <summary>结构化模式是否可用。</summary>
    public bool HasStructure => Snapshot.HasStructure;
    /// <summary>可见的树行，按原始父子顺序排列。</summary>
    public ObservableCollection<DatabasePlanNodeViewModel> VisibleNodes { get; } = [];
    /// <summary>仅影响阅读的当前节点。</summary>
    public DatabasePlanNodeViewModel? SelectedNode
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            NotifySelection();
        }
    }
    /// <summary>详情开关不清除节点选择。</summary>
    public bool IsDetailsOpen { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(IsDetailsVisible)); } } = true;
    /// <summary>详情不占原始阅读器和字段表的空间。</summary>
    public bool IsDetailsVisible => CanShowDetails && IsDetailsOpen;
    /// <summary>原始阅读不保留无效详情按钮。</summary>
    public bool CanShowDetails => IsTree || IsFieldSummary;
    /// <summary>详情标题来自所选原始节点或字段行。</summary>
    public string SelectedTitle => SelectedNode?.Name ?? SelectedField?.Name ?? "";
    /// <summary>完整服务器算子或查询块，不翻译服务器表达式。</summary>
    public string SelectedOperator => SelectedNode?.Node.FullOperation ?? SelectedField?.QueryBlock ?? "";
    /// <summary>原文的本地搜索词。</summary>
    public string SearchText { get; set => SetProperty(ref field, value); } = "";
    /// <summary>原文的本地换行偏好。</summary>
    public bool WrapRawText { get; set => SetProperty(ref field, value); }
    /// <summary>后续请求失败时保留并明确标注上次结果。</summary>
    public bool IsStale { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(Fact)); } }
    /// <summary>执行事实与旧结果标记。</summary>
    public string Fact => !Snapshot.RequestSucceeded ? Text.Get("PlanFailedFact") : IsStale ? Text.Format("PlanPreviousFact", Text.Get(Snapshot.IsActual ? "PlanActualFact" : "PlanEstimateFact")) : Text.Get(Snapshot.IsActual ? "PlanActualFact" : "PlanEstimateFact");
    /// <summary>客户端请求耗时独立展示。</summary>
    public string RequestTime => Text.Format(Snapshot.IsActual ? "PlanActualRequestTime" : "PlanEstimatedRequestTime", Snapshot.RequestElapsed.TotalMilliseconds);
    /// <summary>结构不可用时说明回退原因，不显示虚构的零个节点。</summary>
    public string Summary
    {
        get
        {
            if (!HasStructure) return Text.Get("PlanStructureUnavailable");
            if (Snapshot.Format == DatabasePlanFormat.Fields)
            {
                int column = -1;
                for (int index = 0; index < Snapshot.Columns.Count; index++) if (string.Equals(Snapshot.Columns[index], "id", StringComparison.OrdinalIgnoreCase)) { column = index; break; }
                if (HasFieldSummary && column >= 0 && Snapshot.Rows.Count > 0 && Snapshot.Rows.All(row => row.Count > column && row[column] is not null))
                    return Text.Format("PlanQueryBlockCount", Snapshot.Rows.Select(row => row[column]).Distinct(StringComparer.Ordinal).Count());
                return Text.Format("PlanFieldRows", Snapshot.Rows.Count);
            }
            if (Snapshot.IsActual && _roots is [var root] && root.Node.Metrics.ActualRows is { } rows && root.Node.Metrics.Loops == "1")
                return Text.Format("PlanNodeOutputSummary", _all.Count, rows);
            return Text.Format("PlanNodeCount", _all.Count);
        }
    }
    /// <summary>行数列明确区分实际与估算。</summary>
    public string RowsHeading => Text.Get(Snapshot.IsActual ? "PlanActualRows" : "PlanEstimatedRows");
    /// <summary>成本与时间的列标题不同。</summary>
    public string MetricHeading => Text.Get(HasFieldSummary ? "PlanAccessType" : Snapshot.IsActual ? "PlanActualTimeHeading" : "PlanCostHeading");
    /// <summary>最后一列按真实格式显示索引、循环或估算行宽。</summary>
    public string LastHeading => Text.Get(HasFieldSummary ? "PlanUsedKey" : Snapshot.IsActual ? "PlanLoops" : "PlanWidth");
    /// <summary>步骤和字段摘要不混用相同标题。</summary>
    public string StepHeading => Text.Get(HasFieldSummary ? "PlanFieldHeading" : "PlanStepHeading");
    /// <summary>仅在服务器明确返回这一条简单链路及实际数字时显示数据流；不推导风险或独占时间。</summary>
    public string FlowSummary
    {
        get
        {
            if (!Snapshot.IsActual || _roots is not [var sort] || sort.Node.Operation != DatabasePlanOperation.Sort
                || sort.Children is not [var filter] || filter.Node.Operation != DatabasePlanOperation.Filter
                || filter.Children is not [var scan] || scan.Node.Operation != DatabasePlanOperation.TableScan || scan.Children.Count != 0
                || sort.Node.Metrics.ActualRows is not { } output || filter.Node.Metrics.ActualRows is not { } passed || scan.Node.Metrics.ActualRows is not { } scanned) return "";
            return Text.Format("PlanScanFilterSortFlow", scanned, passed, output);
        }
    }
    /// <summary>无确切数据流时不占固定说明行。</summary>
    public bool HasFlowSummary => FlowSummary.Length > 0;
    /// <summary>时间含义只适用于实际分析。</summary>
    public string MetricNotice => Text.Get(HasFieldSummary ? "PlanExplainFieldNotice" : Snapshot.IsActual ? "PlanTimeNotice" : "PlanCostNotice");
    /// <summary>底部只保留阅读说明，复制导出在阅读器工具栏提供一次。</summary>
    public string FooterHint => Text.Get(!HasStructure ? "PlanRawFooterHint" : HasFieldSummary ? "PlanFieldsFooterHint" : "PlanTreeFooterHint");
    /// <summary>详情数字保持服务器精度。</summary>
    public IReadOnlyList<DatabasePlanDetail> Details => SelectedNode is not { } node ? SelectedField?.Details ?? [] : Snapshot.IsActual
        ? [FirstDetail(node),
            new(Text.Get("PlanReturnedRows"), node.ActualRows), new(Text.Get("PlanFirstTime"), Milliseconds(node.Node.Metrics.FirstRowMilliseconds)),
            new(Text.Get("PlanFinishTime"), Milliseconds(node.Node.Metrics.CompletionMilliseconds)), new(Text.Get("PlanLoopCount"), node.Loops)]
        : [FirstDetail(node),
            new(Text.Get("PlanEstimatedRows"), node.EstimatedRows), new(Text.Get("PlanStartupCost"), DatabasePlanNodeViewModel.Number(node.Node.Metrics.StartupCost)),
            new(Text.Get("PlanTotalCost"), DatabasePlanNodeViewModel.Number(node.Node.Metrics.TotalCost))];
    /// <summary>不翻译服务器字段名和值。</summary>
    public IReadOnlyList<DatabasePlanDetail> RawFields => SelectedNode?.Node.RawFields.Select(entry => new DatabasePlanDetail(entry.Key, entry.Value)).ToArray() ?? SelectedField?.Fields ?? [];
    /// <summary>已在摘要和详情中呈现的字段不重复占行；其余服务器字段仍可展开查看。</summary>
    public IReadOnlyList<DatabasePlanDetail> AdditionalFields => RawFields.Where(entry => !RepresentedFields.Contains(entry.Name)).ToArray();
    /// <summary>只有确有附加字段时展示附加信息入口。</summary>
    public bool HasAdditionalFields => AdditionalFields.Count > 0;
    private static readonly HashSet<string> RepresentedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "select_type", "table", "type", "possible_keys", "key", "rows", "Extra",
        "Node Type", "Relation Name", "Index Name", "Plan Rows", "Plan Width", "Startup Cost", "Total Cost", "Sort Key", "Index Cond", "Filter"
    };
    /// <summary>展开全部，不更改服务器顺序。</summary>
    public RelayCommand ExpandAllCommand { get; }
    /// <summary>收起全部，仍保留快照。</summary>
    public RelayCommand CollapseAllCommand { get; }
    /// <summary>切换详情。</summary>
    public RelayCommand ToggleDetailsCommand { get; }
    /// <summary>键盘 Enter 打开选中节点的详情。</summary>
    public RelayCommand OpenDetailsCommand { get; }
    /// <summary>切到同一份原文并请求定位选中节点。</summary>
    public RelayCommand LocateRawCommand { get; }
    /// <summary>视图只在用户显式定位时选择原文，不在加载时抢焦点。</summary>
    public event Action<int, int>? LocateRequested;

    private static string Milliseconds(string? value) => value is null ? Text.Get("PlanMissingMetric") : Text.Format("PlanMilliseconds", value);
    private static DatabasePlanDetail FirstDetail(DatabasePlanNodeViewModel node)
    {
        string label = node.Node.Operation switch { DatabasePlanOperation.Sort => "PlanSortExpression", DatabasePlanOperation.Filter => "PlanFilterCondition", _ => "PlanAccessObject" };
        string value = node.Node.RawFields.TryGetValue("Relation Name", out var relation) ? relation : node.Expression;
        return new(Text.Get(label), DatabasePlanNodeViewModel.Number(value.Length == 0 ? null : value));
    }
    private void NotifySelection()
    {
        foreach (string name in new[] { nameof(Details), nameof(RawFields), nameof(AdditionalFields), nameof(HasAdditionalFields), nameof(SelectedTitle), nameof(SelectedOperator) }) OnPropertyChanged(name);
        LocateRawCommand?.NotifyCanExecuteChanged(); OpenDetailsCommand?.NotifyCanExecuteChanged();
    }
    private void Collect(DatabasePlanNodeViewModel node) { _all.Add(node); foreach (var child in node.Children) Collect(child); }
    private void SetExpanded(bool expanded)
    {
        _bulkUpdate = true;
        try { foreach (var node in _all) node.IsExpanded = expanded; }
        finally { _bulkUpdate = false; RefreshVisibleNodes(); }
    }
    private void RefreshVisibleNodes()
    {
        if (_bulkUpdate) return;
        var selected = SelectedNode;
        var next = new List<DatabasePlanNodeViewModel>(); foreach (var root in _roots) Add(root);
        // 增量变更保留未受影响的原生行容器和滚动位置。
        for (int index = 0; index < next.Count; index++)
        {
            if (index < VisibleNodes.Count && ReferenceEquals(next[index], VisibleNodes[index])) continue;
            int existing = VisibleNodes.IndexOf(next[index]);
            if (existing >= index) while (existing-- > index) VisibleNodes.RemoveAt(index);
            else VisibleNodes.Insert(index, next[index]);
        }
        while (VisibleNodes.Count > next.Count) VisibleNodes.RemoveAt(VisibleNodes.Count - 1);
        while (selected is not null && !VisibleNodes.Contains(selected)) selected = selected.Parent;
        SelectedNode = selected;
        void Add(DatabasePlanNodeViewModel node) { next.Add(node); if (node.IsExpanded) foreach (var child in node.Children) Add(child); }
    }
}
