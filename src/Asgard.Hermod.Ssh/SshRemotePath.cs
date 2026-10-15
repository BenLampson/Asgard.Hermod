namespace Asgard.Hermod.Ssh;

/// <summary>远端路径只能按 POSIX 规则处理，不能使用 Windows 的 Path.Combine。</summary>
public static class SshRemotePath
{
    /// <summary>名称是单个文件名；拒绝路径穿越、NUL 和终端控制字符。</summary>
    public static string CombineName(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(char.IsControl))
            throw Text.Exception("SshFileNameInvalid");
        return directory.TrimEnd('/') + "/" + name;
    }

    /// <summary>根目录的上级仍然为根；相对路径使用服务器解析的上级。</summary>
    public static string Parent(string path)
    {
        string trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0) return "/";
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed + "/.." : slash == 0 ? "/" : trimmed[..slash];
    }
}
