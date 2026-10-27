using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>冲突提示必须提供可执行入口；逐项确认前不能覆盖或提交配置。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SyncConflictEntryTests(WpfTestFixture fixture)
{
    /// <summary>账户按钮与设置直接路由处理入口，签出和解决后停用。</summary>
    [Fact]
    public Task AccountAndSettingsExposeConflictResolution() => fixture.RunOffscreenAsync(async () =>
    {
        var accounts = new AccountUiTestService(); await accounts.SignInAsync();
        var sync = new AutomaticSyncUiTestService(); sync.SetStatus(ConfigurationSyncStatus.Conflict);
        var editor = new AccountTestEditor();
        await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(),
            new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources), accounts: accounts, accountEditor: editor, sync: sync);
        var menu = new AccountMenu { DataContext = shell };
        await SshKubernetesTests.Layout(menu, 400, 40);
        var button = Assert.IsType<Button>(menu.FindName("AccountButton"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(1, editor.ConflictsOpened); Assert.False(button.ContextMenu.IsOpen);
        int resolves = 0;
        using var settings = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), accounts: accounts,
            openAccount: () => Task.CompletedTask, automaticSync: sync, resolveConflicts: () => { resolves++; return Task.CompletedTask; });
        settings.SelectedCategory = settings.Categories.Single(category => category.Id == "account");
        var view = new SettingsView { DataContext = settings };
        await SshKubernetesTests.Layout(view, 900, 540);
        var resolve = Assert.IsType<Button>(WindowTestVisuals.Named(view, "ResolveSettingsConflicts"));
        Assert.Equal(Visibility.Visible, resolve.Visibility); Assert.True(resolve.IsEnabled);
        Assert.Equal(Text.Get("SyncStatusConflict"), Assert.IsType<TextBlock>(WindowTestVisuals.Named(view, "SettingsSyncStatus")).Text);
        WindowTestVisuals.Invoke(resolve); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(1, resolves);
        sync.SetStatus(ConfigurationSyncStatus.Synced); await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(shell.ResolveSyncConflictsCommand.CanExecute(null));
        Assert.False(settings.ResolveConflictsCommand.CanExecute(null)); Assert.Equal(Visibility.Collapsed, resolve.Visibility);
        sync.SetStatus(ConfigurationSyncStatus.Conflict); await accounts.SignOutAsync(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(shell.ResolveSyncConflictsCommand.CanExecute(null)); Assert.False(settings.HasSyncConflict);
        Assert.Equal(0, sync.Checks);
    });

    /// <summary>生产编辑器直接展示冲突列表，取消零提交；明确选择后才合并。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DirectEntryRequiresExplicitChoicesBeforeSync(bool confirm) => fixture.RunAsync(async () =>
    {
        var accounts = new AccountUiTestService(); await accounts.SignInAsync();
        var sync = new AutomaticSyncUiTestService
        {
            Conflicts = [new("instance:one:name", "Bastion", "InstanceName", "Local name", "Cloud name")],
        };
        sync.SetStatus(ConfigurationSyncStatus.Conflict);
        var editor = new AccountEditorService(() => null, accounts, accounts, sync);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        var deadline = DateTime.UtcNow.AddSeconds(8);
        AccountWindow? account = null; ConfigurationMergeWindow? merge = null; int phase = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                Assert.True(DateTime.UtcNow < deadline, "冲突处理入口未完成交互。");
                account ??= Application.Current.Windows.OfType<AccountWindow>().SingleOrDefault();
                if (phase == 0)
                {
                    merge = Application.Current.Windows.OfType<ConfigurationMergeWindow>().SingleOrDefault();
                    if (merge is null || account is null) return;
                    account.Opacity = merge.Opacity = 0;
                    Assert.Same(account, merge.Owner);
                    var model = Assert.IsType<ConfigurationMergeViewModel>(merge.DataContext);
                    Assert.False(model.ConfirmCommand.CanExecute(null)); Assert.Equal(0, sync.Checks);
                    phase = 1;
                    if (confirm) { model.Items[0].SelectedIndex = 1; model.ConfirmCommand.Execute(null); }
                    else model.CancelCommand.Execute(null);
                }
                else if (account is not null && !Assert.IsType<AccountViewModel>(account.DataContext).IsBusy)
                {
                    Assert.Equal(confirm ? 1 : 0, sync.Checks);
                    Assert.Equal(confirm ? 0 : 1, sync.Conflicts.Count);
                    timer.Stop(); account.Close(); finished.TrySetResult();
                }
            }
            catch (Exception error) { timer.Stop(); merge?.Close(); account?.Close(); finished.TrySetException(error); }
        };
        try
        {
            timer.Start(); await editor.OpenConflictsAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally { timer.Stop(); if (merge?.IsVisible == true) merge.Close(); if (account?.IsVisible == true) account.Close(); }
    });
}
