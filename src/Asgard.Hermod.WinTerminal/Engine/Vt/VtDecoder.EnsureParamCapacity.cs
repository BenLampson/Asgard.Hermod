// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>增量解析 VT500 转义序列与 UTF-8 字节；不完整序列保留到下次输入。</summary>
public sealed partial class VtDecoder
{
    /// <summary>处理 EnsureParamCapacity 对应的终端协议或渲染逻辑。</summary>
    private void EnsureParamCapacity()
    {
        if (_paramCount >= _paramValues.Length)
        {
            int newSize = _paramValues.Length * 2;
            Array.Resize(ref _paramValues, newSize);
            Array.Resize(ref _paramSub, newSize);
        }
    }

    /// <summary>处理 PushIntermediate 对应的终端协议或渲染逻辑。</summary>
    private void PushIntermediate(byte b)
    {
        if (_interCount >= _intermediates.Length)
            Array.Resize(ref _intermediates, _intermediates.Length * 2);
        _intermediates[_interCount++] = b;
    }

    /// <summary>处理 IntermediatesSpan 对应的终端协议或渲染逻辑。</summary>
    private ReadOnlySpan<byte> IntermediatesSpan() => _interCount > 0 ? _intermediates.AsSpan(0, _interCount) : ReadOnlySpan<byte>.Empty;
    /// <summary>处理 BeginCsi 对应的终端协议或渲染逻辑。</summary>
    private void BeginCsi()
    {
        _paramCount = 0;
        _accumulator = 0;
        _hasParam = false;
        _currentIsSub = false;
        _trailingSep = false;
        _interCount = 0;
        _privateMarker = '\0';
    }

    /// <summary>处理 BeginString 对应的终端协议或渲染逻辑。</summary>
    private void BeginString()
    {
        _stringLength = 0;
        _stringSawEsc = false;
    }

    /// <summary>处理 StringSpan 对应的终端协议或渲染逻辑。</summary>
    private ReadOnlySpan<byte> StringSpan() => _stringLength > 0 ? _stringBuf!.AsSpan(0, _stringLength) : ReadOnlySpan<byte>.Empty;
    /// <summary>处理 AddStringByte 对应的终端协议或渲染逻辑。</summary>
    private void AddStringByte(byte b)
    {
        if (_stringBuf is null || _stringLength >= _stringCapacity)
            GrowStringBuffer();
        _stringBuf![_stringLength++] = b;
    }

    /// <summary>处理 GrowStringBuffer 对应的终端协议或渲染逻辑。</summary>
    private void GrowStringBuffer()
    {
        int newCapacity = Math.Max(64, _stringCapacity * 2);
        byte[] newBuf = ArrayPool<byte>.Shared.Rent(newCapacity);
        if (_stringBuf is not null && _stringLength > 0)
            _stringBuf.AsSpan(0, _stringLength).CopyTo(newBuf);
        if (_stringBuf is not null)
            ArrayPool<byte>.Shared.Return(_stringBuf);
        _stringBuf = newBuf;
        _stringCapacity = newBuf.Length;
    }

    /// <summary>处理 ResetStringBuffer 对应的终端协议或渲染逻辑。</summary>
    private void ResetStringBuffer()
    {
        _stringLength = 0;
        _stringSawEsc = false;
    }

    /// <summary>处理 FreeStringBuffer 对应的终端协议或渲染逻辑。</summary>
    private void FreeStringBuffer()
    {
        if (_stringBuf is not null)
        {
            ArrayPool<byte>.Shared.Return(_stringBuf);
            _stringBuf = null;
            _stringCapacity = 0;
            _stringLength = 0;
        }
    }

    /// <summary>处理 ResetToGround 对应的终端协议或渲染逻辑。</summary>
    private void ResetToGround()
    {
        _state = State.Ground;
        _paramCount = 0;
        _accumulator = 0;
        _hasParam = false;
        _currentIsSub = false;
        _trailingSep = false;
        _interCount = 0;
        _privateMarker = '\0';
        ResetStringBuffer();
    }

    /// <summary>处理 ProcessPrintRun 对应的终端协议或渲染逻辑。</summary>
    private void ProcessPrintRun(ReadOnlySpan<byte> run)
    {
        _utf8.AddRange(run);
        DecodeUtf8ToHandler(flush: false);
    }

    /// <summary>处理 DecodeUtf8ToHandler 对应的终端协议或渲染逻辑。</summary>
    private void DecodeUtf8ToHandler(bool flush)
    {
        if (_utf8.Count == 0)
            return;
        Rune[] rented = ArrayPool<Rune>.Shared.Rent(_utf8.Count);
        try
        {
            int runeCount = 0;
            int consumed = 0;
            ReadOnlySpan<byte> buffer = CollectionsMarshal.AsSpan(_utf8);
            while (!buffer.IsEmpty)
            {
                OperationStatus status = Rune.DecodeFromUtf8(buffer, out Rune rune, out int n);
                if (status == OperationStatus.NeedMoreData && !flush)
                    break;
                if (n <= 0)
                {
                    n = 1;
                    rune = new Rune(0xFFFD);
                }

                rented[runeCount++] = rune;
                buffer = buffer[n..];
                consumed += n;
            }

            if (runeCount > 0)
                _handler.Print(rented.AsSpan(0, runeCount));
            if (consumed > 0)
                _utf8.RemoveRange(0, consumed);
        }
        finally
        {
            ArrayPool<Rune>.Shared.Return(rented);
        }
    }

    /// <summary>处理 FlushUtf8 对应的终端协议或渲染逻辑。</summary>
    private void FlushUtf8()
    {
        DecodeUtf8ToHandler(flush: true);
        _utf8.Clear();
    }

    /// <summary>恢复初始终端状态。</summary>
    public void Reset()
    {
        FlushUtf8();
        ResetToGround();
    }

    /// <summary>释放缓冲区、解析器或字体缓存资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        Reset();
        FreeStringBuffer();
        _disposed = true;
    }
}
