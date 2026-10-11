namespace Asgard.Hermod.UI.Tests;

/// <summary>编辑已保存连接后更新页签并失效缓存，取消不会扰动浏览状态。</summary>
public sealed class ObjectStorageConnectionEditTests
{
    /// <summary>保存成功重新读取相同名称的容器，标题、提供方说明和根目录同步更新。</summary>
    [Fact]
    public async Task SavedEdit_UpdatesHeaderAndInvalidatesConnection()
    {
        var service = new ObjectStorageTestService();
        var editor = new ObjectStorageTestInstanceEditor
        {
            Result = new("oss", "oss", "新存储实例", "cloud", ["Staging"], Endpoint: "新的服务商",
                TypeId: "storage", ProviderId: "other-provider")
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, editor: editor);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.NavigateAsync(model.Buckets[0], "old/", TestContext.Current.CancellationToken);
        int changed = 0;
        model.BucketsChanged += (_, _) => changed++;
        await model.EditConnectionCommand.ExecuteAsync();

        Assert.Equal("oss", editor.EditedId);
        Assert.Equal("新存储实例", model.Name);
        Assert.Equal(model.Name, model.Header);
        Assert.Equal("新的服务商 / cn-hangzhou", model.Subtitle);
        Assert.Equal("Staging", Assert.Single(model.Tags));
        Assert.Equal(2, service.BucketReads);
        Assert.True(changed > 0);
        Assert.Empty(model.Prefix);
    }

    /// <summary>用户取消后保留目录和已有连接，不重新读桶。</summary>
    [Fact]
    public async Task CancelledEdit_KeepsExistingConnectionAndPrefix()
    {
        var service = new ObjectStorageTestService();
        var editor = new ObjectStorageTestInstanceEditor();
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, editor: editor);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.NavigateAsync(model.Buckets[0], "old/", TestContext.Current.CancellationToken);
        await model.EditConnectionCommand.ExecuteAsync();
        Assert.Equal("old/", model.Prefix);
        Assert.Equal(1, service.BucketReads);
        Assert.Equal("OSS · Development", model.Header);
    }
}
