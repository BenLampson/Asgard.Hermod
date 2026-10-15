namespace Asgard.Hermod.UI.Tests;

/// <summary>验证设置导航只开放已设计分类，并管理窗口范围内的订阅。</summary>
public sealed class SettingsViewModelTests
{
    /// <summary>导航保留所有设计分类，未实现分类不能切换，关闭后释放标签来源。</summary>
    [Fact]
    public async Task Categories_ExposeOnlyDesignedPageAndDisposeItsSubscription()
    {
        var service = new TagManagementTestService();
        using var model = new SettingsViewModel(service, new TagDialogTestService());
        Assert.Equal(new[] { "general", "appearance", "agent", "tags", "storage", "file-opening", "account", "shortcuts", "about" },
            model.Categories.Select(category => category.Id));
        Assert.Equal(new[] { "tags", "about" }, model.Categories.Where(category => category.IsAvailable).Select(category => category.Id));
        model.SelectCategoryCommand.Execute(model.Categories[0]);
        Assert.Equal("tags", model.SelectedCategory.Id);
        model.SelectCategoryCommand.Execute(model.Categories.Single(category => category.Id == "about")); Assert.True(model.IsAboutSelected); Assert.False(model.IsTagsSelected);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.Tags.IsInitialized);
        Assert.Equal(1, service.SubscriptionCount);
        model.Dispose();
        Assert.Equal(0, service.SubscriptionCount);
    }
}
