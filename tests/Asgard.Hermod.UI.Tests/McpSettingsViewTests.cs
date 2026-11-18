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
            var guide = new McpClientGuideWindow(model);
            try
            {
                var prompt = Assert.IsType<Button>(guide.FindName("McpCopyPromptButton"));
                Assert.Equal(copyLabel, prompt.Content);
                await model.CopyPromptCommand.ExecuteAsync();
                Assert.Equal(Text.Get("McpStarterPrompt"), copied);
                Assert.Equal(0, settings.ClientConfigurationReads);
            }
            finally { guide.Close(); }
            Assert.All(model.InstanceAccess, item => Assert.Equal(McpInstancePolicy.Confirm, item.Policy));
            var instance = model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage");
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(page), control => control.DataContext == instance && control.ContextMenu is not null);
            Assert.Equal(Text.Format("McpPolicyForInstance", instance.Target.Label, instance.PolicyLabel), System.Windows.Automation.AutomationProperties.GetName(button));
            await instance.SetPolicyCommand.ExecuteAsync(McpInstancePolicy.AlwaysAllow);
            Assert.True(Assert.Single(settings.Approvals).Matches(instance.Target.Target));
            await model.RefreshAsync();
            Assert.Equal(McpInstancePolicy.AlwaysAllow, model.VisibleInstances[0].Policy);
            model.SelectedFilter = model.Filters.Single(filter => filter.Policy == McpInstancePolicy.AlwaysAllow);
            model.Search = "sToRaGe";
            instance = Assert.Single(model.VisibleInstances);
            await instance.SetPolicyCommand.ExecuteAsync(McpInstancePolicy.Blocked);
            Assert.Empty(settings.Approvals);
            Assert.Equal("oss", Assert.Single(settings.Exclusions).InstanceId);
            Assert.Empty(model.VisibleInstances);
            Assert.True(model.NoMatches);
            model.SelectedFilter = model.Filters.Single(filter => filter.Policy == McpInstancePolicy.Blocked);
            await Assert.Single(model.VisibleInstances).SetPolicyCommand.ExecuteAsync(McpInstancePolicy.Confirm);
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
        await instance.SetPolicyCommand.ExecuteAsync(McpInstancePolicy.Blocked);
        Assert.Equal(McpInstancePolicy.Confirm, instance.Policy);
        Assert.Empty(settings.Exclusions);
        await instance.SetPolicyCommand.ExecuteAsync(McpInstancePolicy.AlwaysAllow);
        Assert.Equal(McpInstancePolicy.Confirm, instance.Policy);
        Assert.Empty(settings.Approvals);
        Assert.Equal(Text.Get("McpSettingsFailed"), model.Message);
    });

    [Fact]
    public Task EmptyStorageFieldsDoNotSilentlyWidenTheBlock() => fixture.RunOffscreenAsync(async () =>
    {
        using var model = new McpSettingsViewModel(new SettingsStub());
        await model.RefreshAsync();
        model.SelectedTarget = model.Targets.Single(item => item.Target.TypeId == "storage");
        Assert.False(model.AddExclusionCommand.CanExecute(null));
        model.Resource = "exports";
        Assert.False(model.AddExclusionCommand.CanExecute(null));
        model.Qualifier = "private/";
        Assert.Contains("private/", model.ScopePreview);
        model.StorageRange = McpStorageExclusionRange.Bucket;
        Assert.Empty(model.Qualifier);
        Assert.True(model.AddExclusionCommand.CanExecute(null));
        model.StorageRange = McpStorageExclusionRange.Instance;
        Assert.Empty(model.Resource);
        Assert.True(model.AddExclusionCommand.CanExecute(null));
        model.StorageRange = McpStorageExclusionRange.Prefix;
        Assert.False(model.AddExclusionCommand.CanExecute(null));
    });

    [Fact]
    public Task DefaultAccessAndExclusionDialogsRemainUsableAfterResize() => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        var themes = new Asgard.Hermod.UI.Themes.ThemeService(Application.Current.Resources);
        themes.SetTheme(UiTheme.Dark);
        var settings = new SettingsStub
        {
            Workspace = new("", [new("oss", "Documents", "storage", "v1"), new("cache", "Session cache", "redis", "v1"),
                .. Enumerable.Range(1, 12).Select(index => new McpTarget($"sql-{index}", $"Reporting {index}", "postgresql", "v1"))]),
        };
        using var shell = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(),
            languageStore: new LanguagePreferenceTestStore(), mcp: settings);
        await shell.InitializeAsync();
        shell.SelectedCategory = shell.Categories.Single(category => category.Id == "mcp");
        var model = Assert.IsType<McpSettingsViewModel>(shell.Mcp);
        var host = new SettingsWindow(shell);
        try
        {
            WindowPreviewCapture.Show(host);
            await WindowTestVisuals.UpdateLayoutAsync(host);
            var page = Assert.Single(WindowTestVisuals.Descendants<McpSettingsView>(host));
            var row = model.InstanceAccess.Single(item => item.Target.Target.TypeId == "storage");
            var policy = Assert.Single(WindowTestVisuals.Descendants<Button>(page), button => button.DataContext == row && button.ContextMenu is not null);
            policy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(policy.ContextMenu);
            Assert.True(menu.IsOpen);
            var allow = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.CommandParameter, McpInstancePolicy.AlwaysAllow));
            Assert.Empty(settings.Approvals);
            Assert.Equal(row.SetPolicyCommand, allow.Command);
            await row.SetPolicyCommand.ExecuteAsync(allow.CommandParameter);
            menu.IsOpen = false;
            Assert.Single(settings.Approvals);
            await WindowPreviewCapture.VerifyAsync(host, "mcp-default-settings");
            host.Width = host.MinWidth; host.Height = host.MinHeight;
            await WindowPreviewCapture.VerifyAsync(host, "mcp-compact-settings");
            var search = Assert.IsType<TextBox>(page.FindName("McpSearch"));
            Assert.True(search.Focus());
            Assert.True(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            var add = Assert.IsType<Button>(page.FindName("McpOpenExclusion"));
            Assert.True(add.IsEnabled);
            Assert.InRange(add.TranslatePoint(new(), page).Y + add.ActualHeight, 1, page.ActualHeight);
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
        internal McpWorkspace Workspace { get; init; } = new("", [new("cache", "Cache", "redis", "v1"),
            new("sql", "SQL", "mysql", "v1"), new("oss", "OSS", "storage", "v1")]);
        private bool _enabled;
        internal bool FailChanges { get; init; }
        internal List<McpExclusion> Exclusions { get; } = [];
        internal List<McpInstanceApproval> Approvals { get; } = [];
        internal int ClientConfigurationReads { get; private set; }
        public Task<McpSettingsSnapshot> SetInstancePolicyAsync(string owner, McpInstanceApproval approval, McpInstancePolicy selection, CancellationToken cancellationToken = default)
        {
            if (FailChanges) throw new IOException("sensitive details");
            Exclusions.RemoveAll(item => item.InstanceId == approval.InstanceId && item.Resource.Length == 0);
            Approvals.RemoveAll(item => item.InstanceId == approval.InstanceId);
            if (selection == McpInstancePolicy.AlwaysAllow) Approvals.Add(approval);
            if (selection == McpInstancePolicy.Blocked) Exclusions.Add(new("block", approval.InstanceId, approval.TypeId));
            return ReadAsync(cancellationToken);
        }
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
