using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>业务确认和文件选择分离，状态机可独立测试。</summary>
public interface IMySqlWorkspaceDialogs : IDatabaseSessionDialogs
{
    Task<MySqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false);
    Task<bool> ConfirmStopAsync();
    Task<bool> ConfirmEndSessionAsync();
    /// <summary>EXPLAIN ANALYZE 会实际运行查询，默认拒绝，必须由交互实现显式确认。</summary>
    Task<bool> ConfirmAnalyzeAsync() => Task.FromResult(false);
    /// <summary>实际分析必须展示本次完整 SQL、实例、Session、数据库和安全端点。</summary>
    Task<bool> ConfirmAnalyzeAsync(string sql, string database, string instance, string session, string endpoint) => ConfirmAnalyzeAsync();
    /// <summary>展示模型建议的完整 SQL 和目标；未实现交互时默认拒绝。</summary>
    Task<bool> ConfirmAssistantSqlAsync(string sql, string database) => Task.FromResult(false);
    /// <summary>完整展示实例、Session 与目标；兼容无窗口交互实现的默认拒绝策略。</summary>
    Task<bool> ConfirmAssistantSqlAsync(string sql, string database, string instanceName, string sessionName, string endpoint) => ConfirmAssistantSqlAsync(sql, database);
    Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token);
    Task<string?> OpenQueryAsync(CancellationToken token);
    Task<string?> ExportAsync(MySqlGridViewModel grid, CancellationToken token);
    Task ShowConflictAsync(MySqlConflict conflict);
    Task EditFieldAsync(MySqlFieldEditorViewModel field);
}
