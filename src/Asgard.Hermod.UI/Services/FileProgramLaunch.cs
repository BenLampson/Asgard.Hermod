using System.Diagnostics;
using System.IO;

namespace Asgard.Hermod.UI.Services;

/// <summary>程序路径和文件参数分开传递，远程键不能拼接命令行。</summary>
public static class FileProgramLaunch
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".cpl", ".lnk", ".url", ".reg", ".pif", ".appref-ms", ".application", ".jar",
        ".py", ".pyw", ".sh", ".bash", ".rb", ".pl", ".php", ".appx", ".msix", ".chm", ".scf", ".inf"
    };

    /// <summary>无规则时用系统默认关联；可执行类型须下载或指定编辑器，不能直接运行远程程序。</summary>
    public static ProcessStartInfo CreateStartInfo(string path, FileOpenRule? rule)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw Text.Exception("FileOpenMissing");
        if (rule is null)
        {
            if (ExecutableExtensions.Contains(Path.GetExtension(path))) throw Text.Exception("FileOpenExecutableBlocked");
            return new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" };
        }
        if (!File.Exists(rule.ProgramPath)) throw Text.Exception("FileProgramMissing");
        var info = new ProcessStartInfo(rule.ProgramPath) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }
}
