using System.Collections.Concurrent;
using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Core;

/// <summary>以跨进程文件租约管理隔离下载目录；扫描和清理不遍历用户工作区。</summary>
public sealed partial class LocalTemporaryFileStorage : ITemporaryFileStorage, IDisposable
{
    private const string LeaseName = ".hermod-lease";
    private const string CategoryName = ".hermod-category";
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, ManagedTemporaryFile> _active = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <summary>默认复用之前的文件缓存目录；测试可以传入独立的绝对目录。</summary>
    public LocalTemporaryFileStorage(string? rootPath = null)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asgard", "Hermod", "file-cache")));
    }

    /// <inheritdoc />
    public string RootPath { get; }

    /// <inheritdoc />
    public void EnsureDirectory()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyRoot();
            Directory.CreateDirectory(RootPath);
            VerifyRoot();
        }
    }

    /// <inheritdoc />
    public IManagedTemporaryFile CreateFile(string name)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyRoot();
            Directory.CreateDirectory(RootPath);
            string directory = Path.Combine(RootPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FileStream? lease = null;
            try
            {
                lease = new FileStream(Path.Combine(directory, LeaseName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                WriteCategory(directory, TemporaryFileCategory.Download);
                var file = new ManagedTemporaryFile(this, Path.Combine(directory, SafeName(name)), lease);
                _active.TryAdd(directory, file);
                return file;
            }
            catch
            {
                lease?.Dispose();
                TryRemoveEmptyRequest(directory);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public Task<TemporaryStorageSnapshot> InspectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var requests = ReadRequests(cancellationToken, out int unreadable);
            var files = requests.SelectMany(request => request.Files.Select(file => (File: file, request.InUse))).ToArray();
            var usage = Enum.GetValues<TemporaryFileCategory>().Select(category => new TemporaryStorageUsage(category,
                files.Where(file => file.File.Category == category).Sum(file => file.File.Length),
                files.Count(file => file.File.Category == category))).ToArray();
            return new TemporaryStorageSnapshot(files.Sum(file => file.File.Length), files.Length,
                files.Where(file => file.InUse).Sum(file => file.File.Length), files.Count(file => file.InUse), usage, unreadable);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<TemporaryStorageCleanupResult> CleanupAsync(bool expiredOnly = false, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var requests = ReadRequests(cancellationToken, out int unreadable);
            long freed = 0;
            int deleted = 0, skipped = unreadable;
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (expiredOnly && Directory.GetCreationTimeUtc(request.Directory) >= DateTime.UtcNow.AddDays(-7)) continue;
                if (request.InUse) { skipped += request.Files.Count; continue; }
                FileStream? lease = null;
                var handles = new List<FileStream>();
                int remaining = request.Files.Count, requestDeleted = 0;
                try
                {
                    VerifyRequest(request.Directory);
                    lease = AcquireLease(request.Directory);
                    // 清理前再次读取并独占正文，扫描到确认之间的新文件及锁不能被忽略。
                    var current = ReadFiles(request.Directory, out bool unsafeItems);
                    remaining = current.Count;
                    if (unsafeItems) { skipped += Math.Max(1, current.Count); continue; }
                    foreach (var file in current)
                        handles.Add(new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Delete));
                    for (int index = 0; index < current.Count; index++)
                    {
                        var file = current[index];
                        VerifyRequest(request.Directory);
                        VerifyRegularFile(file.Path);
                        File.Delete(file.Path);
                        freed += handles[index].Length; deleted++; requestDeleted++;
                    }
                }
                catch (Exception exception) when (IsAccessError(exception)) { skipped += Math.Max(1, remaining - requestDeleted); }
                finally
                {
                    foreach (var handle in handles) handle.Dispose();
                    lease?.Dispose();
                    TryRemoveEmptyRequest(request.Directory);
                }
            }
            return new TemporaryStorageCleanupResult(freed, deleted, skipped);
        }, cancellationToken);

    /// <summary>外部编辑器可能不保持正文句柄，保守地保护其缓存直到 Hermod 退出。</summary>
    internal void Retain(ManagedTemporaryFile file)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VerifyRequest(Path.GetDirectoryName(file.Path)!);
            WriteCategory(Path.GetDirectoryName(file.Path)!, TemporaryFileCategory.Cache);
        }
    }

    /// <summary>预览关闭或失败后先释放租约，再安全清理该请求；清理失败留待设置页重试。</summary>
    internal void Release(ManagedTemporaryFile file, bool retain)
    {
        lock (_gate)
        {
            if (retain && !_disposed) return;
            string directory = Path.GetDirectoryName(file.Path)!;
            _active.TryRemove(directory, out _);
            file.ReleaseLease();
            if (!retain) TryDeleteRequest(directory);
        }
    }

    /// <summary>应用退出释放全部租约；外部缓存保留供下次会话统计与清理。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var file in _active.Values.ToArray()) file.ReleaseLease();
            _active.Clear();
        }
    }

    /// <summary>分类按不变数字写入，不存储翻译结果。</summary>
    private static void WriteCategory(string directory, TemporaryFileCategory category) =>
        File.WriteAllText(Path.Combine(directory, CategoryName), ((int)category).ToString(CultureInfo.InvariantCulture));
}
