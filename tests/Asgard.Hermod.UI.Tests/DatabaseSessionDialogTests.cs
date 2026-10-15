using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实模态窗口验证确认按钮、固定替代目标与默认取消行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseSessionDialogTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", 1)]
    [InlineData("zh-Hans", 2)]
    public Task Remove_TargetSelectionAndCancellationAreExplicit(string language, int remaining) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(System.Globalization.CultureInfo.GetCultureInfo(language));
        var request = new DatabaseRemovalRequest("instance", "Session 1", "app", "app", remaining == 1 ? ["reports"] : ["reports", "audit"], 0);
        var inspected = InspectAsync(async dialog =>
        {
            var accept = WindowTestVisuals.Descendants<Button>(dialog).Single(button => Equals(button.Content, Text.Get("DatabaseSessionConfirmRemove")));
            Assert.Equal(remaining == 1, accept.IsEnabled); Assert.False(accept.IsDefault);
            var selectors = WindowTestVisuals.Descendants<ComboBox>(dialog).ToArray();
            if (remaining == 1) Assert.Empty(selectors);
            else
            {
                var selector = Assert.Single(selectors); Assert.Equal(-1, selector.SelectedIndex);
                selector.SelectedItem = "audit"; Assert.True(accept.IsEnabled);
            }
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog), text => text.Text.Contains("app", StringComparison.Ordinal) && text.Text.Contains("Session 1", StringComparison.Ordinal));
            await WindowPreviewCapture.VerifyAsync(dialog, "session-removal-" + language);
        });
        try
        {
            Assert.Null(DatabaseSessionDialogs.Remove(null, "Development", "localhost:5432", request));
            await inspected;
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task Add_SingleCandidateUsesItDirectlyAndCancelReturnsNoSelection() => fixture.RunAsync(async () =>
    {
        var inspected = InspectAsync(dialog =>
        {
            var accept = WindowTestVisuals.Descendants<Button>(dialog).Single(button => Equals(button.Content, Text.Get("DatabaseSessionAdd")));
            Assert.True(accept.IsEnabled); Assert.False(accept.IsDefault);
            Assert.Empty(WindowTestVisuals.Descendants<CheckBox>(dialog)); Assert.Empty(WindowTestVisuals.Descendants<ComboBox>(dialog));
            return Task.CompletedTask;
        });
        Assert.Null(DatabaseSessionDialogs.Select(null, "Development", "localhost:3306", "Session 1", [new("instance", "app")], []));
        await inspected;
    });

    private static Task InspectAsync(Func<HermodWindow, Task> inspect)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var dialog = Application.Current.Windows.OfType<HermodWindow>().Single();
            dialog.Opacity = 0; dialog.ShowInTaskbar = false;
            try { await inspect(dialog); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
            finally { dialog.DialogResult = false; }
        }));
        return completion.Task;
    }
}
