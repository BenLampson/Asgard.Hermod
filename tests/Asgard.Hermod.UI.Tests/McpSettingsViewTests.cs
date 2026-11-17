using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class McpSettingsViewTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en-US", "Select an instance", "AI tools (MCP)", "Copy prompt")]
    [InlineData("zh-CN", "选择实例", "AI 工具（MCP）", "复制提示词")]
    [InlineData("de-DE", "Select an instance", "AI tools (MCP)", "Copy prompt")]
    public Task FormAdaptsToTheSelectedConnectorAndKeepsContentOffByDefault(string culture, string pickerLabel, string title, string copyLabel) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settings = new SettingsStub();
            string? copy = null;
            using var model = new McpSettingsViewModel(settings, text => copy = text);
            await model.RefreshAsync(TestContext.Current.CancellationToken);
            var view = new McpSettingsView { DataContext = model };
            view.Measure(new(420, 550)); view.Arrange(new(0, 0, 420, 550)); view.UpdateLayout();
            var picker = Assert.IsType<ComboBox>(view.FindName("McpTargetPicker"));
            var share = Assert.IsType<CheckBox>(view.FindName("McpShareContent"));
            Assert.Equal(pickerLabel, System.Windows.Automation.AutomationProperties.GetName(picker));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.Text == title);
            Assert.False(model.CopyCommand.CanExecute(null));
            var copyPrompt = Assert.IsType<Button>(view.FindName("McpCopyPromptButton"));
            Assert.Equal(copyLabel, copyPrompt.Content);
            Assert.True(copyPrompt.IsEnabled);
            var promptCommand = Assert.IsType<Asgard.Hermod.UI.Common.AsyncRelayCommand>(copyPrompt.Command);
            await promptCommand.ExecuteAsync();
            Assert.Equal(Text.Get("McpStarterPrompt"), copy);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.Text == copy);
            Assert.Equal(Text.Get("McpPromptCopied"), model.Message);
            Assert.Equal(0, settings.ClientConfigurationReads);
            picker.SelectedItem = model.Targets.Single(target => target.Target.TypeId == "redis");
            Assert.Equal(Visibility.Collapsed, share.Visibility);
            Assert.Equal("0", model.Resource);
            picker.SelectedItem = model.Targets.Single(target => target.Target.TypeId == "mysql");
            Assert.Equal(Visibility.Visible, share.Visibility);
            Assert.False(share.IsChecked);

            model.Resource = "orders";
            model.ShareContent = true;
            Assert.False(model.GrantCommand.CanExecute(null));
            model.Table = "exports";
            await model.GrantCommand.ExecuteAsync();
            Assert.True(Assert.Single(settings.Grants).ShareContent);
            Assert.Equal("exports", model.Table);
            Assert.Equal("sql", model.SelectedTarget?.Target.Id);
            await model.ToggleCommand.ExecuteAsync();
            await model.CopyCommand.ExecuteAsync();
            Assert.Equal("test client configuration", copy);
            Assert.Equal(1, settings.ClientConfigurationReads);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Theory]
    [InlineData("en-US", "Choose local folder")]
    [InlineData("zh-CN", "选择本地文件夹")]
    [InlineData("de-DE", "Choose local folder")]
    public Task TransferGrantRequiresExplicitDirectionPrefixAndFolder(string culture, string folderLabel) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settings = new SettingsStub();
            using var model = new McpSettingsViewModel(settings);
            await model.RefreshAsync(TestContext.Current.CancellationToken);
            var view = new McpSettingsView { DataContext = model };
            view.Measure(new(420, 550)); view.Arrange(new(0, 0, 420, 550)); view.UpdateLayout();
            var download = Assert.IsType<CheckBox>(view.FindName("McpAllowDownload"));
            var upload = Assert.IsType<CheckBox>(view.FindName("McpAllowUpload"));
            var rootPanel = Assert.IsType<StackPanel>(view.FindName("McpTransferRootPanel"));
            var folder = Assert.IsType<Button>(view.FindName("McpSelectTransferFolder"));
            model.SelectedTarget = model.Targets.Single(item => item.Target.TypeId == "storage");
            Assert.False(download.IsChecked);
            Assert.False(upload.IsChecked);
            Assert.Equal(Visibility.Collapsed, rootPanel.Visibility);
            model.Resource = "exports";
            download.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            Assert.True(model.AllowDownload);
            Assert.Equal(Visibility.Visible, rootPanel.Visibility);
            Assert.Equal(folderLabel, folder.Content);
            Assert.True(folder.Focusable);
            Assert.False(model.GrantCommand.CanExecute(null));
            model.Qualifier = "test/";
            Assert.False(model.GrantCommand.CanExecute(null));
            model.TransferRoot = @"D:\McpDownloads";
            Assert.True(model.GrantCommand.CanExecute(null));
            await model.GrantCommand.ExecuteAsync();
            var saved = Assert.Single(settings.Grants);
            Assert.True(saved.AllowDownload);
            Assert.False(saved.AllowUpload);
            Assert.Equal(@"D:\McpDownloads", saved.TransferRoot);
            model.SelectedTarget = model.Targets.Single(item => item.Target.TypeId == "mysql");
            Assert.False(model.AllowDownload);
            Assert.Empty(model.TransferRoot);
            Assert.Equal(Visibility.Collapsed, rootPanel.Visibility);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    public Task TransferFolderPickerRemainsReachableAfterResize() => fixture.RunAsync(async () =>
    {
        using var model = new McpSettingsViewModel(new SettingsStub());
        await model.RefreshAsync(TestContext.Current.CancellationToken);
        model.SelectedTarget = model.Targets.Single(item => item.Target.TypeId == "storage");
        model.AllowDownload = true;
        var view = new McpSettingsView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 900, Height = 700 };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Width = 430;
            window.Height = 480;
            var upload = Assert.IsType<CheckBox>(view.FindName("McpAllowUpload"));
            var folder = Assert.IsType<Button>(view.FindName("McpSelectTransferFolder"));
            upload.BringIntoView();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(upload.Focus());
            Assert.True(upload.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.Same(folder, Keyboard.FocusedElement);
            Assert.InRange(folder.ActualWidth, 1, view.ActualWidth);
        }
        finally { window.Close(); }
    });

    private sealed class SettingsStub : IMcpSettingsService
    {
        private static readonly McpWorkspace Workspace = new("", [new("cache", "Cache", "redis", "v1"),
            new("sql", "SQL", "mysql", "v1"), new("oss", "OSS", "storage", "v1")]);
        private bool _enabled;
        internal List<McpGrant> Grants { get; } = [];
        internal int ClientConfigurationReads { get; private set; }
        public Task<McpSettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new McpSettingsSnapshot(Workspace, _enabled, Grants.ToArray()));
        public Task<McpSettingsSnapshot> SetEnabledAsync(string owner, bool enabled, CancellationToken cancellationToken = default) { _enabled = enabled; return ReadAsync(cancellationToken); }
        public Task<McpSettingsSnapshot> AddGrantAsync(string owner, McpGrant grant, CancellationToken cancellationToken = default) { Grants.Add(grant with { Id = "grant" }); return ReadAsync(cancellationToken); }
        public Task<McpSettingsSnapshot> RemoveGrantAsync(string owner, string grantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetClientConfigurationAsync(string owner, CancellationToken cancellationToken = default)
        {
            ClientConfigurationReads++;
            return Task.FromResult("test client configuration");
        }
    }
}
