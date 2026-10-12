using System.Net;

namespace Asgard.Hermod.Identity;

/// <summary>离线结果页复用品牌图形，所有界面文案来自共享语言资源。</summary>
internal static class LoginCallbackPage
{
    private static readonly string Template = ReadTemplate();
    private static readonly string Logo = ReadLogo();

    /// <summary>协议字段与原始异常从不进入页面；资源文案进入 HTML 前统一编码。</summary>
    internal static string Render(LoginCallbackStatus status, string nonce)
    {
        var (state, heading, description) = status switch
        {
            LoginCallbackStatus.Succeeded => ("success", "LoginCallbackSuccessTitle", "LoginCallbackSuccessHelp"),
            LoginCallbackStatus.Received => ("received", "LoginCallbackReceivedTitle", "LoginCallbackReceived"),
            LoginCallbackStatus.Invalid => ("failure", "LoginCallbackFailedTitle", "LoginCallbackInvalid"),
            _ => ("failure", "LoginCallbackFailedTitle", "LoginCallbackFailedHelp"),
        };
        var values = new Dictionary<string, string>
        {
            ["LANG"] = Text.Culture.Name,
            ["NONCE"] = nonce,
            ["STATE"] = state,
            ["BRAND"] = Text.Get("Hermod"),
            ["TITLE"] = Text.Get(heading),
            ["DESCRIPTION"] = Text.Get(description),
            ["CLOSE"] = Text.Get("LoginCallbackClosePage"),
            ["CLOSE_HELP"] = Text.Get("LoginCallbackCloseHelp"),
            ["CLOSE_FALLBACK"] = Text.Get("LoginCallbackCloseFallback"),
        };
        string page = Template.Replace("{{LOGO}}", Logo, StringComparison.Ordinal);
        foreach (var value in values)
            page = page.Replace("{{" + value.Key + "}}", WebUtility.HtmlEncode(value.Value), StringComparison.Ordinal);
        return page;
    }

    /// <summary>模板编译进程序集，回调无需读取可执行目录或请求线上资产。</summary>
    private static string ReadTemplate()
    {
        using var stream = typeof(LoginCallbackPage).Assembly.GetManifestResourceStream("Hermod.LoginCallback.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>品牌图形内嵌为 Data URL，监听释放后仍能完整显示。</summary>
    private static string ReadLogo()
    {
        using var stream = typeof(LoginCallbackPage).Assembly.GetManifestResourceStream("Hermod.Logo.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Convert.ToBase64String(bytes.ToArray());
    }
}
