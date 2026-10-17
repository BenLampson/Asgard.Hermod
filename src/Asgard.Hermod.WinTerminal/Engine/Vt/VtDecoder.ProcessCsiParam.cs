// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>增量解析 VT500 转义序列与 UTF-8 字节；不完整序列保留到下次输入。</summary>
public sealed partial class VtDecoder
{
    /// <summary>处理 ProcessCsiParam 对应的终端协议或渲染逻辑。</summary>
    private void ProcessCsiParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            return;
        }

        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.CsiIntermediate;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            DispatchCsi(b);
            return;
        }

        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
            return;
        }

        if (b < 0x20)
            _handler.Execute(b);
    }

    /// <summary>处理 ProcessCsiIntermediate 对应的终端协议或渲染逻辑。</summary>
    private void ProcessCsiIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            DispatchCsi(b);
            return;
        }

        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
            return;
        }

        if (b < 0x20)
            _handler.Execute(b);
    }

    /// <summary>处理 DispatchCsi 对应的终端协议或渲染逻辑。</summary>
    private void DispatchCsi(byte final)
    {
        PushTrailingParam();
        _handler.CsiDispatch(final, IntermediatesSpan(), new CsiParams(_paramValues, _paramSub, _paramCount), _privateMarker);
        ResetToGround();
    }

    /// <summary>处理 ProcessOscString 对应的终端协议或渲染逻辑。</summary>
    private void ProcessOscString(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.OscDispatch(StringSpan());
                ResetToGround();
                return;
            }

            _handler.OscDispatch(StringSpan());
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == BEL)
        {
            _handler.OscDispatch(StringSpan());
            ResetToGround();
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        AddStringByte(b);
    }

    /// <summary>处理 ProcessDcsEntry 对应的终端协议或渲染逻辑。</summary>
    private void ProcessDcsEntry(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            _state = State.DcsParam;
            return;
        }

        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            _state = State.DcsParam;
            return;
        }

        if (b is >= 0x3C and <= 0x3F)
        {
            _privateMarker = (char)b;
            _state = State.DcsParam;
            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.DcsIntermediate;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }

        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    /// <summary>处理 ProcessDcsParam 对应的终端协议或渲染逻辑。</summary>
    private void ProcessDcsParam(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            return;
        }

        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            return;
        }

        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            _state = State.DcsIntermediate;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }

        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    /// <summary>处理 ProcessDcsIntermediate 对应的终端协议或渲染逻辑。</summary>
    private void ProcessDcsIntermediate(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            PushIntermediate(b);
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            EnterDcsPassthrough(b);
            return;
        }

        if (b == ESC)
        {
            ResetToGround();
            _state = State.Escape;
        }
    }

    /// <summary>处理 EnterDcsPassthrough 对应的终端协议或渲染逻辑。</summary>
    private void EnterDcsPassthrough(byte final)
    {
        PushTrailingParam();
        _dcsFinal = final;
        _handler.DcsHook(final, IntermediatesSpan(), new CsiParams(_paramValues, _paramSub, _paramCount), _privateMarker);
        BeginString();
        _state = State.DcsPassthrough;
    }

    /// <summary>处理 ProcessDcsPassthrough 对应的终端协议或渲染逻辑。</summary>
    private void ProcessDcsPassthrough(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.DcsUnhook();
                ResetToGround();
                return;
            }

            _handler.DcsUnhook();
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        _handler.DcsPut(b);
    }

    /// <summary>处理 ProcessSosPmApc 对应的终端协议或渲染逻辑。</summary>
    private void ProcessSosPmApc(byte b)
    {
        if (_stringSawEsc)
        {
            _stringSawEsc = false;
            if (b == Backslash)
            {
                _handler.SosPmApcDispatch(_dcsFinal, StringSpan());
                ResetToGround();
                return;
            }

            _handler.SosPmApcDispatch(_dcsFinal, StringSpan());
            ResetToGround();
            _state = State.Escape;
            ProcessEscape(b);
            return;
        }

        if (b == BEL)
        {
            _handler.SosPmApcDispatch(_dcsFinal, StringSpan());
            ResetToGround();
            return;
        }

        if (b == ESC)
        {
            _stringSawEsc = true;
            return;
        }

        AddStringByte(b);
    }

    /// <summary>处理 IsDigit 对应的终端协议或渲染逻辑。</summary>
    private static bool IsDigit(byte b) => b >= 0x30 && b <= 0x39;
    /// <summary>处理 AccumulateDigit 对应的终端协议或渲染逻辑。</summary>
    private void AccumulateDigit(byte b)
    {
        _accumulator = _accumulator * 10 + (b - 0x30);
        _hasParam = true;
        _trailingSep = false;
    }

    /// <summary>处理 OnSeparator 对应的终端协议或渲染逻辑。</summary>
    private void OnSeparator(byte sep)
    {
        PushCurrentParam();
        _currentIsSub = sep == ':';
        _trailingSep = true;
    }

    /// <summary>处理 PushCurrentParam 对应的终端协议或渲染逻辑。</summary>
    private void PushCurrentParam()
    {
        EnsureParamCapacity();
        _paramValues[_paramCount] = _hasParam ? _accumulator : 0;
        _paramSub[_paramCount] = _currentIsSub;
        _paramCount++;
        _accumulator = 0;
        _hasParam = false;
    }

    /// <summary>处理 PushTrailingParam 对应的终端协议或渲染逻辑。</summary>
    private void PushTrailingParam()
    {
        if (_hasParam || _trailingSep)
            PushCurrentParam();
    }
}
