namespace Asgard.Hermod.UI.Tests;

/// <summary>区分持久化配置、未配置导航条目与已有详情，不推断远程连接状态。</summary>
public sealed class ServiceDetailViewModelTests
{
    /// <summary>保存成功但详情尚未接入时，不再要求用户重复配置。</summary>
    [Theory]
    [InlineData("", "实例配置已保存")]
    [InlineData("https://oss-cn-hangzhou.aliyuncs.com", "https://oss-cn-hangzhou.aliyuncs.com")]
    public void SavedConfiguration_WithoutDetailsShowsConfiguredState(string endpoint, string subtitle)
    {
        var instance = new InstanceItemViewModel(new InstanceSummary(
            "oss", "oss", "云存储", "database", [], Endpoint: endpoint,
            TypeId: "storage", ProviderId: "aliyun-oss"));

        var model = new ServiceDetailViewModel(instance, snapshot: null);

        Assert.True(model.HasConfiguration);
        Assert.False(model.HasDetails);
        Assert.False(model.IsPreview);
        Assert.Equal("已配置", model.Status);
        Assert.Equal(StatusTone.Muted, model.StatusTone);
        Assert.Equal(subtitle, model.Subtitle);
        Assert.Equal("实例已配置", model.PlaceholderTitle);
        Assert.Equal("实例详情页面待接入。", model.PlaceholderDescription);
        Assert.Empty(model.Metrics);
        Assert.Empty(model.Configuration);
        Assert.Empty(model.Events);
        Assert.False(model.RestartCommand.CanExecute(null));
        Assert.False(model.DeployCommand.CanExecute(null));
    }

    /// <summary>缺少配置身份时保留未配置状态，不能将地址或部分身份当成保存成功。</summary>
    [Theory]
    [InlineData("", "", "", "No connector configured")]
    [InlineData("storage", "", "", "No connector configured")]
    [InlineData("", "aliyun-oss", "", "No connector configured")]
    [InlineData("", "", "https://example.invalid", "https://example.invalid")]
    public void MissingConfiguration_WithoutDetailsKeepsUnconfiguredState(
        string typeId, string providerId, string endpoint, string subtitle)
    {
        var instance = new InstanceItemViewModel(new InstanceSummary(
            "legacy", "legacy", "旧实例", "server", [], Endpoint: endpoint,
            TypeId: typeId, ProviderId: providerId));

        var model = new ServiceDetailViewModel(instance, snapshot: null);

        Assert.False(model.HasConfiguration);
        Assert.False(model.HasDetails);
        Assert.Equal("Not configured", model.Status);
        Assert.Equal(StatusTone.Muted, model.StatusTone);
        Assert.Equal(subtitle, model.Subtitle);
        Assert.Equal("No connector configured", model.PlaceholderTitle);
        Assert.Equal("Add a service connection to view configuration, metrics and events.", model.PlaceholderDescription);
        Assert.False(model.RestartCommand.CanExecute(null));
        Assert.False(model.DeployCommand.CanExecute(null));
    }

    /// <summary>已提供的设计快照完整保留，预览标识不因没有真实配置而丢失。</summary>
    [Fact]
    public void ProvidedSnapshot_PreservesDetailsAndPreviewMetadata()
    {
        MetricValue[] metrics = [new("CPU", "23%", "cpu")];
        ConfigurationValue[] configuration = [new("Host", "demo.local")];
        ServiceEvent[] events = [new("Preview service created", "刚刚", "check", StatusTone.Success)];
        var snapshot = new ServiceSnapshot("Demo service", "Design preview", "Running",
            StatusTone.Success, true, metrics, configuration, events);
        var instance = new InstanceItemViewModel(new InstanceSummary("demo", "demo", "预览", "server", []));

        var model = new ServiceDetailViewModel(instance, snapshot);

        Assert.False(model.HasConfiguration);
        Assert.True(model.HasDetails);
        Assert.True(model.IsPreview);
        Assert.Equal(snapshot.Name, model.Name);
        Assert.Equal(snapshot.Subtitle, model.Subtitle);
        Assert.Equal(snapshot.Status, model.Status);
        Assert.Equal(snapshot.StatusTone, model.StatusTone);
        Assert.Same(metrics, model.Metrics);
        Assert.Same(configuration, model.Configuration);
        Assert.Same(events, model.Events);
        Assert.False(model.RestartCommand.CanExecute(null));
        Assert.False(model.DeployCommand.CanExecute(null));
    }
}
