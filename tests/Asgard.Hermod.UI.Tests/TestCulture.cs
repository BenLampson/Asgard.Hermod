using System.Globalization;
using System.Runtime.CompilerServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>历史中文界面断言在独立测试进程中使用确定的语言，专项测试显式覆盖其他语种。</summary>
internal static class TestCulture
{
    /// <summary>不读取开发机器偏好，也不修改操作系统设置或生产数据库。</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("zh-Hans");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
