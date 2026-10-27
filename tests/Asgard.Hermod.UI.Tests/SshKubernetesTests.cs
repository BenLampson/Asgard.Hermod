using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>kubectl 管理通过内存 SSH 通道验证范围、确认、失效与真实 WPF 绑定，不连接集群。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshKubernetesTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task DetectionQueriesAndCommandsUseExplicitScopeAndLocalizedViews(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        using var bindings = new WpfBindingTrace();
        var connection = Connection(); var dialogs = new Dialogs();
        using var model = new SshKubernetesViewModel(() => connection, () => "ops@example.test", dialogs);
        var view = new SshKubernetesView { DataContext = model };
        try
        {
            await model.InitializeAsync(); Assert.True(model.IsInstalled); Assert.Equal("production", model.Context); Assert.Contains("v1.35.0", model.EnvironmentSummary);
            model.Namespace = "app"; await model.RefreshCommand.ExecuteAsync();
            Assert.Equal("web-1", Assert.Single(model.Items).Name); Assert.Equal("Running", model.Items[0].Status);
            Assert.Contains("app", model.Namespaces);
            model.SelectedItem = model.Items[0]; await model.ActionCommand.ExecuteAsync("logs");
            Assert.Equal("application log output", model.Output); Assert.Equal(0, dialogs.Count);
            await Layout(view, 860, 650);
            var outputSearch = Assert.IsType<TextBox>(view.FindName("OutputSearch")); outputSearch.Text = "log";
            Assert.Equal("log", Assert.IsType<TextBox>(view.FindName("ResultOutput")).SelectedText); outputSearch.Text = "";
            Assert.Contains("--context 'production'", connection.Commands.Last()); Assert.Contains("--namespace 'app'", connection.Commands.Last());
            string target = model.ResultTarget;
            model.Resource = "deployments"; Assert.NotEmpty(model.Items); Assert.Null(model.SelectedItem); Assert.False(model.ActionCommand.CanExecute("logs"));
            await model.RefreshCommand.ExecuteAsync(); model.SelectedItem = model.Items[0];
            await model.ActionCommand.ExecuteAsync("restart"); Assert.Equal(1, dialogs.Count);
            Assert.DoesNotContain(connection.Commands, command => command.Contains("rollout restart", StringComparison.Ordinal));
            dialogs.Approved = true; await model.ActionCommand.ExecuteAsync("restart");
            Assert.Single(connection.Commands, command => command.Contains("rollout restart deployment/web", StringComparison.Ordinal));
            Assert.NotEqual(target, model.ResultTarget); Assert.Contains("app", dialogs.Detail); Assert.Contains("production", dialogs.Detail);
            foreach ((double width, double height) in new[] { (1388d, 820d), (860d, 650d), (600d, 480d) })
            {
                await Layout(view, width, height);
                var table = Assert.IsType<DataGrid>(view.FindName("ResourceTable")); Assert.True(table.ActualHeight >= 32);
                Assert.Same(view.FindResource("MonitorGrid"), table.Style);
                var context = Assert.IsType<ComboBox>(view.FindName("ContextPicker"));
                Assert.InRange(context.TranslatePoint(new(), view).X + context.ActualWidth, 1, width);
                Capture(view, $"kubectl-{language}-{theme}-{width}");
            }
            Assert.Empty(bindings.Messages); Assert.Empty(model.Error);
            Assert.Null(PresentationSource.FromVisual(view));
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });

    [Fact]
    public Task MissingClientAndPermissionErrorsNeverExposeRemoteFailureText() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); using var model = new SshKubernetesViewModel(() => connection, () => "host", new Dialogs());
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("private credential server response", 127, false));
        await model.InitializeAsync(); Assert.False(model.IsInstalled); Assert.Equal(Text.Get("KubeMissing"), model.EnvironmentSummary);
        Assert.False(model.RefreshCommand.CanExecute(null)); Assert.DoesNotContain("private", model.EnvironmentSummary);
        connection.Handler = Handle; await model.DetectCommand.ExecuteAsync();
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("private bearer token", 1, false));
        await model.RefreshCommand.ExecuteAsync(); Assert.Equal(Text.Get("KubeQueryFailed"), model.Error); Assert.Empty(model.Output); Assert.Empty(model.Items);
        Assert.DoesNotContain("private", model.Error);
    });

    [Fact]
    public Task DisconnectDuringConfirmationAndLateQueryCannotActOnNewConnection() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); var dialogs = new Dialogs();
        ISshWorkspaceConnection? current = connection; using var model = new SshKubernetesViewModel(() => current, () => "host", dialogs);
        await model.InitializeAsync(); model.Resource = "deployments"; await model.RefreshCommand.ExecuteAsync(); model.SelectedItem = model.Items[0];
        dialogs.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var restart = model.ActionCommand.ExecuteAsync("restart"); Assert.True(model.IsBusy);
        current = Connection(); model.ConnectionChanged(); dialogs.Pending.SetResult(true); await restart;
        Assert.Empty(model.Items); Assert.False(model.IsInstalled);
        Assert.DoesNotContain(connection.Commands, command => command.Contains("rollout restart", StringComparison.Ordinal));
        current = connection; connection.Handler = Handle; await model.InitializeAsync();
        var pending = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Handler = (_, _) => pending.Task;
        var query = model.RefreshCommand.ExecuteAsync(); model.ConnectionChanged();
        pending.SetResult(new(ListJson("Pod", "web-1"), 0, false)); await query;
        Assert.Empty(model.Items); Assert.Empty(model.Contexts); Assert.Empty(model.Output);
    });

    [Fact]
    public Task ScopeChangeDiscardsLateResultsAndCanceledRestartReportsUnknownOutcome() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); using var model = new SshKubernetesViewModel(() => connection, () => "host", new Dialogs { Approved = true });
        await model.InitializeAsync();
        var pending = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Handler = (_, _) => pending.Task;
        model.SelectedItem = model.Items[0];
        var query = model.ActionCommand.ExecuteAsync("describe"); model.Namespace = "different";
        pending.SetResult(new("old details", 0, false)); await query;
        Assert.Empty(model.Items);
        connection.Handler = Handle; model.Namespace = "app"; model.Resource = "deployments"; model.SelectedItem = model.Items[0];
        connection.Handler = (_, _) => new TaskCompletionSource<SshCommandResult>().Task;
        var restart = model.ActionCommand.ExecuteAsync("restart"); model.CancelCommand.Execute(null); await restart;
        Assert.Equal(Text.Get("KubeOutcomeUnknown"), model.Error);
        Assert.Single(connection.Commands, command => command.Contains("rollout restart", StringComparison.Ordinal));
    });

    internal static SshWorkspaceTestConnection Connection() => new() { Handler = Handle };
    private static Task<SshCommandResult> Handle(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string output = command switch
        {
            SshKubernetesCommands.Probe => """{"clientVersion":{"gitVersion":"v1.35.0"}}""",
            SshKubernetesCommands.Contexts => "staging\nproduction\n",
            SshKubernetesCommands.CurrentContext => "production\n",
            _ when command.Contains("config view --minify", StringComparison.Ordinal) => "app",
            _ when command.EndsWith("get namespaces -o json", StringComparison.Ordinal) => ListJson("Namespace", "app"),
            _ when command.Contains(" get deployments ", StringComparison.Ordinal) => ListJson("Deployment", "web"),
            _ when command.Contains(" get pods ", StringComparison.Ordinal) => ListJson("Pod", "web-1"),
            _ when command.Contains(" get services ", StringComparison.Ordinal) => ListJson("Service", "web"),
            _ when command.Contains(" get nodes ", StringComparison.Ordinal) => ListJson("Node", "worker"),
            _ when command.Contains(" get events ", StringComparison.Ordinal) => ListJson("Event", "web-started"),
            _ => "application log output",
        };
        return Task.FromResult(new SshCommandResult(output, 0, false));
    }
    private static string ListJson(string kind, string name) => $$$"""
        {"items":[{"kind":"{{{kind}}}","metadata":{"name":"{{{name}}}","namespace":"app"},"status":{"phase":"Running","replicas":2,"readyReplicas":2}}]}
        """;
    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        internal bool Approved;
        internal int Count;
        internal string Detail = "";
        internal TaskCompletionSource<bool>? Pending;
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview) { Count++; Detail = detail; return Pending?.Task ?? Task.FromResult(Approved); }
    }
    internal static async Task Layout(FrameworkElement view, double width, double height)
    { view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout(); }
    internal static void Capture(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
