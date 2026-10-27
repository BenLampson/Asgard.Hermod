using System.Globalization;
using System.Windows.Automation;
using Asgard.Hermod.UI.Views.Shell;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实侧栏区分类型与品牌，并在双语、主题和折叠状态下呈现配置同步标志。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceSidebarPresentationTests(WpfTestFixture fixture)
{
    /// <summary>新连接器的类型图标来自注册元数据，无需侧栏认识类型 ID。</summary>
    [Fact]
    public void TypeIcon_UsesRegisteredDescriptor()
    {
        var provider = new NewInstanceWizardTestDockerProvider();
        var registry = new NewInstanceWizardTestRegistry(provider);
        var item = new InstanceItemViewModel(new("docker", "Example", "Example", "provider-aliyun-oss", [],
            TypeId: provider.Descriptor.Type.Id, ProviderId: provider.Descriptor.Id), registry);
        Assert.Equal(provider.Descriptor.Type.Icon, item.TypeIcon);
        Assert.Equal(provider.Descriptor.Type.Name, item.TypeName);
        Assert.True(item.HasProviderIcon);
    }

    /// <summary>不同同步状态不改动名称及打开命令，本地模式完全隐藏云端标记。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("fr-FR", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Row_SeparatesTypeBrandAndCloudStatus(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        themes.SetTheme(theme);
        var status = ConfigurationSyncStatus.Local;
        var source = new ObjectStorageWorkspaceDataSource
        {
            Instance = new("example", "Example OSS with a long name", "Example OSS", "provider-aliyun-oss", [],
                Endpoint: "Aliyun OSS", TypeId: "storage", ProviderId: "aliyun-oss"),
        };
        using var sidebar = new InstanceSidebarViewModel(source, new FakeInstanceEditor(), _ => { }, () => { }, syncStatus: _ => status);
        var view = new InstanceSidebarView { DataContext = sidebar };
        var window = new HermodWindow { Content = view, Width = 280, Height = 480 };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var item = Assert.Single(sidebar.AllInstances);
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => button.IsVisible && button.Command == sidebar.OpenInstanceCommand);
            var type = Assert.Single(WindowTestVisuals.Descendants<IconView>(button), icon => icon.Name == "InstanceTypeIcon");
            var brand = Assert.Single(WindowTestVisuals.Descendants<IconView>(button), icon => icon.Name == "InstanceProviderIcon");
            var cloud = Assert.Single(WindowTestVisuals.Descendants<IconView>(button), icon => icon.Name == "InstanceCloudStatus");
            Assert.Equal("box", type.Kind);
            Assert.Equal("provider-aliyun-oss", brand.Kind);
            Assert.Equal(Text.Format("InstanceTypeHint", Text.Get("Storage")), type.ToolTip);
            Assert.Equal(Text.Format("InstanceProviderHint", "Aliyun OSS"), brand.ToolTip);
            foreach (var next in Enum.GetValues<ConfigurationSyncStatus>())
            {
                status = next;
                sidebar.RefreshSyncStates();
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Equal(next != ConfigurationSyncStatus.Local, cloud.IsVisible);
                Assert.Equal(item.SyncIcon, cloud.Kind);
                var name = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button), text => text.Name == "SidebarInstanceName");
                Assert.Same(Application.Current.FindResource(next == ConfigurationSyncStatus.Conflict ? "DangerBrush" : "TextBodyBrush"), name.Foreground);
                if (next == ConfigurationSyncStatus.Conflict) Assert.Same(Application.Current.FindResource("DangerBrush"), cloud.Foreground);
                Assert.Equal(Text.Get("InstanceSync" + next), cloud.ToolTip);
                Assert.Equal(item.AccessibleName, AutomationProperties.GetName(button));
            }
            status = ConfigurationSyncStatus.Synced;
            sidebar.RefreshSyncStates();
            window.Width = 200;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(type.TranslatePoint(new Point(), button).X < brand.TranslatePoint(new Point(), button).X);
            Assert.True(brand.TranslatePoint(new Point(), button).X + brand.ActualWidth <= cloud.TranslatePoint(new Point(), button).X);
            Assert.True(cloud.TranslatePoint(new Point(), button).X + cloud.ActualWidth <= button.ActualWidth);
            await WindowPreviewCapture.VerifyAsync(window, $"instance-sidebar-{theme}-{language}");
            view.IsExpanded = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var collapsed = Assert.Single(WindowTestVisuals.Descendants<Button>(view), candidate => candidate.IsVisible && candidate.Command == sidebar.OpenInstanceCommand);
            Assert.Equal(36, collapsed.ActualWidth);
            Assert.Equal(3, WindowTestVisuals.Descendants<IconView>(collapsed).Count(icon => icon.IsVisible));
            Assert.Same(item, collapsed.CommandParameter);
            status = ConfigurationSyncStatus.Local;
            sidebar.RefreshSyncStates();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(2, WindowTestVisuals.Descendants<IconView>(collapsed).Count(icon => icon.IsVisible));
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; }
    });
}
