using System.Text;

namespace Asgard.Hermod.Mcp;

/// <summary>将客户端传来的相对路径与已选根目录绑定，并限制 Windows 路径和对象键的映射。</summary>
internal static class McpStorageTransferPaths
{
    private static readonly char[] Separators = ['/', '\\'];

    internal static string NormalizeRoot(string root)
    {
        try
        {
            if (root.Length < 4 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] is not ('\\' or '/')
                || root.Any(char.IsControl))
                throw Text.Exception("McpTransferInvalidLocalPath");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string drive = Path.GetPathRoot(full)!;
            if (full.Length <= drive.Length || new DriveInfo(drive).DriveType == DriveType.Network || !Directory.Exists(full))
                throw Text.Exception("McpTransferInvalidLocalPath");
            CheckExisting(full, allowMissing: false);
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Text.Exception("McpTransferInvalidLocalPath");
        }
    }

    internal static bool IsKey(string key) => key.Length > 0 && Encoding.UTF8.GetByteCount(key) <= 1023
        && !key.Any(char.IsControl) && McpGrantRules.IsObjectPath(key)
        && (key.EndsWith('/') ? key[..^1] : key).Split('/').All(part => part.Length > 0);

    internal static string Resolve(string root, string relative, bool directory)
    {
        if (relative.Length == 0)
            return directory ? root : throw Text.Exception("McpTransferInvalidLocalPath");
        if (relative[0] is '/' or '\\' || Path.IsPathRooted(relative))
            throw Text.Exception("McpTransferInvalidLocalPath");
        string input = directory && (relative[^1] is '/' or '\\') ? relative[..^1] : relative;
        string[] parts = input.Split(Separators);
        if (parts.Any(part => !IsRegularPart(part))) throw Text.Exception("McpTransferInvalidLocalPath");
        string target = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw Text.Exception("McpTransferInvalidLocalPath");
        return target;
    }

    // 在每次打开源文件或目标目录之前重新拒绝符号链接、目录联接和设备。
    internal static FileAttributes? CheckExisting(string path, bool allowMissing)
    {
        string current = Path.GetPathRoot(path)!;
        string[] parts = path[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        FileAttributes? last = null;
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (allowMissing) return null;
                throw Text.Exception("McpTransferInvalidLocalPath");
            }
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0
                || (i < parts.Length - 1 && !attributes.HasFlag(FileAttributes.Directory)))
                throw Text.Exception("McpTransferInvalidLocalPath");
            last = attributes;
        }
        return last;
    }

    private static bool IsRegularPart(string part)
    {
        if (part.Length is 0 or > 255 || part.EndsWith('.') || part.EndsWith(' ')
            || part.Any(char.IsControl) || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && stem[3] is (>= '1' and <= '9') or '¹' or '²' or '³');
    }
}
