using System.Text;

namespace Asgard.Hermod.Mcp;

/// <summary>限制下载目标与旧上传授权的路径范围；新上传授权接受明确的绝对来源路径。</summary>
internal static class McpStorageTransferPaths
{
    private static readonly char[] Separators = ['/', '\\'];

    internal static string NormalizeRoot(string root)
    {
        try
        {
            if (root.Length < 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] is not ('\\' or '/')
                || root.Any(char.IsControl))
                throw Text.Exception("McpTransferInvalidLocalPath");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string drive = Path.GetPathRoot(full)!;
            if (full.Length < drive.Length || new DriveInfo(drive).DriveType == DriveType.Network || !Directory.Exists(full))
                throw Text.Exception("McpTransferInvalidLocalPath");
            CheckExisting(full, allowMissing: false);
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Text.Exception("McpTransferInvalidLocalPath");
        }
    }

    // 空前缀表示 Bucket 根目录，其余目录键以斜杠结尾。
    internal static bool IsDirectoryKey(string key) => key.Length == 0 || key.EndsWith('/');

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
        string boundary = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw Text.Exception("McpTransferInvalidLocalPath");
        return target;
    }

    // 新上传授权使用完整来源路径，不与下载目录或旧授权的相对路径混用。
    internal static string ResolveUpload(string path, bool directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw Text.Exception("McpTransferInvalidUploadPath");
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string root = Path.GetPathRoot(full)!;
            if (root.Length == 0 || !directory && full.Length == root.Length
                || full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries).Any(part => !IsRegularPart(part)))
                throw Text.Exception("McpTransferInvalidUploadPath");
            return full;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Text.Exception("McpTransferInvalidUploadPath");
        }
    }

    // 下载及旧授权始终拒绝链接；新上传明确允许任意来源，仍拒绝设备路径。
    internal static FileAttributes? CheckExisting(string path, bool allowMissing, bool allowLinks = false)
    {
        string current = Path.GetPathRoot(path)!;
        string[] parts = path[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        FileAttributes? last = null;
        if (parts.Length == 0)
        {
            try { last = File.GetAttributes(current); }
            catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (allowMissing) return null;
                throw Text.Exception("McpTransferInvalidLocalPath");
            }
            if ((last.Value & FileAttributes.Device) != 0 || !allowLinks && last.Value.HasFlag(FileAttributes.ReparsePoint))
                throw Text.Exception("McpTransferInvalidLocalPath");
            return last;
        }
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
            if ((attributes & FileAttributes.Device) != 0 || !allowLinks && attributes.HasFlag(FileAttributes.ReparsePoint)
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
