using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class McpApprovalWindowTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en-US", "Confirm MCP access")]
    [InlineData("zh-CN", "确认 MCP 访问")]
    [InlineData("de-DE", "Confirm MCP access")]
    public Task ConfirmationShowsTheInstanceAndLocationAndRequiresTheDownloadFolder(string culture, string title) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var request = new McpApprovalRequest("Storage One", "Object storage", "Download", [new("Bucket", "exports"), new("Path", "daily/report.txt")],
                RequiresDownloadFolder: true, DownloadFileName: "report.txt");
            var model = new McpApprovalViewModel(request);
            var window = new McpApprovalWindow(model);
            try
            {
                WindowPreviewCapture.Show(window);
                await WindowPreviewCapture.VerifyAsync(window, "mcp-approval-download-" + culture);
                Assert.Equal(title, window.Title);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text == "Storage One");
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text == "daily/report.txt");
                var approve = Assert.IsType<Button>(window.FindName("McpApprove"));
                model.DownloadFolder = "";
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.False(approve.IsEnabled);
                model.DownloadFolder = @"D:\Downloads";
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.True(approve.IsEnabled);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text == @"D:\Downloads");
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                await WindowPreviewCapture.VerifyAsync(window, "mcp-approval-narrow-" + culture);
            }
            finally { window.Close(); }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    public Task FirstStorageAccessShowsAnEditableDefaultDownloadFolder() => fixture.RunAsync(async () =>
    {
        string folder = Path.GetTempPath();
        var request = new McpApprovalRequest("Storage One", "Object storage", "List buckets", [],
            SuggestedDownloadFolder: folder, ShowDownloadFolder: true);
        var automatic = new McpApprovalViewModel(request with { SuggestedDownloadFolder = "" });
        Assert.True(Directory.Exists(automatic.DownloadFolder));
        var model = new McpApprovalViewModel(request);
        var window = new McpApprovalWindow(model);
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, "mcp-approval-instance");
            Assert.True(model.CanApprove);
            Assert.True(model.ShowDownloadFolder);
            Assert.Equal(folder, model.DownloadFolder);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text == folder);
            Assert.True(Assert.IsType<Button>(window.FindName("McpApprovalChooseFolder")).IsVisible);
            model.DownloadFolder = "";
            Assert.False(model.CanApprove);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MinimizedOwnerLeavesThePromptAccessibleFromTheTaskbar() => fixture.RunAsync(async () =>
    {
        var parent = new HermodWindow();
        WindowPreviewCapture.Show(parent);
        parent.WindowState = WindowState.Minimized;
        var shown = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new WindowsMcpApprovalService(() => parent)
        {
            PresentWindow = window =>
            {
                window.ShowActivated = false;
                window.Opacity = 0;
                window.Show();
                shown.SetResult(window);
            },
        };
        try
        {
            var pending = service.ConfirmAsync(new("Instance", "Storage", "Read", []), TestContext.Current.CancellationToken);
            var prompt = await shown.Task.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                Assert.Null(prompt.Owner);
                Assert.Equal(WindowStartupLocation.CenterScreen, prompt.WindowStartupLocation);
                Assert.True(prompt.ShowInTaskbar);
                Assert.True(prompt.IsVisible);
            }
            finally { prompt.Close(); }
            Assert.False((await pending).Approved);
        }
        finally { parent.Close(); }
    });

    [Fact]
    public Task FailedPresentationReturnsAConfirmationErrorAndReleasesTheWindow() => fixture.RunOffscreenAsync(async () =>
    {
        var service = new WindowsMcpApprovalService(() => null) { PresentWindow = _ => throw new InvalidOperationException("sensitive details") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmAsync(new("Instance", "Storage", "Read", []), TestContext.Current.CancellationToken));
        Assert.Equal(Text.Get("McpApprovalUnavailable"), Text.Error(error));
        Assert.Empty(Application.Current.Windows.OfType<McpApprovalWindow>());
    });

    [Fact]
    public Task CancellationClosesThePendingWindowAndNeverApproves() => fixture.RunAsync(async () =>
    {
        var service = new WindowsMcpApprovalService(() => null)
        {
            PresentWindow = window => WindowPreviewCapture.Show((HermodWindow)window),
        };
        using var cancelled = new CancellationTokenSource();
        var request = new McpApprovalRequest("Instance", "Storage", "Read", [new("Path", "reports/")]);
        Task<McpApprovalResult> pending = service.ConfirmAsync(request, cancelled.Token);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Single(Application.Current.Windows.OfType<McpApprovalWindow>());
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Empty(Application.Current.Windows.OfType<McpApprovalWindow>());
    });
}
