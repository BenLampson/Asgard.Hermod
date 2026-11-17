using Windows.ApplicationModel;

namespace Asgard.Hermod;

/// <summary>打包版使用稳定的 Windows 执行别名，独立版引用同目录下的入口。</summary>
internal static class McpLaunchCommand
{
    internal static string Get()
    {
        const string executable = "Hermod.Mcp.exe";
        try
        {
            _ = Package.Current;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", executable);
        }
        catch (InvalidOperationException) { return Path.Combine(AppContext.BaseDirectory, executable); }
    }
}
