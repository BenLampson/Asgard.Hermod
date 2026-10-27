using Asgard.Hermod.Identity;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实状态栏与修改边界，避免仅对静态显示属性做断言。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWorkspaceStateTests(WpfTestFixture fixture)
{
    [Fact]
    public Task SyncStatus_UpdatesRenderedTextIconAndColor() => fixture.RunAsync(async () =>
    {
        var accounts = new AccountUiTestService(); var sync = new AutomaticSyncUiTestService();
        await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(), new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), accounts: accounts, sync: sync);
        var window = new Asgard.Hermod.MainWindow(shell); WindowPreviewCapture.Show(window);
        try
        {
            await WpfTestFixture.DrainDispatcherAsync();
            var icon = Assert.IsType<IconView>(WindowTestVisuals.Named(window, "MainSyncGlyph"));
            var label = Assert.IsType<TextBlock>(WindowTestVisuals.Named(window, "MainSyncText"));
            Assert.Equal("laptop", icon.Kind); Assert.Equal(Text.Get("LocalWorkspace"), label.Text);
            await accounts.SignInAsync(TestContext.Current.CancellationToken);
            foreach (var state in new[] { ConfigurationSyncStatus.Synchronizing, ConfigurationSyncStatus.Conflict, ConfigurationSyncStatus.Failed, ConfigurationSyncStatus.Synced })
            {
                sync.SetStatus(state); await WpfTestFixture.DrainDispatcherAsync();
                Assert.Equal(Text.Get("SyncBadge" + state), label.Text);
                Assert.Equal(state == ConfigurationSyncStatus.Synced ? "cloud-check" : state == ConfigurationSyncStatus.Synchronizing ? "refresh-cw" : "circle-alert", icon.Kind);
                string brush = state switch { ConfigurationSyncStatus.Synced => "SuccessBrush", ConfigurationSyncStatus.Failed => "DangerBrush", ConfigurationSyncStatus.Conflict => "DangerBrush", _ => "TextMutedBrush" };
                Assert.Same(Application.Current.Resources[brush], icon.Foreground);
            }
            await accounts.SignOutAsync(TestContext.Current.CancellationToken); await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("laptop", icon.Kind); Assert.Same(Application.Current.Resources["TextMutedBrush"], icon.Foreground);
        }
        finally { await window.RequestExitAsync(sessionEnding: true); }
    });

    [Fact]
    public Task RenameAndTags_SaveTogetherWhileCancelSavesNeither() => fixture.RunAsync(async () =>
    {
        int writes = 0; string? name = null; IReadOnlyList<string>? tags = null;
        foreach (bool save in new[] { false, true })
        {
            using var model = new MetadataEditorViewModel(Text.Get("RenameInstance"), Text.Get("InstanceName"), "Original", 128,
                (value, ids, _, _) => { writes++; name = value; tags = ids; return Task.CompletedTask; },
                isTagEditing: true, tags: TagManagementTestData.Create(), selectedIds: ["team"], editNameAndTags: true);
            var window = new MetadataEditorWindow(model);
            await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                Assert.True(model.IsNameEditing); Assert.True(model.IsTagEditing);
                Assert.IsType<TextBox>(window.FindName("NameInput")).Text = "Updated together";
                var picker = model.Tags.OpenPicker()!; picker.ClearCommand.Execute(null);
                picker.Rows.Single(row => row.Tag.Id == "production").IsSelected = true; picker.ApplyCommand.Execute(null);
                if (save) await model.SaveCommand.ExecuteAsync(); else model.CancelCommand.Execute(null);
            });
            Assert.Equal(save ? 1 : 0, writes);
        }
        Assert.Equal("Updated together", name); Assert.Equal(new[] { "production" }, tags);
    });

    [Fact]
    public Task Delete_RejectsAccountChangeBeforeLocalTransaction() => fixture.RunAsync(async () =>
    {
        using var storage = new LocalStorageTestEnvironment(); using var services = storage.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var repository = services.GetRequiredService<ILocalConfigurationRepository>();
        repository.CreateInstance(new("boundary-delete", "Boundary", "storage", "aliyun-oss", [1], DateTime.UtcNow, []), TestContext.Current.CancellationToken);
        var accounts = new AccountUiTestService(); await accounts.SignInAsync(TestContext.Current.CancellationToken);
        var request = new InstanceDeleteRequest(accounts.Account); await accounts.SignOutAsync(TestContext.Current.CancellationToken);
        var sync = new AutomaticSyncUiTestService();
        var configurations = new SyncedInstanceConfigurationService(services.GetRequiredService<IInstanceConfigurationService>(), sync, accounts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.DeleteAsync("boundary-delete", request, TestContext.Current.CancellationToken));
        Assert.NotNull(repository.GetInstance("boundary-delete"));
        await configurations.DeleteAsync("boundary-delete", new(null), TestContext.Current.CancellationToken);
        Assert.Null(repository.GetInstance("boundary-delete")); Assert.Equal(ConfigurationSyncStatus.Pending, sync.Status);
    });
}
