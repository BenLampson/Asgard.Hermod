using System.Globalization;
using System.Net.Http;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实双语 WPF 注册窗口、密码释放和显式登录选择。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountRegistrationWindowTests(WpfTestFixture fixture)
{
    /// <summary>完整和紧凑布局可用，成功后密码清空且不能再次注册。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Registration_RendersAndClearsPasswordAfterSuccess(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        AccountRegistrationWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var service = new RegistrationUiTestService();
            await using var model = Model(service);
            window = new AccountRegistrationWindow(model); WindowPreviewCapture.Show(window);
            Assert.Equal(Text.Get("RegistrationTitle"), window.Title);
            Assert.Equal(2, WindowTestVisuals.Descendants<SecretInput>(window).Count());
            Assert.All(WindowTestVisuals.Descendants<SecretInput>(window), input => Assert.False(input.IsPasswordVisible));
            await WindowPreviewCapture.VerifyAsync(window, "registration-" + language);
            window.Width = 480; window.Height = 520;
            await WindowPreviewCapture.VerifyAsync(window, "registration-compact-" + language);
            await model.RegisterCommand.ExecuteAsync(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(model.IsRegistered); Assert.False(model.RegisterCommand.CanExecute(null));
            Assert.Equal("", model.Password); Assert.Equal("", model.ConfirmPassword);
            Assert.Equal(Text.Get("RegistrationSucceeded"), model.Message);
            Assert.True(model.SignInCommand.CanExecute(null)); Assert.Equal(1, service.Calls);
            await model.RegisterCommand.ExecuteAsync(); Assert.Equal(1, service.Calls);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
    /// <summary>密码不一致不访问 API；未知结果只允许先登录，原始异常不出现在界面。</summary>
    [Fact]
    public Task Registration_RejectsMismatchAndBlocksUncertainResubmission() => fixture.RunAsync(async () =>
    {
        var service = new RegistrationUiTestService();
        await using var model = Model(service);
        model.ConfirmPassword = "different-password";
        await model.RegisterCommand.ExecuteAsync();
        Assert.Equal(0, service.Calls); Assert.Equal(Text.Get("RegistrationPasswordMismatch"), model.Message);
        model.Password = model.ConfirmPassword = "test-password-123456";
        service.Error = new HttpRequestException("private credential and remote response");
        await model.RegisterCommand.ExecuteAsync();
        Assert.True(model.IsUncertain); Assert.False(model.IsRegistered); Assert.False(model.CanRegister);
        Assert.Equal(Text.Get("RegistrationOutcomeUnknown"), model.Message);
        Assert.Equal("", model.Password); Assert.DoesNotContain("credential", model.Message);
    });
    /// <summary>打开注册入口与注册成功均不会自动登录，只有明确返回 true 才调用原登录流程。</summary>
    [Fact]
    public Task AccountMenu_RegistrationRequiresExplicitSignInChoice() => fixture.RunAsync(async () =>
    {
        var accounts = new AccountUiTestService(); bool signIn = false;
        await using var model = new AccountViewModel(accounts, accounts, _ => null, requestRegistration: () => Task.FromResult(signIn));
        Assert.True(model.HasRegistration);
        await model.RegisterCommand.ExecuteAsync(); Assert.Null(accounts.Account);
        signIn = true;
        await model.RegisterCommand.ExecuteAsync(); Assert.NotNull(accounts.Account);
        Assert.False(model.HasRegistration); Assert.False(model.RegisterCommand.CanExecute(null));
    });
    /// <summary>测试资料只存在于窗口状态中。</summary>
    private static AccountRegistrationViewModel Model(IAccountRegistrationService service) => new(service)
    {
        Username = " new-user ",
        Email = "new-user@example.test",
        Password = "test-password-123456",
        ConfirmPassword = "test-password-123456",
    };
}
