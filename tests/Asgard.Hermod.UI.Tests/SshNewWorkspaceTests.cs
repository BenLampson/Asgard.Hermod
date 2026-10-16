using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证新工作区的真实状态、分屏、断线保护和四种语言主题组合。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshNewWorkspaceTests(WpfTestFixture fixture)
{
    /// <summary>收起保留控件、宽度与草稿；迟到的模型回复不能重新展开 Agent。</summary>
    [Fact]
    public Task HiddenAgentKeepsDraftAndNeverReopensForReply() => fixture.RunOffscreenAsync(async () =>
    {
        var client = new AgentClient(); var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(new SshWorkspaceTestConnection()) }; var dialogs = new SshTestDialogs();
        await using var model = new SshWorkspaceViewModel(new(new("ssh", "SSH", "SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, dialogs, dialogs, client);
        await model.InitializeAsync();
        Assert.False(model.IsAgentOpen); model.ToggleAgentCommand.Execute(null); model.Assistant.Prompt = "diagnose";
        var sending = model.Assistant.SendCommand.ExecuteAsync(); model.Assistant.Prompt = "next draft";
        model.ToggleAgentCommand.Execute(null); client.Completion.SetResult(AgentReply.FromContent("Try this.\n```bash\nuname -a\n```")); await sending;
        Assert.False(model.IsAgentOpen); Assert.Equal("next draft", model.Assistant.Prompt); Assert.Equal(2, model.Assistant.Messages.Count);
        var pane = new Border(); pane.SetBinding(FrameworkElement.DataContextProperty, new Binding("Assistant"));
        var split = new WorkspaceSplitView { MainContent = new Border(), PaneContent = pane, IsPaneOpen = true, PaneWidth = 350, DataContext = model };
        Measure(split, 1100, 600); Assert.InRange(split.ColumnDefinitions[2].ActualWidth, 349, 351);
        Assert.Same(model.Assistant, pane.DataContext);
        split.ColumnDefinitions[2].Width = new GridLength(450); Assert.Equal(450, split.PaneWidth);
        split.IsPaneOpen = false; Measure(split, 1100, 600); Assert.Equal(0, split.ColumnDefinitions[2].ActualWidth);
        split.IsPaneOpen = true; Measure(split, 1100, 600); Assert.Same(pane, split.PaneContent); Assert.Equal(450, split.PaneWidth);
    });

    /// <summary>取消删除不调用远端；重连后的旧命令不可执行。</summary>
    [Fact]
    public Task CanceledDeleteAndStaleAgentSuggestionCannotMutateServer() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs();
        using var files = new SshFilesViewModel(() => connection, dialogs);
        await files.RefreshCommand.ExecuteAsync(); files.Selected = files.Entries[1];
        await files.DeleteCommand.ExecuteAsync(); Assert.Empty(connection.Deleted);
        dialogs.Accept = true; await files.DeleteCommand.ExecuteAsync(); Assert.Single(connection.Deleted);
        using var assistant = new SshAssistantViewModel(null, () => connection, () => "host", dialogs);
        var original = new SshAssistantCommand("uname -a", 0); Assert.True(assistant.ExecuteCommand.CanExecute(original));
        assistant.ConnectionChanged(); Assert.False(assistant.ExecuteCommand.CanExecute(original));
        await assistant.ExecuteCommand.ExecuteAsync(original); Assert.Empty(connection.Commands);
    });

    /// <summary>完整主机命令与文件名不会作为未知异常展示给用户。</summary>
    [Fact]
    public Task FileFailuresUseSafeLocalizedMessages() => fixture.RunOffscreenAsync(async () =>
    {
        using var files = new SshFilesViewModel(() => null, new Dialogs());
        await files.RefreshCommand.ExecuteAsync(); Assert.Equal(Text.Get("SshWorkspaceUnavailable"), files.Error);
    });

    /// <summary>下载失败不能截断原目标，安全错误也不能泄露远端异常原文。</summary>
    [Fact]
    public Task FailedDownloadPreservesExistingFileAndRemovesTemporaryFile() => fixture.RunOffscreenAsync(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "hermod-ssh-download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "existing.txt"); await File.WriteAllTextAsync(target, "keep original");
        try
        {
            var connection = new SshWorkspaceTestConnection { DownloadHandler = async (stream, token) => { await stream.WriteAsync("partial"u8.ToArray(), token); throw new IOException("private credential must never appear"); } };
            using var files = new SshFilesViewModel(() => connection, new Dialogs { DownloadTarget = target });
            await files.RefreshCommand.ExecuteAsync(); files.Selected = files.Entries[1]; await files.DownloadCommand.ExecuteAsync();
            Assert.Equal("keep original", await File.ReadAllTextAsync(target)); Assert.Single(Directory.GetFiles(directory));
            Assert.Equal("SshTransferFailed", Assert.Single(files.Transfers).StatusKey); Assert.Equal(Text.Get("SshFilesFailed"), files.Error);
        }
        finally { File.Delete(target); Directory.Delete(directory); }
    });

    private static void Measure(FrameworkElement element, double width, double height) { element.Measure(new(width, height)); element.Arrange(new(0, 0, width, height)); element.UpdateLayout(); }
    private sealed class AgentClient : IAgentClient
    {
        internal TaskCompletionSource<AgentReply> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => Completion.Task;
    }
    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        internal bool Accept { get; set; }
        internal string? DownloadTarget { get; set; }
        public string? ChooseDownload(string name) => DownloadTarget;
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(Accept);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview) => Task.FromResult(Accept);
    }
}
