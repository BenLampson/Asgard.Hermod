using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Mcp;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI;
using Asgard.Hermod.UI;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class McpStorageBrowserTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en-US", "Use selected location")]
    [InlineData("zh-CN", "使用所选位置")]
    [InlineData("de-DE", "Use selected location")]
    public Task SelectsNestedFolderAndSavesOnlyItsExactPrefix(string culture, string chooseLabel) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var settings = new SettingsStub();
            using var model = new McpSettingsViewModel(settings, storageBrowser: new BrowserStub());
            await model.RefreshAsync();
            model.SelectedTarget = Assert.Single(model.Targets);
            using var browser = Assert.IsType<McpStorageBrowserViewModel>(model.CreateStorageBrowser());
            await browser.InitializeAsync();
            Assert.False(browser.CanChoose);
            browser.SelectedBucket = Assert.Single(browser.Buckets);
            Assert.True(browser.CanChoose);
            Assert.Contains(Text.Get("McpStorageBucketRoot"), browser.Location);
            Assert.Equal("one/", Assert.Single(browser.Folders).Prefix);
            await browser.LoadMoreCommand.ExecuteAsync();
            Assert.Equal(2, browser.Folders.Count);
            browser.SelectedFolder = browser.Folders[1];
            Assert.Equal("two/", browser.ChosenPrefix);
            Assert.Contains("two/", browser.Location);
            model.ApplyStorageScope(browser);
            Assert.Equal("two/", model.Qualifier);
            await browser.OpenFolderCommand.ExecuteAsync();
            Assert.Equal("two/", browser.Prefix);
            browser.SelectedFolder = Assert.Single(browser.Folders);
            await browser.OpenFolderCommand.ExecuteAsync();
            Assert.Equal("two/nested/", browser.Prefix);
            var dialog = new McpStorageBrowserWindow(browser);
            try
            {
                var choose = Assert.IsType<Button>(dialog.FindName("McpUseStorageLocation"));
                Assert.Equal(chooseLabel, choose.Content);
                model.ApplyStorageScope(browser);
                Assert.Equal("exports", model.Resource);
                Assert.Equal("two/nested/", model.Qualifier);
                Assert.Empty(settings.Exclusions);
                await model.AddExclusionCommand.ExecuteAsync();
                var exclusion = Assert.Single(settings.Exclusions);
                Assert.Equal("exports", exclusion.Resource);
                Assert.Equal("two/nested/", exclusion.Qualifier);
            }
            finally { dialog.Close(); }
            await browser.UpCommand.ExecuteAsync();
            Assert.Equal("two/", browser.Prefix);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    public Task CancelAndChangingTargetNeverSaveOrApplyAStaleChoice() => fixture.RunOffscreenAsync(async () =>
    {
        var settings = new SettingsStub();
        using var model = new McpSettingsViewModel(settings, storageBrowser: new BrowserStub());
        await model.RefreshAsync();
        model.SelectedTarget = Assert.Single(model.Targets);
        using var browser = Assert.IsType<McpStorageBrowserViewModel>(model.CreateStorageBrowser());
        await browser.InitializeAsync();
        browser.SelectedBucket = Assert.Single(browser.Buckets);
        model.BeginExclusionDraft();
        model.ApplyStorageScope(browser);
        Assert.Empty(model.Resource);
        Assert.Equal(Text.Get("McpWorkspaceChanged"), model.Message);
        Assert.Empty(settings.Exclusions);
    });

    [Fact]
    public Task BrowserHidesRawProviderErrors() => fixture.RunOffscreenAsync(async () =>
    {
        var target = new McpTarget("oss", "OSS", "storage", "v1");
        using var browser = new McpStorageBrowserViewModel(new BrowserStub { Fail = true }, "", target);
        await browser.InitializeAsync();
        Assert.Equal(Text.Get("McpStorageBrowseFailed"), browser.Message);
        Assert.DoesNotContain("secret", browser.Message);
        Assert.False(browser.CanChoose);
    });

    [Fact]
    public Task DesktopCompositionProvidesTheStorageBrowserToSettings() => fixture.RunOffscreenAsync(() =>
    {
        using var environment = new LocalStorageTestEnvironment();
        var registrations = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss()
            .AddLocalStorage(environment.CreateConfiguration()).AddSsh().AddHermodMcp("Hermod.Mcp.exe")
            .AddHermodUi(() => null, Application.Current.Resources).AddSshUi(() => null);
        using var provider = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.NotNull(provider.GetRequiredService<IMcpStorageScopeBrowser>());
        Assert.NotNull(provider.GetRequiredService<IMcpApprovalService>());
        Assert.NotNull(provider.GetRequiredService<McpDesktopServer>());
        Assert.NotNull(provider.GetRequiredService<ISettingsEditor>());
        return Task.CompletedTask;
    });

    [Fact]
    public Task ExclusionDialogSavesOnlyOnConfirmation() => fixture.RunAsync(async () =>
    {
        var settings = new SettingsStub();
        using var model = new McpSettingsViewModel(settings, storageBrowser: new BrowserStub());
        await model.RefreshAsync();
        var cancelled = new McpExclusionWindow(model);
        bool? cancelResult = await ModalWindowTestInteraction.ShowAsync(cancelled, () =>
        {
            model.SelectedTarget = Assert.Single(model.Targets);
            model.Resource = "exports";
            cancelled.Close();
            return Task.CompletedTask;
        });
        Assert.NotEqual(true, cancelResult);
        Assert.Empty(settings.Exclusions);
        model.BeginExclusionDraft();
        Assert.Null(model.SelectedTarget);

        var dialog = new McpExclusionWindow(model);
        bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
        {
            model.SelectedTarget = Assert.Single(model.Targets);
            model.Resource = "exports"; model.Qualifier = "reports/";
            await model.AddExclusionCommand.ExecuteAsync();
        });
        Assert.True(result);
        Assert.Equal("reports/", Assert.Single(settings.Exclusions).Qualifier);
    });

    [Fact]
    public Task BothDialogsRemainUsableAtNarrowSizes() => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        themes.SetTheme(UiTheme.Dark);
        var settings = new SettingsStub();
        using var model = new McpSettingsViewModel(settings, storageBrowser: new BrowserStub());
        await model.RefreshAsync(); model.SelectedTarget = Assert.Single(model.Targets);
        var grant = new McpExclusionWindow(model);
        try
        {
            WindowPreviewCapture.Show(grant);
            await WindowPreviewCapture.VerifyAsync(grant, "mcp-grant");
            grant.Width = grant.MinWidth; grant.Height = grant.MinHeight;
            await WindowPreviewCapture.VerifyAsync(grant, "mcp-grant-narrow");
            var browse = Assert.IsType<Button>(grant.FindName("McpExclusionBrowseStorage"));
            Assert.True(browse.IsEnabled);
            browse.BringIntoView(); await WindowTestVisuals.UpdateLayoutAsync(grant);
            Assert.True(browse.Focus());
        }
        finally { grant.Close(); }

        using var browser = Assert.IsType<McpStorageBrowserViewModel>(model.CreateStorageBrowser());
        var dialog = new McpStorageBrowserWindow(browser);
        try
        {
            WindowPreviewCapture.Show(dialog);
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            browser.SelectedBucket = Assert.Single(browser.Buckets);
            await WindowPreviewCapture.VerifyAsync(dialog, "mcp-browse");
            dialog.Width = dialog.MinWidth; dialog.Height = dialog.MinHeight;
            await WindowPreviewCapture.VerifyAsync(dialog, "mcp-browse-narrow");
            var picker = Assert.IsType<ComboBox>(dialog.FindName("McpBucketPicker"));
            var folders = Assert.IsType<ListBox>(dialog.FindName("McpFolderList"));
            Assert.True(folders.ActualHeight >= 80);
            Assert.Same(Application.Current.FindResource("AppBackgroundBrush"), folders.Background);
            browser.SelectedFolder = Assert.Single(browser.Folders);
            Assert.Equal("one/", browser.ChosenPrefix);
            var reset = Assert.IsType<Button>(dialog.FindName("McpSelectCurrent"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(browser.ChosenPrefix);
            Assert.True(picker.Focus());
            Assert.True(picker.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
            var current = Assert.IsType<Button>(dialog.FindName("McpSelectCurrent"));
            Assert.Same(current, System.Windows.Input.Keyboard.FocusedElement);
            Assert.True(current.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
            Assert.Same(folders.ItemContainerGenerator.ContainerFromIndex(0), System.Windows.Input.Keyboard.FocusedElement);
        }
        finally { dialog.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private sealed class BrowserStub : IMcpStorageScopeBrowser
    {
        public bool Fail { get; init; }
        public Task<IReadOnlyList<McpStorageBucket>> ListBucketsAsync(string owner, McpTarget target, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("secret");
            return Task.FromResult<IReadOnlyList<McpStorageBucket>>([new("exports", "region")]);
        }
        public Task<McpStorageFolderPage> ListFoldersAsync(string owner, McpTarget target, McpStorageBucket bucket, string prefix,
            string? continuationToken = null, CancellationToken cancellationToken = default) => Task.FromResult((prefix, continuationToken) switch
            {
                ("", null) => new McpStorageFolderPage(["one/"], "next"),
                ("", "next") => new McpStorageFolderPage(["two/"], null),
                ("two/", _) => new McpStorageFolderPage(["two/nested/"], null),
                _ => new McpStorageFolderPage([], null),
            });
    }

    private sealed class SettingsStub : IMcpSettingsService
    {
        private static readonly McpWorkspace Workspace = new("", [new("oss", "OSS", "storage", "v1")]);
        public List<McpExclusion> Exclusions { get; } = [];
        public Task<McpSettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new McpSettingsSnapshot(Workspace, false, [], Exclusions.ToArray()));
        public Task<McpSettingsSnapshot> AddGrantAsync(string owner, McpGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> SetEnabledAsync(string owner, bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> RemoveGrantAsync(string owner, string grantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> SetInstanceApprovalAsync(string owner, McpInstanceApproval approval, bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<McpSettingsSnapshot> SetExclusionsAsync(string owner, IReadOnlyList<McpExclusion> exclusions, CancellationToken cancellationToken = default)
        {
            Exclusions.Clear();
            Exclusions.AddRange(exclusions.Select(item => item.Id.Length == 0 ? item with { Id = "exclusion" } : item));
            return ReadAsync(cancellationToken);
        }
        public Task<string> GetClientConfigurationAsync(string owner, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
