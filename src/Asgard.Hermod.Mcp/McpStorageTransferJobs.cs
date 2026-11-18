using System.Security.Cryptography;

namespace Asgard.Hermod.Mcp;

/// <summary>保存进程内传输状态；任务跟随授权的生命周期，独立于发起调用和客户端连接。</summary>
internal sealed class McpStorageTransferJobs : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly CancellationTokenSource _stop = new();
    private readonly McpAccessService _access;
    private readonly McpStorageTransfer _transfer;
    private readonly TimeProvider _clock;
    private readonly ITimer _sweep;
    private int _active;
    private bool _disposed;

    internal McpStorageTransferJobs(McpAccessService access, McpStorageTransfer transfer, TimeProvider clock)
    {
        _access = access;
        _transfer = transfer;
        _clock = clock;
        _sweep = clock.CreateTimer(_ => { lock (_gate) Sweep(); }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    internal object Start(McpSessionAccess session, McpGrant grant, McpArguments args, CancellationToken requestToken)
    {
        _access.Check(session, grant);
        bool upload = args.String("direction") == "upload";
        if (!(upload ? grant.AllowUpload : grant.AllowDownload))
            throw Text.Exception("McpAccessDenied");
        bool overwrite = args.String("conflict", "fail") == "overwrite";
        if (overwrite && !grant.AllowOverwrite) throw Text.Exception("McpAccessDenied");

        string key = args.String("key");
        if (!(session.Policy.DefaultAllow && key.Length == 0) && !McpStorageTransferPaths.IsKey(key)
            || !key.StartsWith(grant.Qualifier, StringComparison.Ordinal))
            throw Text.Exception("McpTransferInvalidKey");
        bool directory = McpStorageTransferPaths.IsDirectoryKey(key);
        string fallback = !upload && !directory ? key[(key.LastIndexOf('/') + 1)..] : "";
        string suppliedPath = args.String("local_path", fallback);
        string local = upload && grant.AllowUnrestrictedUpload
            ? McpStorageTransferPaths.ResolveUpload(suppliedPath, directory)
            : grant.TransferRoot is { } root ? McpStorageTransferPaths.Resolve(root, suppliedPath, directory)
            : throw Text.Exception("McpAccessDenied");

        lock (_gate)
        {
            requestToken.ThrowIfCancellationRequested();
            session.Revoked.ThrowIfCancellationRequested();
            if (_disposed) throw Text.Exception("McpAccessUnavailable");
            Sweep();
            if (_active >= 8 || _entries.Count >= 64) throw Text.Exception("McpTransferQueueFull");
            var entry = new Entry(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), grant, session.Revoked);
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, session.Revoked);
            lifetime.CancelAfter(TimeSpan.FromHours(2));
            _entries.Add(entry.Id, entry);
            _active++;
            _ = Task.Run(() => RunAsync(entry, session, upload, overwrite, key, local, lifetime));
            return Snapshot(entry);
        }
    }

    internal object Poll(McpSessionAccess session, string id)
    {
        lock (_gate)
        {
            Sweep();
            if (!_entries.TryGetValue(id, out var entry) || entry.Owner != session.Revoked)
                throw Text.Exception("McpTransferNotFound");
            _access.Check(session, entry.Grant);
            object snapshot = Snapshot(entry);
            // 终态可领取一次；在同一把锁里读取并清理，防止并发查询重复处理结果。
            if (entry.FinishedAt is not null) _entries.Remove(id);
            return snapshot;
        }
    }

    private async Task RunAsync(Entry entry, McpSessionAccess session, bool upload, bool overwrite, string key, string local, CancellationTokenSource lifetime)
    {
        string? failure = null;
        try
        {
            await _slots.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                lock (_gate) entry.Status = "running";
                await _transfer.RunAsync(session, entry.Grant, upload, overwrite, key, local, (total, completed) =>
                {
                    lock (_gate)
                    {
                        entry.Total = total;
                        entry.Completed = completed;
                    }
                }, lifetime.Token).ConfigureAwait(false);
            }
            finally { _slots.Release(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { failure = Text.Get("McpTransferTimedOut"); }
        catch (Exception error) { failure = Text.Error(error, "McpTransferFailed"); }
        finally
        {
            lifetime.Dispose();
            lock (_gate)
            {
                if (entry.Owner.IsCancellationRequested) _entries.Remove(entry.Id);
                entry.Status = failure is null ? "completed" : "failed";
                entry.Error = failure;
                entry.FinishedAt = _clock.GetTimestamp();
                if (--_active == 0 && _disposed) _slots.Dispose();
            }
        }
    }

    private void Sweep()
    {
        long now = _clock.GetTimestamp();
        foreach (string id in _entries.Where(pair => pair.Value.Owner.IsCancellationRequested
            || pair.Value.FinishedAt is { } end && _clock.GetElapsedTime(end, now) >= Retention).Select(pair => pair.Key).ToArray())
            _entries.Remove(id);
    }

    private static object Snapshot(Entry entry) => new
    {
        taskId = entry.Id,
        status = entry.Status,
        totalFiles = entry.Total,
        completedFiles = entry.Completed,
        error = entry.Error,
        pollAfterMs = entry.FinishedAt is null ? (int?)1000 : null,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            if (_active == 0) _slots.Dispose();
        }
        _sweep.Dispose();
        try { _stop.Cancel(); }
        catch (AggregateException) { /* 关闭时已阻止新的任务。 */ }
        finally { _stop.Dispose(); }
    }

    private sealed class Entry(string id, McpGrant grant, CancellationToken owner)
    {
        internal string Id { get; } = id;
        internal McpGrant Grant { get; } = grant;
        internal CancellationToken Owner { get; } = owner;
        internal string Status { get; set; } = "queued";
        internal int? Total { get; set; }
        internal int Completed { get; set; }
        internal string? Error { get; set; }
        internal long? FinishedAt { get; set; }
    }
}
