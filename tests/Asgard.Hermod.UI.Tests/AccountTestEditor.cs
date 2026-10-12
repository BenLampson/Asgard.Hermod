namespace Asgard.Hermod.UI.Tests;

/// <summary>记录账号菜单的边界调用，避免测试打开浏览器或实际模态窗口。</summary>
internal sealed class AccountTestEditor : IAccountEditor
{
    /// <summary>已登录账户信息入口调用次数。</summary>
    public int Opened { get; private set; }
    /// <summary>未登录直达浏览器登录入口调用次数。</summary>
    public int SignIns { get; private set; }
    /// <inheritdoc />
    public Task OpenAsync() { Opened++; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task OpenForSignInAsync() { SignIns++; return Task.CompletedTask; }
}
