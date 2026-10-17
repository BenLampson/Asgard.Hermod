using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>复制完整回答与执行建议分离；离屏验证实际模板绑定，不读写用户剪贴板。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentMessageCopyHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SshAnswerCopyRetainsFencesAndCommandsMovedToSuggestionCards(bool includeDescription) => fixture.RunOffscreenAsync(async () =>
    {
        string source = (includeDescription ? "Review this command before running it.\n\n" : "") + "```bash\nprintf '%s' '<literal>'; echo \"a,b\";\n```";
        using var model = new SshAssistantViewModel(null, () => null, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs());
        var message = new SshAssistantMessage(source, false, 0); model.Messages.Add(message);
        Assert.Single(message.Commands); Assert.DoesNotContain("printf", message.Content); Assert.Equal(source, message.OriginalContent);
        var view = new SshAssistantView { DataContext = model }; view.Resources.MergedDictionaries.Add(Application.Current.Resources);
        using var bindings = new WpfBindingTrace(); await Layout();
        var answer = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(view)); Assert.Equal(source, answer.GetAnswerCopyText()); Assert.Equal(Visibility.Visible, ((StackPanel)answer.FindName("AnswerActions")).Visibility);
        model.CloseSource(); await Layout(); Assert.Equal(source, answer.GetAnswerCopyText()); Assert.False(model.SendCommand.CanExecute(null));
        Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(view)); Assert.Null(PresentationSource.FromVisual(view));
        async Task Layout()
        {
            view.Measure(new(1120, 1000)); view.Arrange(new(0, 0, 1120, 1000)); view.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
        }
    });
}
