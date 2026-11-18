using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class McpSettingsViewTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en-US", "Instance access", "Copy prompt")]
    [InlineData("zh-CN", "实例访问", "复制提示词")]
    [InlineData("de-DE", "Instance access", "Copy prompt")]
    public Task InstancesDefaultToAllowedAndExclusionsCanBeAddedOrRemoved(string culture, string heading, string copyLabel) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settings = new SettingsStub();
            string? copied = null;
            using var model = new McpSettingsViewModel(settings, text => copied = text);
            await model.RefreshAsync();
            var page = new McpSettingsView { DataContext = model };
            page.Measure(new(600, 800)); page.Arrange(new(0, 0, 600, 800)); page.UpdateLayout();
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(page), text => text.Text == heading);
            var prompt = Assert.IsType<Button>(page.FindName("McpCopyPromptButton"));
            Assert.Equal(copyLabel, prompt.Content);
            await model.CopyPromptCommand.ExecuteAsync();
            Assert.Equal(Text.Get("McpStarterPrompt"), copied);
            Assert.Equal(0, settings.ClientConfigurationReads);
            Assert.All(model.InstanceAccess, item => Assert.True(item.IsAllowed));
            Assert.All(model.InstanceAccess, item => Assert.False(item.AlwaysAllow));
            var instance = model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage");
            var alwaysAllow = Assert.Single(WindowTestVisuals.Descendants<CheckBox>(page),
                box => box.DataContext == instance && Equals(box.Content, Text.Get("McpAlwaysAllowInstance")));
            Assert.Equal(Text.Format("McpAlwaysAllowNamedInstance", instance.Label), System.Windows.Automation.AutomationProperties.GetName(alwaysAllow));
            alwaysAllow.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert.True(Assert.Single(settings.Approvals).Matches(instance.Target.Target));
            await model.RefreshAsync();
            instance = model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage");
            Assert.True(instance.AlwaysAllow);
            instance.AlwaysAllow = false;
            Assert.Empty(settings.Approvals);
            instance = model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage");
            instance.IsAllowed = false;
            Assert.Equal("oss", Assert.Single(settings.Exclusions).InstanceId);
            Assert.False(model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage").IsAllowed);
            model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage").IsAllowed = true;
            Assert.Empty(settings.Exclusions);
            model.SelectedTarget = model.Targets.Single(item => item.Target.TypeId == "storage");
            model.Resource = "exports"; model.Qualifier = "private/";
            await model.AddExclusionCommand.ExecuteAsync();
            var excluded = Assert.Single(settings.Exclusions);
            Assert.Equal("private/", excluded.Qualifier);
            var visible = Assert.Single(model.ExcludedScopes);
            Assert.Contains("private/", visible.Scope);
            await model.RemoveExclusionCommand.ExecuteAsync(visible);
            Assert.Empty(settings.Exclusions);
            Assert.Empty(model.ExcludedScopes);
            await model.ToggleCommand.ExecuteAsync();
            await model.CopyCommand.ExecuteAsync();
            Assert.Equal("test client configuration", copied);
            Assert.Equal(1, settings.ClientConfigurationReads);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    public Task FailedInstanceChangeRestoresThePreviousSelection() => fixture.RunOffscreenAsync(async () =>
    {
        var settings = new SettingsStub { FailChanges = true };
        using var model = new McpSettingsViewModel(settings);
        await model.RefreshAsync();
        var instance = model.InstanceAccess.First();
        instance.IsAllowed = false;
        Assert.True(instance.IsAllowed);
        Assert.Empty(settings.Exclusions);
        instance.AlwaysAllow = true;
        Assert.False(instance.AlwaysAllow);
        Assert.Empty(settings.Approvals);
        Assert.Equal(Text.Get("McpSettingsFailed"), model.Message);
    });

    [Fact]
    public Task DefaultAccessAndExclusionDialogsRemainUsableAfterResize() => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        var themes = new Asgard.Hermod.UI.Themes.ThemeService(Application.Current.Resources);
        themes.SetTheme(UiTheme.Dark);
        var settings = new SettingsStub();
        using var model = new McpSettingsViewModel(settings);
        await model.RefreshAsync();
        var page = new McpSettingsView { DataContext = model };
        var host = new HermodWindow { Content = page, Width = 760, Height = 760 };
        try
        {
            WindowPreviewCapture.Show(host);
            await WindowPreviewCapture.VerifyAsync(host, "mcp-default-settings");
            var add = Assert.IsType<Button>(page.FindName("McpOpenExclusion"));
            Assert.True(add.IsEnabled);
            model.SelectedTarget = model.Targets.Single(target => target.Target.TypeId == "storage");
            model.Resource = "exports"; model.Qualifier = "private/";
            var dialog = new McpExclusionWindow(model);
            try
            {
                WindowPreviewCapture.Show(dialog);
                await WindowPreviewCapture.VerifyAsync(dialog, "mcp-exclusion");
                dialog.Width = dialog.MinWidth; dialog.Height = dialog.MinHeight;
                await WindowPreviewCapture.VerifyAsync(dialog, "mcp-exclusion-narrow");
                var target = Assert.IsType<ComboBox>(dialog.FindName("McpExclusionTarget"));
                Assert.True(target.Focus());
                Assert.True(target.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            }
            finally { dialog.Close(); }
        }
        finally { host.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private sealed class SettingsStub : IMcpSettingsService
    {
        private static readonly McpWorkspace Workspace = new("", [new("cache", "Cache", "redis", "v1"),
            new("sql", "SQL", "mysql", "v1"), new("oss", "OSS", "storage", "v1")]);
        private bool _enabled;
        internal bool FailChanges { get; init; }
        internal List<McpExclusion> Exclusions { get; } = [];
        internal List<McpInstanceApproval> Approvals { get; } = [];
        internal int ClientConfigurationReads { get; private set; }
        public Task<McpSettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpSettingsSnapshot(Workspace, _enabled, [], Exclusions.ToArray(), InstanceApprovals: Approvals.ToArray()));
        public Task<McpSettingsSnapshot> SetInstanceApprovalAsync(string owner, McpInstanceApproval approval, bool enabled, CancellationToken cancellationToken = default)
        {
            if (FailChanges) throw new IOException("sensitive details");
            Approvals.RemoveAll(item => item.InstanceId == approval.InstanceId);
            if (enabled) Approvals.Add(approval);
            return ReadAsync(cancellationToken);
        }
        public Task<McpSettingsSnapshot> SetEnabledAsync(string owner, bool enabled, CancellationToken cancellationToken = default)
        { _enabled = enabled; return ReadAsync(cancellationToken); }
        public Task<McpSettingsSnapshot> AddGrantAsync(string owner, McpGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> RemoveGrantAsync(string owner, string grantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> SetExclusionsAsync(string owner, IReadOnlyList<McpExclusion> exclusions, CancellationToken cancellationToken = default)
        {
            if (FailChanges) throw new IOException("sensitive details");
            Exclusions.Clear();
            Exclusions.AddRange(exclusions.Select(item => item.Id.Length == 0 ? item with { Id = Guid.NewGuid().ToString("N") } : item));
            return ReadAsync(cancellationToken);
        }
        public Task<string> GetClientConfigurationAsync(string owner, CancellationToken cancellationToken = default)
        { ClientConfigurationReads++; return Task.FromResult("test client configuration"); }
    }
}
