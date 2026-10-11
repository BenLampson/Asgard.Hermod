using System.IO;

namespace Asgard.Hermod.UI.Services;

/// <summary>将用户选择的本地路径映射为对象键，仅枚举文件元数据，拒绝符号链接和重解析点。</summary>
public static class ObjectStorageUploadPathMapper
{
    /// <summary>后台枚举可被关闭窗体取消，不占用 UI 线程或把文件读入内存。</summary>
    public static Task<IReadOnlyList<ObjectStorageUploadItem>> MapAsync(IEnumerable<string> paths, string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(prefix);
        string[] selected = paths.ToArray();
        return Task.Run<IReadOnlyList<ObjectStorageUploadItem>>(() => Map(selected, prefix, cancellationToken), cancellationToken);
    }

    /// <summary>先完成整次枚举才交给队列，遇到无权限或不安全链接不会留下部分结果。</summary>
    private static IReadOnlyList<ObjectStorageUploadItem> Map(string[] paths, string prefix, CancellationToken token)
    {
        var items = new List<ObjectStorageUploadItem>();
        string destination = prefix.Length == 0 ? "" : prefix.TrimEnd('/') + "/";
        foreach (string selected in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(selected);
            RejectLinkedAncestors(fullPath);
            FileAttributes attributes = File.GetAttributes(fullPath);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                AddFile(fullPath, Path.GetFileName(fullPath), destination, items, token);
                continue;
            }
            string? parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(fullPath))?.FullName;
            if (parent is null)
                throw new ObjectStorageException("不能上传磁盘根目录，请选择文件夹。");
            var directories = new Stack<string>();
            directories.Push(fullPath);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (directories.TryPop(out string? directory))
            {
                token.ThrowIfCancellationRequested();
                if (!visited.Add(directory))
                    throw new ObjectStorageException("目录存在循环，请选择真实文件夹。");
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    FileAttributes entryAttributes = File.GetAttributes(entry);
                    RejectLink(entryAttributes);
                    if (entryAttributes.HasFlag(FileAttributes.Directory))
                        directories.Push(entry);
                    else
                        AddFile(entry, Path.GetRelativePath(parent, entry).Replace('\\', '/'), destination, items, token);
                }
            }
        }
        return items;
    }

    /// <summary>只读取大小并保留绝对本地路径，实际文件流由上传执行服务稍后打开。</summary>
    private static void AddFile(string localPath, string relativePath, string prefix,
        List<ObjectStorageUploadItem> items, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectLink(File.GetAttributes(localPath));
        items.Add(new(localPath, prefix + relativePath, new FileInfo(localPath).Length));
    }

    /// <summary>直接选中链接目录中的普通文件也不能绕过链接限制。</summary>
    private static void RejectLinkedAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            RejectLink(File.GetAttributes(current));
    }

    /// <summary>所有重解析点均拒绝，因此目录链接不会形成递归循环。</summary>
    private static void RejectLink(FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ObjectStorageException("不能上传链接或重解析点，请选择真实文件和文件夹。");
    }
}
