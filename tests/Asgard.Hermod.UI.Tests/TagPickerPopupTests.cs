using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>实际侧栏弹层验证草稿提交边界和窗口生命周期，不另写弹层事件代理。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TagPickerPopupTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>弹层使用真实窗口和现有产品资源，在共享 STA 中交互。</summary>
    public TagPickerPopupTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>只有应用按钮提交草稿；已经完成的草稿不能再次应用或关闭重新打开的选择器。</summary>
    [Theory]
    [InlineData(TagFilterMatchMode.Any)]
    [InlineData(TagFilterMatchMode.All)]
    public Task Apply_SubmitsDraftAndCompletedPickerCannotCommitAgain(TagFilterMatchMode mode) => _fixture.RunAsync(async () =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        sidebar.ApplyTagFilter(["Environment/Production"], TagFilterMatchMode.Any);
        var view = new InstanceSidebarView { DataContext = sidebar };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var (popup, picker, draft) = await OpenAsync(view);
            TagPickerTestData.Node(draft, "Owner/Platform").IsChecked = true;
            draft.MatchMode = mode;
            Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));

            InvokePickerButton(picker, draft.ApplyCommand);
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.False(popup.IsOpen);
            Assert.Null(picker.Model);
            Assert.False(draft.ApplyCommand.CanExecute(null));
            Assert.Equal(new[] { "Environment/Production", "Owner/Platform" }, sidebar.SelectedTagPaths);
            Assert.Equal(mode, sidebar.TagMatchMode);
            string[] expected = mode == TagFilterMatchMode.Any
                ? ["api-east", "api-west", "copy", "root", "worker"] : ["api-east"];
            Assert.Equal(expected, TagPickerTestData.VisibleIds(sidebar));

            var (_, _, reopened) = await OpenAsync(view);
            Assert.Equal(sidebar.SelectedTagPaths, reopened.SelectedPaths);
            Assert.Equal(mode, reopened.MatchMode);
            draft.ClearCommand.Execute(null);
            draft.ApplyCommand.Execute(null);
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.True(popup.IsOpen);
            Assert.Same(reopened, picker.Model);
            Assert.Equal(expected, TagPickerTestData.VisibleIds(sidebar));
            view.CloseTagPicker();
        }
        finally
        {
            view.CloseTagPicker();
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>取消按钮和外部关闭均放弃变化，下次打开读取已应用条件而非上次草稿。</summary>
    [Theory]
    [InlineData("Cancel")]
    [InlineData("ExternalClose")]
    public Task Dismissal_KeepsAppliedFilterAndStartsFreshDraft(string action) => _fixture.RunAsync(async () =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        sidebar.ApplyTagFilter(["Environment/Production"], TagFilterMatchMode.All);
        var view = new InstanceSidebarView { DataContext = sidebar };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var (popup, picker, draft) = await OpenAsync(view);
            draft.ClearCommand.Execute(null);
            TagPickerTestData.Node(draft, "Owner").IsChecked = true;
            draft.MatchMode = TagFilterMatchMode.Any;
            Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));

            if (action == "Cancel")
                InvokePickerButton(picker, draft.CancelCommand);
            else
                view.CloseTagPicker();
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.False(popup.IsOpen);
            Assert.Null(picker.Model);
            Assert.Equal(new[] { "Environment/Production" }, sidebar.SelectedTagPaths);
            Assert.Equal(TagFilterMatchMode.All, sidebar.TagMatchMode);
            Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));

            var (_, _, reopened) = await OpenAsync(view);
            Assert.NotSame(draft, reopened);
            Assert.Equal(new[] { "Environment/Production" }, reopened.SelectedPaths);
            Assert.Equal(TagFilterMatchMode.All, reopened.MatchMode);

            draft.ClearCommand.Execute(null);
            draft.ApplyCommand.Execute(null);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(popup.IsOpen);
            Assert.Same(reopened, picker.Model);
            Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));
            view.CloseTagPicker();
        }
        finally
        {
            view.CloseTagPicker();
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>拥有者移动、缩放或关闭都移除浮层，旧模型的完成事件随后失去作用。</summary>
    [Theory]
    [InlineData("Move")]
    [InlineData("Resize")]
    [InlineData("Close")]
    public Task OwnerLifecycle_ClosesPopupAndReleasesDraft(string action) => _fixture.RunAsync(async () =>
    {
        var sidebar = TagPickerTestData.CreateSidebar();
        sidebar.ApplyTagFilter(["Environment/Production"], TagFilterMatchMode.Any);
        var view = new InstanceSidebarView { DataContext = sidebar };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var (popup, picker, draft) = await OpenAsync(view);
            draft.ClearCommand.Execute(null);
            TagPickerTestData.Node(draft, "Owner/Platform").IsChecked = true;

            switch (action)
            {
                case "Move": window.Left += 25; break;
                case "Resize": window.Width += 25; break;
                case "Close": window.Close(); break;
            }
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.False(popup.IsOpen);
            Assert.Null(picker.Model);
            draft.ApplyCommand.Execute(null);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(new[] { "Environment/Production" }, sidebar.SelectedTagPaths);
            Assert.Equal(new[] { "api-east", "api-west", "root" }, TagPickerTestData.VisibleIds(sidebar));
        }
        finally
        {
            view.CloseTagPicker();
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>点击产品按钮打开真实 Popup，屏蔽像素显示而不绕过事件或窗口生命周期。</summary>
    private static async Task<(Popup Popup, TagTreePicker Picker, TagTreePickerViewModel Draft)> OpenAsync(
        InstanceSidebarView view)
    {
        var popup = Assert.IsType<Popup>(view.FindName("TagFilterPopup"));
        popup.PopupAnimation = PopupAnimation.None;
        Assert.IsAssignableFrom<UIElement>(popup.Child).Opacity = 0;
        var picker = Assert.IsType<TagTreePicker>(view.FindName("TagPicker"));
        var button = Assert.IsType<Button>(view.FindName("TagFilterButton"));
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(popup.IsOpen);
        var model = Assert.IsType<TagTreePickerViewModel>(picker.Model);
        picker.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
        return (popup, picker, model);
    }

    /// <summary>使用产品按钮已绑定的命令，验证应用和取消按钮随草稿装配正确。</summary>
    private static void InvokePickerButton(TagTreePicker picker, ICommand command) => WindowTestVisuals.Invoke(
        Assert.Single(WindowTestVisuals.Descendants<Button>(picker), button => ReferenceEquals(button.Command, command)));

    /// <summary>拥有者创建真实 HWND，测试内容与弹层都保持不可见。</summary>
    private static HermodWindow CreateWindow(InstanceSidebarView view) => new()
    {
        Title = "Tag picker regression",
        Content = view,
        Width = 240,
        Height = 620,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 100,
        Top = 100,
    };
}
