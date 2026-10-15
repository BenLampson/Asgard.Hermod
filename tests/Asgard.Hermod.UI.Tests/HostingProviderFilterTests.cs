using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>跨类型的提供商筛选按品牌聚合，与连接方式和翻译文案无关。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class HostingProviderFilterTests(WpfTestFixture fixture)
{
    /// <summary>相同品牌的 SSH 和数据库可共同筛选，相同连接器的不同品牌仍彼此独立。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task ProviderFilter_UsesSavedBrandAcrossInstanceTypes(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var editor = new FakeInstanceEditor();
            await using var shell = new ShellViewModel(new Source(), editor, new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources));
            var main = shell.Main;
            main.SelectedNavigation = main.Navigation.Single(item => item.Id == "all");
            main.SelectedProvider = main.Providers.Single(option => option.Id == "hosting:volcengine");
            Assert.Equal(2, main.Rows.Count);
            Assert.All(main.Rows, row => Assert.Equal("volcengine", row.Instance.Summary.HostingProviderId));
            main.SelectedNavigation = main.Navigation.Single(item => item.Id == "ssh");
            Assert.Equal("ssh-volcengine", Assert.Single(main.Rows).Instance.Id);
            main.SelectedProvider = main.Providers.Single(option => option.Id == "hosting:aliyun");
            var ali = Assert.Single(main.Rows);
            editor.RenameHandler = _ => Task.FromResult<InstanceSummary?>(ali.Instance.Summary with { HostingProviderId = "volcengine" });
            await main.RenameCommand.ExecuteAsync(ali);
            main.SelectedProvider = main.Providers.Single(option => option.Id == "hosting:volcengine");
            Assert.Equal(2, main.Rows.Count);
            Assert.DoesNotContain(main.Providers, option => option.Id == "hosting:aliyun");
            main.SelectedNavigation = main.Navigation.Single(item => item.Id == "all");
            main.SelectedProvider = main.Providers.Single(option => option.Id == "hosting:self-hosted");
            Assert.Equal("redis-self-hosted", Assert.Single(main.Rows).Instance.Id);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private sealed class Source : IWorkspaceDataSource
    {
        public IReadOnlyList<InstanceSummary> GetInstances() =>
        [
            Summary("ssh-volcengine", "ssh", "volcengine"), Summary("mysql-volcengine", "mysql", "volcengine"),
            Summary("ssh-aliyun", "ssh", "aliyun"), Summary("redis-self-hosted", "redis", ""),
        ];
        public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
        private static InstanceSummary Summary(string id, string type, string brand) =>
            new(id, id, id, "terminal", [], TypeId: type, ProviderId: "transport") { HostingProviderId = brand };
    }
}
