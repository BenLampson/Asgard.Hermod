namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>所有命令只写入当前页签，任何失败保留编辑缓冲。</summary>
public sealed partial class RedisKeyTabViewModel
{
    /// <summary>一页一次读取，关闭页签后不回填；草稿存在时读取最新值只用于对比。</summary>
    public async Task ReadAsync(bool next = false)
    {
        if (!ConnectionAvailable || IsBusy || IsDirty || _lifetime.IsCancellationRequested) return;
        int generation = ++_readGeneration;
        IsBusy = true; Error = ""; RefreshState();
        try
        {
            var result = await _session.ReadAsync(Database, Key, next ? _snapshot.Cursor : "0", next ? _snapshot.Offset : 0, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || generation != _readGeneration) return;
            Render(result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { SetError(error); }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task EditAsync()
    {
        if (!ConnectionAvailable) return;
        IsBusy = true; Error = ""; RefreshState();
        try
        {
            var baseline = await _session.ReadEditableAsync(Database, Key, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            if (!baseline.IsComplete) { Render(baseline); throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT"); }
            if (baseline.Type == "none") { Render(baseline); return; }
            _baseline = baseline; IsEditing = true; Render(baseline, true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { SetError(error); }
        finally { IsBusy = false; RefreshState(); }
    }

    /// <summary>Ctrl+S 与按钮共用此操作，不因格式切换产生重复写入。</summary>
    public async Task<bool> SaveAsync()
    {
        if (!CanSave || _baseline is null) return false;
        IsBusy = true; Error = ""; RefreshState();
        try
        {
            var draft = Draft();
            if (IsStream) await _session.AppendAsync(Database, Key, draft.Entries, _lifetime.Token);
            else await _session.SaveAsync(Database, Key, _baseline, draft, _lifetime.Token);
            IsDirty = false; IsEditing = false; _baseline = null;
            if (!IsStream) Render(draft);
            var latest = await _session.ReadAsync(Database, Key, cancellationToken: _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) Render(latest);
            return true;
        }
        catch (Exception error) { SetError(error); return false; }
        finally { IsBusy = false; RefreshState(); }
    }

    private async Task RefreshAsync()
    {
        if (!ConnectionAvailable) return;
        if (!IsDirty)
        {
            IsEditing = false; _baseline = null; await ReadAsync();
            if (Error.Length == 0) { _unknownWrite = false; _permissionDenied = false; RefreshState(); }
            return;
        }
        IsBusy = true; RefreshState();
        try
        {
            var latest = await _session.ReadEditableAsync(Database, Key, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            await _dialogs.ShowLatestAsync(FullName, RedisPreview.Format(latest, Format));
            // 无结果写入只在确认服务器现值与草稿相同后解除；Stream 追加不可自动重放。
            if (_unknownWrite && !IsStream && latest.IsComplete && RedisPreview.Equal(latest, Draft()))
            { _unknownWrite = false; IsDirty = false; IsEditing = false; _baseline = null; Render(latest); Error = Text.Get("RedisLatestMatchesDraft"); }
        }
        catch (Exception error) { SetError(error); }
        finally { IsBusy = false; RefreshState(); }
    }

    private void Discard()
    {
        var baseline = _baseline; IsDirty = false; IsEditing = false; _baseline = null;
        if (baseline is not null) Render(baseline);
        if (_unknownWrite) Error = Text.Get("RedisRetryBlocked");
        RefreshState();
    }

    private void AddEntry(bool head)
    {
        if (Entries.Count >= RedisSnapshot.MaximumEntries) { SetError(new RedisException("RedisValueTooLarge", "REDIS_LIMIT")); return; }
        var entry = new RedisEntryViewModel(new([], [], IsList ? "" : "0"), Format, MarkDirty);
        Entries.Insert(head ? 0 : Entries.Count, entry); MarkDirty();
    }
    private void RemoveEntry(RedisEntryViewModel? entry)
    {
        if (entry is null) return;
        Entries.Remove(entry); MarkDirty();
    }
    private void BeginAppend()
    {
        _baseline = _snapshot; IsEditing = true; IsDirty = true;
        _rendering = true; Entries.Clear(); Entries.Add(new(new([], []), Format, MarkDirty)); _rendering = false;
        Error = ""; RefreshState();
    }

    private async Task ChangeExpiryAsync()
    {
        if (!ConnectionAvailable) return;
        string? value = await _dialogs.ExpiryAsync(FullName);
        if (value is null) return;
        IsBusy = true; Error = ""; RefreshState();
        try
        {
            TimeSpan? expiry = null;
            if (value.Length != 0)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds)
                    || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds) throw Text.Exception("RedisExpiryInvalid");
                expiry = TimeSpan.FromSeconds(seconds);
            }
            await _session.SetExpiryAsync(Database, Key, expiry, _lifetime.Token);
            var latest = await _session.ReadAsync(Database, Key, cancellationToken: _lifetime.Token);
            if (!IsDirty) Render(latest); else _snapshot = _snapshot with { TtlMilliseconds = latest.TtlMilliseconds };
        }
        catch (Exception error) { SetError(error); }
        finally { IsBusy = false; RefreshState(); }
    }
    private async Task DeleteAsync()
    {
        if (!ConnectionAvailable) return;
        if (!await _dialogs.DeleteAsync(FullName, Database, TargetLabel)) return;
        IsBusy = true; Error = ""; RefreshState();
        try
        {
            var baseline = await _session.ReadEditableAsync(Database, Key, _lifetime.Token);
            if (!baseline.IsComplete) throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT");
            await _session.DeleteAsync(Database, Key, baseline, _lifetime.Token);
            Deleted?.Invoke(this);
        }
        catch (Exception error) { SetError(error); }
        finally { IsBusy = false; RefreshState(); }
    }

    /// <summary>关闭脏页签必须明确选择；保存失败时保持页签与草稿。</summary>
    public async Task<bool> RequestCloseAsync()
    {
        if (IsBusy) return false;
        if (!IsDirty) return true;
        return await _dialogs.CloseDirtyAsync(FullName, Database) switch
        {
            RedisCloseChoice.Discard => true,
            RedisCloseChoice.Save => await SaveAsync(),
            _ => false,
        };
    }
}
