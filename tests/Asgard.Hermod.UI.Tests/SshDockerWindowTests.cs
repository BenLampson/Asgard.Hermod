using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>需显式授权才显示测试窗口；所有连接均为内存替身，不访问任何服务器。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerWindowTests(WpfTestFixture fixture)
{
    /// <summary>日常回归默认跳过原生窗口，不干扰用户桌面。</summary>
    public static bool IsConfigured => Environment.GetEnvironmentVariable("HERMOD_TEST_DOCKER_WINDOWS") == "1";

    /// <summary>真实深色确认窗口展示对象及影响，拒绝不执行，批准只执行一次。</summary>
    [Fact(Skip = "Explicitly enable temporary Docker UI windows.", SkipUnless = nameof(IsConfigured))]
    public Task NativeActionConfirmationKeepsDarkThemeAndHumanReadableContent() => fixture.RunAsync(async () =>
    {
        var previous = Text.Culture; Text.Initialize("zh-Hans", CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(UiTheme.Dark);
        var connection = new SshMonitorTestData().CreateConnection(); var data = new SshMonitorTestData();
        connection.Handler = (command, token) => command.StartsWith("docker restart ", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult("done", 0, false)) : data.HandleAsync(command, token);
        var window = new Window { Width = 1100, Height = 820, ShowInTaskbar = false, ShowActivated = false };
        using var model = new SshDockerCommandsViewModel(() => connection, () => "isolated-test",
            new WindowsSshWorkspaceDialogs(() => window), () => true, (_, _) => Task.CompletedTask);
        var view = new SshDockerCommandsView { DataContext = model }; window.Content = view;
        try
        {
            await model.InitializeAsync(); model.SelectedContainer = model.Containers[1];
            window.Show(); await WpfTestFixture.DrainDispatcherAsync();
            var button = Assert.IsType<Button>(view.FindName("RestartAction"));
            foreach (bool accept in new[] { false, true })
            {
                Exception? failure = null;
                var review = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    ConfirmationDialog? dialog = null;
                    try
                    {
                        dialog = Assert.Single(Application.Current.Windows.OfType<ConfirmationDialog>());
                        Assert.True(dialog.IsVisible);
                        var background = Assert.IsType<SolidColorBrush>(dialog.Background).Color;
                        Assert.True(background.R < 100 && background.G < 100 && background.B < 100);
                        var panel = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(dialog.Content).Content);
                        Assert.DoesNotContain(panel.Children.Cast<UIElement>(), child => child is TextBox);
                        string message = string.Join(" ", panel.Children.OfType<TextBlock>().Select(text => text.Text));
                        Assert.Contains("hermod-api", message, StringComparison.Ordinal);
                        Assert.Contains("isolated-test", message, StringComparison.Ordinal);
                        Assert.DoesNotContain("docker restart", message, StringComparison.Ordinal);
                    }
                    catch (Exception error) { failure = error; }
                    finally { if (dialog?.IsVisible == true) dialog.DialogResult = accept; }
                });
                var command = Assert.IsType<Asgard.Hermod.UI.Common.AsyncRelayCommand>(button.Command);
                await command.ExecuteAsync(button.CommandParameter); await review;
                Assert.Null(failure); Assert.Empty(model.Error); Assert.True(view.IsVisible);
                Assert.Equal(accept ? 1 : 0, connection.Commands.Count(c => c.StartsWith("docker restart ", StringComparison.Ordinal)));
            }
        }
        finally
        {
            foreach (var dialog in Application.Current.Windows.OfType<ConfirmationDialog>().ToArray()) dialog.Close();
            window.Close(); themes.SetTheme(UiTheme.Light); Text.Initialize(previous.Name, CultureInfo.InvariantCulture);
        }
    });
}
