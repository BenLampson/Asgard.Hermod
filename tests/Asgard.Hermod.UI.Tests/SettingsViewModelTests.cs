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
        Assert.Equal(new[] { "general", "appearance", "tags", "storage", "file-opening", "account", "shortcuts", "about" },
            model.Categories.Select(category => category.Id));
        Assert.Equal("tags", Assert.Single(model.Categories, category => category.IsAvailable).Id);
        model.SelectCategoryCommand.Execute(model.Categories[0]);
        Assert.Equal("tags", model.SelectedCategory.Id);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(model.Tags.IsInitialized);
        Assert.Equal(1, service.SubscriptionCount);
        model.Dispose();
        Assert.Equal(0, service.SubscriptionCount);
    }
}
