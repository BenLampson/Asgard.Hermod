using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlWorkspaceViewModel
{
    private MySqlTabViewModel? _scopeEditorSession;
    /// <summary>只属于打开时的 Session，不随活动文档切换重新绑定。</summary>
    public DatabaseScopeEditorViewModel? ScopeEditor { get; private set => SetProperty(ref field, value); }
    /// <summary>唯一入口加载完整同实例目录后打开统一范围草稿。</summary>
    public AsyncRelayCommand OpenScopeEditorCommand { get; private set; } = null!;

    private async Task OpenScopeEditorAsync()
    {
        var tab = ActiveSession;
        if (tab?.Scope is not { } scope || IsManagingDatabases || tab.IsBusy || tab.IsManagingScope) return;
        long revision = scope.Revision;
        IsManagingDatabases = true; RefreshSessionNavigation();
        try
        {
            if (!await LoadDatabaseCatalogAsync() || ActiveSession != tab || scope.Revision != revision || IsClosed) return;
            _scopeEditorSession = tab;
            ScopeEditor = new(scope, DatabaseChoices.Select(item => item.Identity), change => ApplyScopeChangeAsync(tab, scope, change));
        }
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); }
        finally { IsManagingDatabases = false; RefreshSessionNavigation(); Refresh(); }
    }

    private async Task<bool> ApplyScopeChangeAsync(MySqlTabViewModel tab, DatabaseSessionScope scope, DatabaseScopeChange change)
    {
        if (IsManagingDatabases || !IsConnected || tab.IsBusy || tab.IsManagingScope || !Current()) return false;
        if (change.Selected.Any(item => item.InstanceId != InstanceId || !scope.Databases.Contains(item) && DatabaseChoices.All(choice => choice.Identity != item))) return false;
        IsManagingDatabases = true; tab.IsManagingScope = true; RefreshSessionNavigation();
        try
        {
            var names = change.Selected.Select(item => item.Name).ToArray();
            if (names.Length == 0) return false;
            string target = names.Contains(change.ExecutionTarget, StringComparer.Ordinal) ? change.ExecutionTarget : names.Length == 1 ? names[0] : "";
            var removed = change.Before.Where(item => !change.Selected.Contains(item)).ToArray();
            foreach (var database in removed)
            {
                var request = new DatabaseRemovalRequest(change.InstanceId, change.SessionName, database.Name, change.ExecutionTarget, names, change.Revision);
                var decision = await _dialogs.ConfirmDatabaseRemovalAsync(_instance.Name, Endpoint, request);
                if (decision is null || !Current()) return false;
                if (database.Name == change.ExecutionTarget)
                {
                    if (names.Length > 1 && !names.Contains(decision.ReplacementTarget, StringComparer.Ordinal)) return false;
                    if (names.Length == 1 && decision.ReplacementTarget is not null && decision.ReplacementTarget != names[0]) return false;
                    target = names.Length == 1 ? names[0] : decision.ReplacementTarget!;
                }
                else if (decision.ReplacementTarget is not null) return false;
            }
            var objects = Tabs.Where(item => item.SessionOwner == tab && !names.Contains(item.Database, StringComparer.Ordinal)).ToArray();
            var plans = new List<WorkspaceClosePlan>();
            foreach (var item in objects) { var plan = await item.PrepareCloseAsync(); if (plan is null) return false; plans.Add(plan); }
            bool changeTarget = target != change.ExecutionTarget;
            if (changeTarget) { var plan = await tab.PrepareReconnectAsync(); if (plan is null) return false; plans.Add(plan); }
            if (!Current()) return false;
            foreach (var plan in plans) await plan.CommitAsync();
            if (changeTarget) { await tab.DisconnectSessionAsync(); tab.RebindConnection(_options!); }
            if (objects.Contains(ActiveTab)) ActiveTab = tab;
            foreach (var item in objects) { item.PropertyChanged -= OnTabChanged; Tabs.Remove(item); tab.Documents.Remove(item); await item.DisposeAsync(); }
            return tab.CommitScopeChange(change, target);
        }
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); return false; }
        finally { IsManagingDatabases = false; tab.IsManagingScope = false; RefreshSessionNavigation(); Refresh(); }

        bool Current() => !IsClosed && IsConnected && Sessions.Contains(tab) && ActiveSession == tab && ReferenceEquals(tab.Scope, scope) && scope.Matches(change);
    }
}
