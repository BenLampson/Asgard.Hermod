using System.Globalization;

namespace Asgard.Hermod.Core;

/// <summary>限定 GUID 请求目录、拒绝链接和重解析点，并在删除前重新确认边界。</summary>
public sealed partial class LocalTemporaryFileStorage
{
    /// <summary>扫描不进入嵌套目录，不读取可能指向配置或工作区的链接目标。</summary>
    private List<TemporaryRequestState> ReadRequests(CancellationToken token, out int unreadable)
    {
        VerifyRoot();
        unreadable = 0;
        var requests = new List<TemporaryRequestState>();
        if (!Directory.Exists(RootPath)) return requests;
        foreach (string entry in Directory.EnumerateFileSystemEntries(RootPath))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                VerifyRequest(entry);
                var files = ReadFiles(entry, out bool unsafeItems);
                if (unsafeItems) unreadable++;
                bool inUse = unsafeItems || _active.ContainsKey(entry);
                if (!inUse)
                {
                    try
                    {
                        // 刚建立但尚未写入租约的空目录不参与清理，避免另一个 Hermod 的创建竞态。
                        if (files.Count == 0 && !File.Exists(Path.Combine(entry, LeaseName))
                            && Directory.GetCreationTimeUtc(entry) > DateTime.UtcNow.AddMinutes(-1)) inUse = true;
                        else
                        {
                            using var lease = AcquireLease(entry);
                            foreach (var file in files)
                            {
                                using var handle = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None);
                            }
                        }
                    }
                    catch (Exception exception) when (IsAccessError(exception)) { inUse = true; }
                }
                requests.Add(new(entry, files, inUse));
            }
            catch (Exception exception) when (IsAccessError(exception)) { unreadable++; }
        }
        return requests;
    }

    /// <summary>标记文件不计入正文占用；未完成下载单独归入处理中间文件。</summary>
    private static List<TemporaryFileState> ReadFiles(string directory, out bool unsafeItems)
    {
        unsafeItems = false;
        var files = new List<TemporaryFileState>();
        TemporaryFileCategory category = TemporaryFileCategory.Cache;
        string marker = Path.Combine(directory, CategoryName);
        try
        {
            if (File.Exists(marker))
            {
                VerifyRegularFile(marker);
                if (new FileInfo(marker).Length <= 32 && int.TryParse(File.ReadAllText(marker), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value) && Enum.IsDefined((TemporaryFileCategory)value))
                    category = (TemporaryFileCategory)value;
            }
        }
        catch (Exception exception) when (IsAccessError(exception)) { unsafeItems = true; }
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            try
            {
                VerifyRegularFile(path);
                string name = Path.GetFileName(path);
                if (name is LeaseName or CategoryName) continue;
                files.Add(new(path, new FileInfo(path).Length,
                    name.StartsWith('.') && name.EndsWith(".download", StringComparison.Ordinal)
                        && Guid.TryParseExact(name.Split('.')[^2], "N", out _) ? TemporaryFileCategory.Processing : category));
            }
            catch (Exception exception) when (IsAccessError(exception)) { unsafeItems = true; }
        }
        return files;
    }

    /// <summary>所有祖先必须是真实目录，根目录被替换成链接时立即停止扫描或删除。</summary>
    private void VerifyRoot()
    {
        for (string? current = RootPath; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current)) throw Text.Exception("TemporaryStorageUnsafePath");
            if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw Text.Exception("TemporaryStorageUnsafePath");
        }
    }

    /// <summary>只能处理专用根目录中的直接 GUID 子目录，不允许递归删除任何路径。</summary>
    private void VerifyRequest(string directory)
    {
        VerifyRoot();
        string full = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(full), RootPath, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _)
            || !Directory.Exists(full) || File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
            throw Text.Exception("TemporaryStorageUnsafePath");
    }

    /// <summary>目录、符号链接、重解析点永远不作为可删除正文。</summary>
    private static void VerifyRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw Text.Exception("TemporaryStorageUnsafePath");
    }

    /// <summary>文件租约可被自己删除，但与其它读写租约互斥。</summary>
    private FileStream AcquireLease(string directory)
    {
        VerifyRequest(directory);
        string marker = Path.Combine(directory, LeaseName);
        if (File.Exists(marker)) VerifyRegularFile(marker);
        return new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete);
    }

    /// <summary>释放预览时只清理当前请求；锁定或不安全文件留待下次安全维护。</summary>
    private void TryDeleteRequest(string directory)
    {
        try
        {
            VerifyRequest(directory);
            using (var lease = AcquireLease(directory))
            {
                var files = ReadFiles(directory, out bool unsafeItems);
                if (unsafeItems) return;
                var handles = new List<FileStream>();
                try
                {
                    foreach (var file in files) handles.Add(new(file.Path, FileMode.Open, FileAccess.Read, FileShare.Delete));
                    foreach (var file in files) { VerifyRegularFile(file.Path); File.Delete(file.Path); }
                }
                finally { foreach (var handle in handles) handle.Dispose(); }
            }
            TryRemoveEmptyRequest(directory);
        }
        catch (Exception exception) when (IsAccessError(exception)) { }
    }

    /// <summary>只移除已经没有正文的请求，不用递归删除，也不跟随目录链接。</summary>
    private void TryRemoveEmptyRequest(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            VerifyRequest(directory);
            using (var lease = AcquireLease(directory))
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any(path => Path.GetFileName(path) is not LeaseName and not CategoryName)) return;
                foreach (string marker in new[] { Path.Combine(directory, CategoryName), Path.Combine(directory, LeaseName) })
                    if (File.Exists(marker)) { VerifyRegularFile(marker); File.Delete(marker); }
            }
            VerifyRequest(directory);
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception exception) when (IsAccessError(exception)) { }
    }

    /// <summary>访问失败和安全拒绝不会泄漏路径，也不会中断其它请求的扫描。</summary>
    private static bool IsAccessError(Exception exception) => exception is IOException or UnauthorizedAccessException
        || exception.Data["Hermod.ResourceKey"] as string == "TemporaryStorageUnsafePath";

    /// <summary>保留后缀，剥离远程路径并避免 Windows 设备名称。</summary>
    private static string SafeName(string name)
    {
        string filename = name.Replace('\\', '/').Split('/').Last().TrimEnd(' ', '.');
        string extension = Path.GetExtension(filename);
        if (extension.Length > 33 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) extension = "";
        string stem = Path.GetFileNameWithoutExtension(filename);
        stem = new string(stem.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        if (stem.Length > 100) stem = stem[..100];
        string upper = stem.Split('.')[0].ToUpperInvariant();
        bool reserved = upper is "CON" or "PRN" or "AUX" or "NUL" || upper.Length == 4
            && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal))
            && upper[3] is >= '1' and <= '9';
        filename = (reserved ? "file-" : "") + stem + extension;
        if (filename is LeaseName or CategoryName) filename = "file-" + filename;
        return filename.Length == 0 ? "file" : filename;
    }
}
