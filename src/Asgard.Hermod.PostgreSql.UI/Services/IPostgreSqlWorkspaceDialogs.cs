using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.PostgreSql.UI.Services;

/// <summary>业务确认和文件选择分离，状态机可独立测试。</summary>
public interface IPostgreSqlWorkspaceDialogs : IDatabaseSessionDialogs
{
    Task<PostgreSqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false);
    Task<bool> ConfirmStopAsync();
    Task<bool> ConfirmEndSessionAsync();
    /// <summary>展示模型建议的完整 SQL 和目标；未实现交互时默认拒绝。</summary>
    Task<bool> ConfirmAssistantSqlAsync(string sql, string database) => Task.FromResult(false);
    /// <summary>完整展示实例、Session 与目标；兼容无窗口交互实现的默认拒绝策略。</summary>
    Task<bool> ConfirmAssistantSqlAsync(string sql, string database, string instanceName, string sessionName, string endpoint) => ConfirmAssistantSqlAsync(sql, database);
    Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token);
    Task<string?> OpenQueryAsync(CancellationToken token);
    Task<string?> ExportAsync(PostgreSqlGridViewModel grid, CancellationToken token);
    Task ShowConflictAsync(PostgreSqlConflict conflict);
    Task EditFieldAsync(PostgreSqlFieldEditorViewModel field);
}
