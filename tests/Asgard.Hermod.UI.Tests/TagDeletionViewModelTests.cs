using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证删除完整影响、显式确认与失败重试边界。</summary>
public sealed class TagDeletionViewModelTests
{
    /// <summary>初始读取只展示子树和去重影响，取消也不会触发删除。</summary>
    [Fact]
    public async Task Initialize_PreviewsWholeSubtreeWithoutDeleting()
    {
        var service = new TagManagementTestService { AffectedInstanceCount = 2 };
        using var model = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "team"));
        var completions = new List<bool>();
        model.CloseRequested += (_, accepted) => completions.Add(accepted);
        await model.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Team", "Team/Platform", "Team/Platform/API" }, model.Tags.Select(tag => tag.Path));
        Assert.Equal(Text.Format("DeleteAndItsChildTags", "Team"), model.Title);
        Assert.Contains("2 个实例", model.Impact);
        Assert.Contains("不会删除实例或连接信息", model.Impact);
        Assert.Equal(Text.Format("DeleteTags", 3), model.DeleteLabel);
        Assert.True(model.CanDelete);
        Assert.Equal(0, service.DeleteCount);
        model.CancelCommand.Execute(null);
        Assert.Equal(new[] { false }, completions);
        Assert.Equal(0, service.DeleteCount);
    }

    /// <summary>失败保留已展示的影响和确认窗口，重试成功才结束确认。</summary>
    [Fact]
    public async Task Delete_ServiceFailureRetainsPreviewAndAllowsRetry()
    {
        var service = new TagManagementTestService { DeleteFailure = new IOException("删除未完成") };
        using var model = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "region"));
        var completions = new List<bool>();
        model.CloseRequested += (_, accepted) => completions.Add(accepted);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var preview = model.Preview;
        await model.DeleteCommand.ExecuteAsync();

        Assert.Same(preview, model.Preview);
        Assert.Equal(Text.Get("TheOperationFailedTryAgain"), model.Error);
        Assert.False(model.WasDeleted);
        Assert.Empty(completions);
        Assert.True(model.CanDelete);

        service.DeleteFailure = null;
        await model.DeleteCommand.ExecuteAsync();
        Assert.True(model.WasDeleted);
        Assert.Equal(new[] { true }, completions);
        Assert.Equal(2, service.DeleteCount);
        Assert.DoesNotContain(service.Tags, tag => tag.Id is "region" or "eu" or "us");
        Assert.Equal(3, service.Tags.Count(tag => tag.IsBuiltIn));
    }

    /// <summary>无法读取影响时不能确认，读取重试成功后才启用删除。</summary>
    [Fact]
    public async Task Preview_FailureRequiresSuccessfulRetryBeforeDelete()
    {
        var service = new TagManagementTestService { PreviewFailure = new IOException("无法读取删除影响") };
        using var model = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "api"));
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.False(model.CanDelete);
        Assert.Equal(0, service.DeleteCount);

        service.PreviewFailure = null;
        await model.RetryCommand.ExecuteAsync();
        Assert.True(model.CanDelete);
        Assert.Single(model.Tags);
        Assert.Equal(Text.Format("Delete2", "API"), model.Title);
        Assert.Equal(Text.Format("DeleteTags", 1), model.DeleteLabel);
    }

    /// <summary>即使直接创建确认状态，内置环境也不能进入预览和删除服务。</summary>
    [Fact]
    public async Task BuiltInEnvironment_CannotPreviewOrDelete()
    {
        var service = new TagManagementTestService();
        using var model = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "production"));
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.DeleteAsync(TestContext.Current.CancellationToken);
        Assert.False(model.CanDelete);
        Assert.NotEmpty(model.Error);
        Assert.Equal(0, service.PreviewCount);
        Assert.Equal(0, service.DeleteCount);
    }
}
