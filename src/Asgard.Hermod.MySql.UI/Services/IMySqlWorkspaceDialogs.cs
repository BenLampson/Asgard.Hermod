using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Services;

/// <summary>业务确认和文件选择分离，状态机可独立测试。</summary>
public interface IMySqlWorkspaceDialogs
{
    Task<MySqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false);
    Task<bool> ConfirmStopAsync();
    Task<bool> ConfirmEndSessionAsync();
    Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token);
    Task<string?> OpenQueryAsync(CancellationToken token);
    Task<string?> ExportAsync(MySqlGridViewModel grid, CancellationToken token);
    Task ShowConflictAsync(MySqlConflict conflict);
    Task EditFieldAsync(MySqlFieldEditorViewModel field);
}

public enum MySqlDraftChoice { Cancel, Discard, Save }
