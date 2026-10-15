using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>业务确认和文件选择分离，状态机可独立测试。</summary>
public interface IMySqlWorkspaceDialogs
{
    Task<MySqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false);
    Task<bool> ConfirmStopAsync();
    Task<bool> ConfirmEndSessionAsync();
    /// <summary>EXPLAIN ANALYZE 会实际运行查询，默认拒绝，必须由交互实现显式确认。</summary>
    Task<bool> ConfirmAnalyzeAsync() => Task.FromResult(false);
    /// <summary>展示模型建议的完整 SQL 和目标；未实现交互时默认拒绝。</summary>
    Task<bool> ConfirmAssistantSqlAsync(string sql, string database) => Task.FromResult(false);
    Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token);
    Task<string?> OpenQueryAsync(CancellationToken token);
    Task<string?> ExportAsync(MySqlGridViewModel grid, CancellationToken token);
    Task ShowConflictAsync(MySqlConflict conflict);
    Task EditFieldAsync(MySqlFieldEditorViewModel field);
}
