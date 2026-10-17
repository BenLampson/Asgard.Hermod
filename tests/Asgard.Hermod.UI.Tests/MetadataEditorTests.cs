using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Views.Shell;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实轻量窗口验证取消、失败保留输入和稳定页面同步。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MetadataEditorTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task RenameWindow_RetainsFailedInputAndRetries(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            int saves = 0;
            string? saved = null;
            using var model = new MetadataEditorViewModel(Text.Get("RenameInstance"), Text.Get("InstanceName"), "Original", 128,
                (name, _, _, _) =>
                {
                    saves++; if (saves == 1) throw new IOException("Connection string must not be displayed");
                    saved = name; return Task.CompletedTask;
                });
            var window = new MetadataEditorWindow(model);
            bool? result = await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                var input = Assert.IsType<TextBox>(window.FindName("NameInput"));
                Assert.True(input.IsKeyboardFocused);
                input.Text = "New name";
                await model.SaveCommand.ExecuteAsync();
                Assert.True(window.IsVisible);
                Assert.Equal("New name", input.Text);
                Assert.Equal(Text.Get("TheOperationFailedTryAgain"), model.Error);
                Assert.False(model.WasSaved);
                await WindowPreviewCapture.VerifyAsync(window, "metadata-rename-" + language + "-" + theme);
                await model.SaveCommand.ExecuteAsync();
            });
            Assert.True(result); Assert.Equal(2, saves); Assert.Equal("New name", saved);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task CancelAndUnconfirmedTags_DoNotSave() => fixture.RunAsync(async () =>
    {
        int saves = 0;
        using var model = new MetadataEditorViewModel(Text.Get("EditInstanceTags"), Text.Get("InstanceName"), "Original", 128,
            (_, _, _, _) => { saves++; return Task.CompletedTask; }, isTagEditing: true, tags: TagManagementTestData.Create(), selectedIds: ["team"]);
        var window = new MetadataEditorWindow(model);
        await ModalWindowTestInteraction.ShowAsync(window, async () =>
        {
            var draft = model.Tags.OpenPicker(); Assert.NotNull(draft); Assert.False(model.CanSave);
            await model.SaveCommand.ExecuteAsync(); Assert.Equal(0, saves);
            draft.CancelCommand.Execute(null);
            Assert.Equal(["team"], model.Tags.SelectedIds);
            Assert.True(model.CanSave);
            model.CancelCommand.Execute(null);
        });
        Assert.Equal(0, saves); Assert.False(model.WasSaved);
    });

    [Fact]
    public Task RenameAndTags_UpdateExistingPageWithoutChangingIdentity() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var item = environment.Shell.Sidebar.AllInstances.First(instance => !instance.IsLocal);
        environment.Shell.Workspace.OpenInstance(item);
        var page = environment.Shell.Workspace.SelectedTab!;
        string pageId = page.Id;
        environment.Editor.RenameHandler = id => Task.FromResult<InstanceSummary?>(item.Summary with { Name = "Renamed", DisplayName = "Renamed" });
        var title = new InstanceNameButton { Instance = item, FontSize = 15 };
        var view = new AppShellView { DataContext = environment.Shell, Content = title };
        var window = new HermodWindow { Content = view, Width = 520, Height = 260 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var rename = Assert.Single(WindowTestVisuals.Descendants<Button>(title));
            Assert.Same(environment.Shell.Sidebar.RenameInstanceCommand, rename.Command);
            Assert.Same(item, rename.CommandParameter);
            await environment.Shell.Sidebar.RenameInstanceCommand.ExecuteAsync(rename.CommandParameter);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(title), text => text.Text == "Renamed");
        }
        finally { window.Close(); }
        Assert.Same(page, environment.Shell.Workspace.SelectedTab);
        Assert.Equal(pageId, page.Id); Assert.Equal(item.Id, page.InstanceId); Assert.Equal("Renamed", page.Header);
        environment.Editor.EditTagsHandler = id => Task.FromResult<InstanceSummary?>(item.Summary with
        { Tags = ["Environment/Development"], TagIds = ["builtin.environment.development"] });
        await environment.Shell.Sidebar.EditInstanceTagsCommand.ExecuteAsync(item);
        Assert.Same(page, environment.Shell.Workspace.SelectedTab);
        Assert.Equal("Renamed", page.Header); Assert.Equal(["builtin.environment.development"], item.Summary.TagIds);
        Assert.Same(item, Assert.Single(environment.Shell.Workspace.Home.RecentInstances).Instance);
        environment.Editor.RenameHandler = _ => Task.FromResult<InstanceSummary?>(null);
        await environment.Shell.Sidebar.RenameInstanceCommand.ExecuteAsync(item);
        Assert.Equal("Renamed", page.Header);
    });

    [Fact]
    public Task InstanceRow_MenuUsesCurrentInstanceAndDisablesBuiltInTerminal() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        await using var environment = new ShellTestEnvironment();
        var view = new InstanceSidebarView { DataContext = environment.Shell.Sidebar };
        var window = new HermodWindow { Content = view, Width = 260, Height = 600 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            foreach (var instance in environment.Shell.Sidebar.AllInstances)
            {
                var row = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => button.IsVisible && ReferenceEquals(button.CommandParameter, instance));
                var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
                menu.PlacementTarget = row; menu.IsOpen = true;
                await WpfTestFixture.DrainDispatcherAsync();
                var rename = Assert.IsType<MenuItem>(menu.Items[0]);
                Assert.Same(instance, rename.CommandParameter);
                Assert.Same(environment.Shell.Sidebar.EditInstanceTagsCommand, rename.Command);
                Assert.Equal(!instance.IsLocal, rename.IsEnabled);
                menu.IsOpen = false;
            }
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); }
    });
}
