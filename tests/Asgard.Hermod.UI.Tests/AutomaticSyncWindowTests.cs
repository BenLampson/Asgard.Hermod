using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>自动同步面板、只读保存提示及合并窗口的真实双语布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AutomaticSyncWindowTests(WpfTestFixture fixture)
{
    /// <summary>首次设置窗口说明本机记住密码，与手动旧模式的说明保持分离。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task PasswordSetup_ExplainsRememberingAndRendersAtMinimumSize(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; SyncPasswordWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var model = new SyncPasswordViewModel(false, true);
            Assert.Equal(Text.Get("SyncRememberedPasswordHelp"), model.PasswordHelp);
            window = new SyncPasswordWindow(model); WindowPreviewCapture.Show(window);
            Assert.Equal(Text.Get("SyncEnable"), window.Title);
            model.Password = "remembered-password"; Assert.True(model.CanConfirm);
            await WindowPreviewCapture.VerifyAsync(window, "automatic-password-" + language);
            window.Width = 480; window.Height = 480; await WindowPreviewCapture.VerifyAsync(window, "automatic-password-compact-" + language);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
    /// <summary>首次登录询问一次密码，后续同步无弹窗；忘记密码后显示暂停。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task Account_AutomaticallyUnlocksOnceAndShowsStatus(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; AccountWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var accounts = new AccountUiTestService(); var automatic = new AutomaticSyncUiTestService(); int prompts = 0;
            await using var model = new AccountViewModel(accounts, accounts, _ => { prompts++; return "remembered-password"; }, automatic);
            window = new AccountWindow(model); WindowPreviewCapture.Show(window);
            await model.SignInCommand.ExecuteAsync(); Assert.Equal(1, prompts); Assert.True(automatic.HasPassword);
            await model.SyncNowCommand.ExecuteAsync(); Assert.Equal(1, prompts); Assert.Equal(2, automatic.Checks);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), item => item.Text == automatic.Message);
            await WindowPreviewCapture.VerifyAsync(window, "automatic-account-" + language);
            window.Width = 520; window.Height = 520; await WindowPreviewCapture.VerifyAsync(window, "automatic-account-compact-" + language);
            await model.ForgetSyncPasswordCommand.ExecuteAsync(); Assert.False(automatic.HasPassword); Assert.False(model.SyncNowCommand.CanExecute(null));
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
    /// <summary>保存位置没有选择控件，账户状态决定说明。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task SaveLocation_IsInformationOnly(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; NewInstanceWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var accounts = new AccountUiTestService(); var sync = new AutomaticSyncUiTestService();
            using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()), new NewInstanceWizardTestConfigurationService(), accounts, sync);
            Assert.Equal(InstanceSaveLocation.Local, model.SaveLocation); Assert.Equal(Text.Get("SyncSaveLocalHint"), model.SaveLocationDescription);
            await accounts.SignInAsync(TestContext.Current.CancellationToken);
            Assert.Equal(InstanceSaveLocation.Account, model.SaveLocation); Assert.Equal(Text.Get("SyncSavePasswordHint"), model.SaveLocationDescription);
            await sync.EnableAsync("saved-password", TestContext.Current.CancellationToken);
            Assert.Equal(Text.Get("SyncSaveCloudHint"), model.SaveLocationDescription);
            NewInstanceWizardTests.MoveToTest(model);
            await model.TestConnectionCommand.ExecuteAsync(); model.NextCommand.Execute(null);
            window = new NewInstanceWindow(model); WindowPreviewCapture.Show(window);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<SelectionCard>(window), item => item.GroupName == "SaveLocation");
            await WindowPreviewCapture.VerifyAsync(window, "automatic-save-location-" + language);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
    /// <summary>冲突全部明确选择后才启用确认，遮蔽预览与紧凑布局可读。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Merge_RequiresEveryChoice(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; ConfigurationMergeWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var model = new ConfigurationMergeViewModel([
                new("instance:one:name", "Example OSS", "InstanceName", "Local name", "Cloud name"),
                new("instance:one:parameter:accessKeySecret", "Example OSS", "SyncConflictParameters", Text.Get("SyncSecretValue"), Text.Get("SyncSecretValue"))]);
            window = new ConfigurationMergeWindow(model); WindowPreviewCapture.Show(window);
            Assert.False(model.ConfirmCommand.CanExecute(null)); model.Items[0].SelectedIndex = 0; Assert.False(model.ConfirmCommand.CanExecute(null));
            model.Items[1].SelectedIndex = 1; Assert.True(model.ConfirmCommand.CanExecute(null)); Assert.False(model.Choices[model.Items[0].Key]); Assert.True(model.Choices[model.Items[1].Key]);
            await WindowPreviewCapture.VerifyAsync(window, "automatic-merge-" + language);
            window.Width = 520; window.Height = 450; await WindowPreviewCapture.VerifyAsync(window, "automatic-merge-compact-" + language);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
}
