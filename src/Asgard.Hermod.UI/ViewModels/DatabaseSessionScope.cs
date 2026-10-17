namespace Asgard.Hermod.UI.ViewModels;

/// <summary>两种数据库引擎共用的 Session 范围规则；不负责连接或执行 SQL。</summary>
public sealed class DatabaseSessionScope : ObservableObject
{
    private readonly ObservableCollection<DatabaseIdentity> _databases = [];

    /// <summary>创建固定实例范围；唯一数据库直接作为执行目标，多个时保持未选择。</summary>
    public DatabaseSessionScope(string instanceId, string sessionName, IEnumerable<DatabaseIdentity> databases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        InstanceId = instanceId; SessionName = sessionName;
        Databases = new(_databases);
        var initial = Validate(databases);
        if (initial.Length == 0) throw Text.Exception("DatabaseSessionRequiresDatabase");
        foreach (var item in initial) _databases.Add(item);
        ExecutionTarget = initial.Length == 1 ? initial[0].Name : "";
    }

    /// <summary>固定实例身份，不由实例名称、端点或翻译文本判断。</summary>
    public string InstanceId { get; }
    /// <summary>弹窗与 AI 显示当前脚本 Session 的名称。</summary>
    public string SessionName { get; }
    /// <summary>外部不可直接移除成员，避免绕过最少一个和确认快照校验。</summary>
    public ReadOnlyObservableCollection<DatabaseIdentity> Databases { get; }
    /// <summary>当前明确的目标；多个候选尚未选择时为空。</summary>
    public string ExecutionTarget { get; private set; }
    /// <summary>范围或目标变化后，使旧 AI 建议和迟到确认失效。</summary>
    public long Revision { get; private set; }
    /// <summary>唯一执行目标固定展示，无需下拉选择。</summary>
    public bool HasSingleDatabase => _databases.Count == 1;
    /// <summary>当前是否允许移除一个数据库。</summary>
    public bool CanRemoveDatabase => _databases.Count > 1;
    /// <summary>只在当前实例的已纳入范围中判断对象归属。</summary>
    public bool Contains(string database) => _databases.Any(item => item.Name == database);

    /// <summary>添加前整批校验实例，任一无效项不会造成部分添加或改变已有目标。</summary>
    public void Add(IEnumerable<DatabaseIdentity> databases)
    {
        var additions = Validate(databases).Where(item => !Contains(item.Name)).ToArray();
        if (additions.Length == 0) return;
        foreach (var item in additions) _databases.Add(item);
        Changed();
    }

    /// <summary>多个候选仅接受用户明确选择；唯一目标只接受自身。</summary>
    public void SelectExecutionTarget(string database)
    {
        if (!Contains(database)) throw Text.Exception("DatabaseSessionTargetOutsideScope");
        if (ExecutionTarget == database) return;
        ExecutionTarget = database; Changed();
    }

    /// <summary>最后一个数据库不能进入移除流程。</summary>
    public DatabaseRemovalRequest? PrepareRemoval(string database) => CanRemoveDatabase && Contains(database)
        ? new(InstanceId, SessionName, database, ExecutionTarget,
            _databases.Where(item => item.Name != database).Select(item => item.Name).ToArray(), Revision)
        : null;

    /// <summary>确认后再次校验原范围和目标，拒绝迟到确认与外部伪造的替代目标。</summary>
    public bool Matches(DatabaseRemovalRequest request) => request.InstanceId == InstanceId
        && request.SessionName == SessionName && request.Revision == Revision && request.ExecutionTarget == ExecutionTarget
        && CanRemoveDatabase && Contains(request.Database)
        && request.RemainingDatabases.SequenceEqual(_databases.Where(item => item.Name != request.Database).Select(item => item.Name));

    /// <summary>唯一替代目标直接使用；多个替代目标必须由确认弹窗明确给出。</summary>
    public string? ReplacementFor(DatabaseRemovalRequest request, DatabaseRemovalDecision decision)
    {
        if (!Matches(request)) return null;
        if (ExecutionTarget != request.Database) return decision.ReplacementTarget is null ? ExecutionTarget : null;
        if (request.RemainingDatabases.Count == 1)
            return decision.ReplacementTarget is null || decision.ReplacementTarget == request.RemainingDatabases[0]
                ? request.RemainingDatabases[0] : null;
        return request.RemainingDatabases.Contains(decision.ReplacementTarget, StringComparer.Ordinal) ? decision.ReplacementTarget : null;
    }

    /// <summary>调用方完成显式确认和连接保护后才提交，不执行任何数据库删除。</summary>
    public bool ApplyConfirmedRemoval(DatabaseRemovalRequest request, DatabaseRemovalDecision decision)
    {
        string? target = ReplacementFor(request, decision);
        if (target is null) return false;
        _databases.Remove(_databases.Single(item => item.Name == request.Database));
        ExecutionTarget = target; Changed(); return true;
    }

    /// <summary>校验完整选择并冻结前后范围；准备阶段不改变现有成员或目标。</summary>
    public DatabaseScopeChange PrepareChange(IEnumerable<DatabaseIdentity> selected)
    {
        var validated = Validate(selected);
        if (validated.Length == 0) throw Text.Exception("DatabaseSessionRequiresDatabase");
        return new(InstanceId, SessionName, Revision, ExecutionTarget, Array.AsReadOnly(_databases.ToArray()), Array.AsReadOnly(validated));
    }

    /// <summary>确认后检查同一份原范围，防止旧浮层覆盖新范围。</summary>
    public bool Matches(DatabaseScopeChange change) => change.InstanceId == InstanceId && change.SessionName == SessionName
        && change.Revision == Revision && change.ExecutionTarget == ExecutionTarget && change.Before.SequenceEqual(_databases);

    /// <summary>完成全部确认与连接保护后整批应用；现有目标仍可用时不允许隐式切换。</summary>
    public bool ApplyConfirmedChange(DatabaseScopeChange change, string executionTarget)
    {
        if (!Matches(change)) return false;
        var selected = Validate(change.Selected);
        if (selected.Length == 0) return false;
        bool containsTarget = selected.Any(item => item.Name == ExecutionTarget);
        if (containsTarget && executionTarget != ExecutionTarget) return false;
        if (selected.Length == 1 && executionTarget != selected[0].Name) return false;
        if (selected.Length > 1 && executionTarget.Length > 0 && selected.All(item => item.Name != executionTarget)) return false;
        if (selected.Length > 1 && ExecutionTarget.Length > 0 && !containsTarget && executionTarget.Length == 0) return false;
        if (selected.Length > 1 && ExecutionTarget.Length == 0 && executionTarget.Length > 0) return false;
        foreach (var removed in _databases.Where(item => !selected.Contains(item)).ToArray()) _databases.Remove(removed);
        foreach (var added in selected.Where(item => !_databases.Contains(item))) _databases.Add(added);
        ExecutionTarget = executionTarget; Changed(); return true;
    }

    private DatabaseIdentity[] Validate(IEnumerable<DatabaseIdentity> databases)
    {
        ArgumentNullException.ThrowIfNull(databases);
        var items = databases.ToArray();
        if (items.Any(item => item.InstanceId != InstanceId)) throw Text.Exception("DatabaseSessionInstanceMismatch");
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Name))) throw Text.Exception("DatabaseSessionRequiresDatabase");
        return items.DistinctBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    private void Changed()
    {
        Revision++;
        foreach (string property in new[] { nameof(Databases), nameof(ExecutionTarget), nameof(Revision), nameof(HasSingleDatabase), nameof(CanRemoveDatabase) })
            OnPropertyChanged(property);
    }
}
