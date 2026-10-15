namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>每个表页签独立维护有界分页和临时参数化筛选，不改变连接配置。</summary>
public sealed partial class PostgreSqlTabViewModel
{
    private string _appliedFilter = "";
    /// <summary>每页实际读取的常用行数，不冒充 SQL 查询分页。</summary>
    public IReadOnlyList<int> PageSizes { get; } = [50, 100, 200, 500];
    /// <summary>当前生效的每页行数。</summary>
    public int PageSize { get; private set; } = 200;
    /// <summary>待应用的 WHERE 条件，文字输入不触发读取。</summary>
    public string FilterText { get; set => SetProperty(ref field, value); } = "";
    /// <summary>已经生效的完整筛选表达式。</summary>
    public string AppliedFilter => _appliedFilter;
    /// <summary>表和视图的数据区才提供表页控制。</summary>
    public bool ShowTableControls => !IsQuery && !IsMetadataOnly && Section == "data";
    /// <summary>上一页来自真实偏移。</summary>
    public bool HasPreviousPage => !IsQuery && !IsMetadataOnly && Offset > 0;
    /// <summary>下一页来自额外探测行，不根据恰好满页猜测。</summary>
    public bool HasNextPage => !IsQuery && !IsMetadataOnly && SelectedResult?.Result.HasMore == true;
    /// <summary>首个且唯一页面不显示无意义的箭头。</summary>
    public bool ShowPageNavigation => ShowTableControls && (HasPreviousPage || HasNextPage);
    /// <summary>应用和清除复用草稿确认，不静默丢弃待保存单元格。</summary>
    public AsyncRelayCommand ApplyFilterCommand { get; private set; } = null!;
    /// <summary>清除只影响当前表页签。</summary>
    public AsyncRelayCommand ClearFilterCommand { get; private set; } = null!;
    private void InitializePagingCommands()
    {
        ApplyFilterCommand = new AsyncRelayCommand(_ => ApplyFilterAsync(false), _ => ConnectionAvailable && ShowTableControls && !IsBusy);
        ClearFilterCommand = new AsyncRelayCommand(_ => ApplyFilterAsync(true), _ => ConnectionAvailable && ShowTableControls && !IsBusy);
    }
    /// <summary>确认草稿后重置到第一页，并把新上限传给真实协议读取。</summary>
    public async Task ChangePageSizeAsync(int pageSize)
    {
        if (!ConnectionAvailable || !ShowTableControls || IsBusy || !PageSizes.Contains(pageSize) || pageSize == PageSize
            || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        PageSize = pageSize; Offset = 0; RefreshPaging(); await ReadAsync();
    }
    private async Task ApplyFilterAsync(bool clear)
    {
        if (!ConnectionAvailable || !ShowTableControls || IsBusy || CommitInputRequested?.Invoke() == false) return;
        string candidate = clear ? "" : FilterText.Trim();
        try
        {
            if (SelectedResult is { } result)
                _ = PostgreSqlTableReadPlan.Create(new(Database, Name, result.Columns, "", Schema: Schema), 0, PageSize, candidate);
        }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlFilterInvalid"); Refresh(); return; }
        if (!await ResolveDataDraftAsync()) return;
        _appliedFilter = candidate; FilterText = candidate; Offset = 0; RefreshPaging(); await ReadAsync();
    }
    private void RefreshPaging()
    {
        foreach (string property in new[] { nameof(PageSize), nameof(AppliedFilter), nameof(Offset), nameof(ShowTableControls), nameof(HasPreviousPage), nameof(HasNextPage), nameof(ShowPageNavigation) }) OnPropertyChanged(property);
        ApplyFilterCommand?.NotifyCanExecuteChanged(); ClearFilterCommand?.NotifyCanExecuteChanged();
    }
}
