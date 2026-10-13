using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实密码弹窗的双语、英语回退、遮蔽输入、长度和恢复确认。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SyncPasswordWindowTests(WpfTestFixture fixture)
{
    /// <summary>上传与下载在明暗主题下保持可读，只有有效密码与必要确认才能提交。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, false)]
    [InlineData("en", UiTheme.Light, true)]
    [InlineData("zh-Hans", UiTheme.Light, false)]
    [InlineData("zh-Hans", UiTheme.Light, true)]
    [InlineData("en", UiTheme.Dark, false)]
    [InlineData("en", UiTheme.Dark, true)]
    [InlineData("zh-Hans", UiTheme.Dark, false)]
    [InlineData("zh-Hans", UiTheme.Dark, true)]
    [InlineData("fr-FR", UiTheme.Light, false)]
    [InlineData("fr-FR", UiTheme.Light, true)]
    public Task PasswordDialog_ValidatesAndReturnsExplicitConfirmation(string language, UiTheme theme, bool download) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        using var model = new SyncPasswordViewModel(download);
        var owner = new HermodWindow { Width = 640, Height = 560, Opacity = 0, ShowInTaskbar = false };
        SyncPasswordWindow? dialog = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            themes.SetTheme(theme);
            owner.Show();
            dialog = new SyncPasswordWindow(model) { Owner = owner, Opacity = 0 };
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                Assert.Equal(Text.Get(download ? "DownloadWorkspace" : "UploadWorkspace"), dialog.Title);
                Assert.Equal(Text.Get(download ? "SyncDownloadPasswordExplanation" : "SyncUploadPasswordExplanation"),
                    Assert.IsType<TextBlock>(dialog.FindName("PasswordExplanation")).Text);
                var input = Assert.IsType<SecretInput>(dialog.FindName("PasswordInput"));
                var editor = Assert.IsType<PasswordBox>(input.Template.FindName("PART_Editor", input));
                var confirm = Assert.IsType<Button>(dialog.FindName("ConfirmButton"));
                var restore = Assert.IsType<CheckBox>(dialog.FindName("RestoreConfirmation"));
                Assert.Equal("", editor.Password);
                Assert.Equal(128, editor.MaxLength);
                Assert.Equal('●', editor.PasswordChar);
                Assert.Equal(Text.Get("SyncPassword"), AutomationProperties.GetName(input));
                Assert.Same(editor, Keyboard.FocusedElement);
                Assert.False(confirm.IsDefault);
                Assert.False(confirm.IsEnabled);
                Assert.Equal(download ? Visibility.Visible : Visibility.Collapsed, restore.Visibility);
                editor.Password = new string('a', 11);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.False(confirm.IsEnabled);
                editor.Password = "password-for-sync";
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(!download, confirm.IsEnabled);
                if (download) restore.IsChecked = true;
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.True(confirm.IsEnabled);
                model.Password = new string('a', 128);
                Assert.True(model.ConfirmCommand.CanExecute(null));
                model.Password = new string('a', 129);
                Assert.False(model.ConfirmCommand.CanExecute(null));
                model.Password = "password-for-sync";
                await WindowPreviewCapture.VerifyAsync(dialog, $"sync-password-{theme}-{language}-{download}");
                dialog.Width = 480; dialog.Height = 480;
                await WindowPreviewCapture.VerifyAsync(dialog, $"sync-password-compact-{theme}-{language}-{download}");
                editor.Focus();
                Assert.True(editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.NotSame(editor, Keyboard.FocusedElement);
                WindowTestVisuals.Invoke(confirm);
            });
            Assert.True(result);
            Assert.Equal("password-for-sync", model.Password);
            model.Dispose();
            Assert.Empty(model.Password);
            Assert.Empty(Assert.IsType<SecretInput>(dialog.FindName("PasswordInput")).Value);
            Assert.False(model.ConfirmCommand.CanExecute(null));
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally
        {
            if (dialog?.IsVisible == true) dialog.Close();
            owner.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>Escape 和关闭按钮都不能确认，即使密码已填写也不会提交。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PasswordDialog_CancelOrCloseReturnsNoConfirmation(bool closeWindow) => fixture.RunAsync(async () =>
    {
        using var model = new SyncPasswordViewModel(false);
        var dialog = new SyncPasswordWindow(model) { Opacity = 0 };
        bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, () =>
        {
            model.Password = "password-for-sync";
            if (closeWindow) dialog.Close();
            else
            {
                var escape = Assert.Single(dialog.InputBindings.OfType<KeyBinding>());
                Assert.Equal(Key.Escape, escape.Key);
                Assert.Same(model.CancelCommand, escape.Command);
                escape.Command.Execute(null);
            }
            return Task.CompletedTask;
        });
        Assert.NotEqual(true, result);
        model.Dispose();
        Assert.Empty(model.Password);
    });
}
