using System.Windows.Interop;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在显示边界验证设置窗口复用和释放，不创建 HWND、不激活用户桌面。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SettingsEditorTests(WpfTestFixture fixture)
{
    /// <summary>打开立即返回，重复点击保留页面与输入；真正关闭后释放订阅并允许重新打开。</summary>
    [Fact]
    public Task Reopen_ActivatesSameIndependentWindowAndPreservesCurrentPage() => fixture.RunOffscreenAsync(async () =>
    {
        var tags = new TagManagementTestService(); var presented = new List<SettingsWindow>();
        using var editor = new SettingsEditorService(() => throw new InvalidOperationException("Settings must be independent"), tags, new LanguagePreferenceTestStore())
        { PresentWindow = window => presented.Add(window) };
        await editor.OpenAsync();
        var first = Assert.Single(presented);
        Assert.IsAssignableFrom<HermodWindow>(first); Assert.False(typeof(HermodDialog).IsAssignableFrom(first.GetType()));
        Assert.Null(first.Owner); Assert.True(first.ShowInTaskbar); Assert.Equal(ResizeMode.CanResize, first.ResizeMode);
        Assert.Equal(0, new WindowInteropHelper(first).Handle); Assert.False(ComponentDispatcher.IsThreadModal);
        var model = Assert.IsType<SettingsViewModel>(first.DataContext);
        Assert.Equal("general", model.SelectedCategory.Id); Assert.Equal(1, tags.SubscriptionCount);
        await editor.OpenTagsAsync(); model.Tags.SearchText = "keep this search";
        await editor.OpenAsync();
        Assert.Equal(3, presented.Count); Assert.All(presented, window => Assert.Same(first, window));
        Assert.Equal("tags", model.SelectedCategory.Id); Assert.Equal("keep this search", model.Tags.SearchText);
        Assert.Equal(1, tags.ReadCount); Assert.Equal(1, tags.SubscriptionCount);
        model.CloseCommand.Execute(null); Assert.Equal(0, tags.SubscriptionCount);
        await editor.OpenAsync();
        Assert.NotSame(first, presented.Last()); Assert.Equal(2, tags.ReadCount); Assert.Equal(1, tags.SubscriptionCount);
        editor.Dispose(); Assert.Equal(0, tags.SubscriptionCount);
    });

    /// <summary>慢初始化期间的多个入口共用首次加载，普通设置入口不覆盖明确的分类请求。</summary>
    [Fact]
    public Task PendingRequests_ShareOneLoadAndUseLatestExplicitCategory() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ManagedTag>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tags = new TagManagementTestService { NextRead = pending }; var presented = new List<SettingsWindow>();
        using var editor = new SettingsEditorService(() => null, tags, new LanguagePreferenceTestStore())
        { PresentWindow = window => presented.Add(window) };
        var first = editor.OpenAsync(); var second = editor.OpenTagsAsync(); var third = editor.OpenAsync();
        Assert.False(first.IsCompleted); Assert.Empty(presented); Assert.Equal(1, tags.ReadCount); Assert.Equal(1, tags.SubscriptionCount);
        pending.SetResult(tags.Tags);
        await Task.WhenAll(first, second, third);
        var model = Assert.IsType<SettingsViewModel>(Assert.Single(presented).DataContext);
        Assert.Equal("tags", model.SelectedCategory.Id); Assert.Equal(1, tags.ReadCount);
    });

    /// <summary>退出会取消读取，迟到结果与后续入口都不能重新打开设置。</summary>
    [Fact]
    public Task DisposeDuringInitialization_CancelsLoadingAndSuppressesLateWindow() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ManagedTag>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tags = new TagManagementTestService { NextRead = pending }; int presentations = 0;
        using var editor = new SettingsEditorService(() => null, tags, new LanguagePreferenceTestStore())
        { PresentWindow = _ => presentations++ };
        var opening = editor.OpenAsync(); Assert.Equal(1, tags.SubscriptionCount);
        editor.Dispose(); await opening;
        pending.SetResult(tags.Tags); await editor.OpenAsync(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(0, presentations); Assert.Equal(0, tags.SubscriptionCount);
    });

    /// <summary>显示失败清除缓存窗口和模型，下一次点击可以重新打开而不会留下僵死实例。</summary>
    [Fact]
    public Task PresentationFailure_ReleasesModelAndAllowsRetry() => fixture.RunOffscreenAsync(async () =>
    {
        var tags = new TagManagementTestService(); int attempts = 0; SettingsWindow? shown = null;
        using var editor = new SettingsEditorService(() => null, tags, new LanguagePreferenceTestStore())
        {
            PresentWindow = window =>
            {
                if (++attempts == 1) throw new InvalidOperationException("test display failure");
                shown = window;
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.OpenAsync());
        Assert.Equal(0, tags.SubscriptionCount);
        await editor.OpenAsync(); Assert.NotNull(shown); Assert.Equal(2, attempts); Assert.Equal(1, tags.SubscriptionCount);
        Assert.Equal(0, new WindowInteropHelper(shown).Handle);
    });
}
