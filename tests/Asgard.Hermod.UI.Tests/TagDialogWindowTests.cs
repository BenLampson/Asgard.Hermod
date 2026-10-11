using System.Windows.Automation;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实模态绑定、失败重试和窗体释放，避免仅验证未显示的状态对象。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TagDialogWindowTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>复用独立 STA 上的真实 WPF 资源和消息循环。</summary>
    public TagDialogWindowTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>设置树选择更新详情，搜索无匹配保留环境，关闭解除共享来源订阅。</summary>
    [Fact]
    public Task Settings_SelectionSearchAndCloseUseRealBindings() => _fixture.RunAsync(async () =>
    {
        var service = new TagManagementTestService();
        using var model = new SettingsViewModel(service, new TagDialogTestService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var owner = CreateOwner();
        var window = new SettingsWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            window.Owner = owner;
            await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Single(WindowTestVisuals.Descendants<HermodTitleBar>(window));
                var region = model.Tags.Rows.Single(node => node.Id == "region");
                var select = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, region.SelectCommand));
                WindowTestVisuals.Invoke(select);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.Equal("Region", model.Tags.SelectedTag?.Name);
                var edit = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, model.Tags.EditCommand));
                Assert.True(edit.IsVisible);
                Assert.True(edit.IsEnabled);

                var search = Assert.Single(WindowTestVisuals.Descendants<TextBox>(window), input => AutomationProperties.GetName(input) == "搜索 Tag");
                search.Text = "unmatched value";
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(model.Tags.HasNoMatches);
                Assert.Equal(3, model.Tags.BuiltInTags.Count);
                Assert.Equal("region", model.Tags.SelectedTag?.Id);
                var clear = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, model.Tags.ClearSearchCommand));
                Assert.True(clear.IsVisible);
                WindowTestVisuals.Invoke(clear);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.Equal(6, model.Tags.Rows.Count);

                var newTag = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, model.Tags.NewTagCommand) && button.IsVisible);
                AssertContained(WindowTestVisuals.ScreenBounds(window), WindowTestVisuals.ScreenBounds(newTag));
                AssertContained(WindowTestVisuals.ScreenBounds(window), WindowTestVisuals.ScreenBounds(edit));
                model.CloseCommand.Execute(null);
            });
            Assert.Equal(0, service.SubscriptionCount);
            Assert.False(model.Tags.NewTagCommand.CanExecute(null));
        }
        finally
        {
            if (window.IsVisible) window.Close();
            owner.Close();
        }
    });

    /// <summary>无效 HEX 实际禁用按钮，后端保存失败保持窗口和输入，重试成功结束模态。</summary>
    [Fact]
    public Task Editor_InvalidColorAndFailedSaveKeepDraftVisible() => _fixture.RunAsync(async () =>
    {
        var service = new TagManagementTestService { SaveFailure = new IOException("保存暂时失败") };
        using var model = new TagEditorViewModel(service, parentId: "team") { Name = "Data" };
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var owner = CreateOwner();
        var window = new TagEditorWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            window.Owner = owner;
            var result = await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Single(WindowTestVisuals.Descendants<DialogActionBar>(window));
                Assert.Single(WindowTestVisuals.Descendants<HermodTitleBar>(window));
                var color = Assert.IsType<TextBox>(window.FindName("HexInput"));
                var save = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, model.SaveCommand));
                color.Text = "#12";
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(save.IsEnabled);
                Assert.Equal("#3B82F6", model.PreviewColor);
                color.Text = "#3af";
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(save.IsEnabled);
                WindowTestVisuals.Invoke(save);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(window.IsVisible);
                Assert.Equal("保存暂时失败", model.Error);
                Assert.Equal("#3af", color.Text);
                Assert.Equal("Data", Assert.IsType<TextBox>(window.FindName("NameInput")).Text);
                AssertContained(WindowTestVisuals.ScreenBounds(window), WindowTestVisuals.ScreenBounds(save));

                service.SaveFailure = null;
                WindowTestVisuals.Invoke(save);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(window.IsVisible);
            });
            Assert.Equal<bool?>(true, result);
            Assert.Equal("Team/Data", model.Result?.Path);
            Assert.Equal("#33AAFF", service.LastDraft?.Color);
        }
        finally
        {
            if (window.IsVisible) window.Close();
            owner.Close();
        }
    });

    /// <summary>删除预览不执行写入，没有默认确认按钮，主动确认成功才移除子树。</summary>
    [Fact]
    public Task Deletion_RequiresExplicitConfirmationAndKeepsEnvironment() => _fixture.RunAsync(async () =>
    {
        var service = new TagManagementTestService();
        using var model = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "region"));
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var owner = CreateOwner();
        var window = new DeleteTagWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            window.Owner = owner;
            var result = await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Equal(0, service.DeleteCount);
                var delete = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => ReferenceEquals(button.Command, model.DeleteCommand));
                Assert.False(delete.IsDefault);
                Assert.True(delete.IsEnabled);
                Assert.Equal("删除 3 个 Tag", delete.Content);
                AssertContained(WindowTestVisuals.ScreenBounds(window), WindowTestVisuals.ScreenBounds(delete));
                WindowTestVisuals.Invoke(delete);
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(true, result);
            Assert.Equal(1, service.DeleteCount);
            Assert.Equal(3, service.Tags.Count(tag => tag.IsBuiltIn));
        }
        finally
        {
            if (window.IsVisible) window.Close();
            owner.Close();
        }
    });

    /// <summary>判断实际按钮在窗口内可见，允许 DPI 换算产生一像素误差。</summary>
    private static void AssertContained(Rect outer, Rect inner) => Assert.True(
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1
        && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1,
        $"操作按钮超出窗口边界。Window={outer}; Button={inner}");

    /// <summary>创建不可见但拥有真实 HWND 的模态所有者。</summary>
    private static HermodWindow CreateOwner() => new()
    {
        Title = "Tag dialog owner",
        Content = new Grid(),
        Width = 640,
        Height = 480,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 100,
        Top = 100,
    };
}
