// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>增量解析 VT500 转义序列与 UTF-8 字节；不完整序列保留到下次输入。</summary>
public sealed partial class VtDecoder : IDisposable
{
    /// <summary>表示 State 的终端协议数据。</summary>
    private enum State : byte
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscString,
        DcsEntry,
        DcsParam,
        DcsIntermediate,
        DcsPassthrough,
        SosPmApcString,
    }

    /// <summary>保存或提供 ESC 对应的终端状态。</summary>
    private const byte ESC = 0x1B;
    /// <summary>保存或提供 BEL 对应的终端状态。</summary>
    private const byte BEL = 0x07;
    /// <summary>保存或提供 Backslash 对应的终端状态。</summary>
    private const byte Backslash = (byte)'\\';
    /// <summary>VT 指令事件接收器。</summary>
    private readonly IVtHandler _handler;
    /// <summary>跨数据块的 UTF-8 字节。</summary>
    private readonly List<byte> _utf8 = new(16);
    /// <summary>CSI/DCS 参数值。</summary>
    private int[] _paramValues = new int[16];
    /// <summary>子参数标记。</summary>
    private bool[] _paramSub = new bool[16];
    /// <summary>有效参数数量。</summary>
    private int _paramCount;
    /// <summary>正在累积的数字参数。</summary>
    private int _accumulator;
    /// <summary>当前参数是否已包含数字。</summary>
    private bool _hasParam;
    /// <summary>当前参数是否为冒号子参数。</summary>
    private bool _currentIsSub;
    /// <summary>末尾是否为参数分隔符。</summary>
    private bool _trailingSep;
    /// <summary>转义指令的中间字节。</summary>
    private byte[] _intermediates = new byte[8];
    /// <summary>中间字节数量。</summary>
    private int _interCount;
    /// <summary>DEC 私有指令标记。</summary>
    private char _privateMarker;
    /// <summary>池化的 OSC/DCS 字符串负载。</summary>
    private byte[]? _stringBuf;
    /// <summary>有效字符串负载长度。</summary>
    private int _stringLength;
    /// <summary>负载缓冲容量。</summary>
    private int _stringCapacity;
    /// <summary>字符串中等待 ST 结束符的 ESC 状态。</summary>
    private bool _stringSawEsc;
    /// <summary>DCS 最终指令字节。</summary>
    private byte _dcsFinal;
    /// <summary>解析状态。</summary>
    private State _state = State.Ground;
    /// <summary>资源是否已释放。</summary>
    private bool _disposed;
    /// <summary>创建增量解析器，并验证事件接收器。</summary>
    public VtDecoder(IVtHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <summary>字节编码。</summary>
    public Encoding Encoding { get => Encoding.UTF8; }

    /// <summary>增量处理输入字节，保留跨数据块的解析状态。</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            if (_state == State.Ground && b != ESC && b >= 0x20 && b != 0x7F)
            {
                int runStart = i;
                do
                {
                    i++;
                }
                while (i < data.Length && data[i] != ESC && data[i] >= 0x20 && data[i] != 0x7F);
                try
                {
                    ProcessPrintRun(data[runStart..i]);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"VtDecoder recovered from an unexpected error processing a text run: {ex}");
                    _utf8.Clear();
                    ResetToGround();
                }

                continue;
            }

            try
            {
                ProcessByte(b);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VtDecoder recovered from an unexpected error processing byte {b:X2}: {ex}");
                ResetToGround();
            }

            i++;
        }
    }

    /// <summary>处理 ProcessByte 对应的终端协议或渲染逻辑。</summary>
    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                ProcessGround(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.EscapeIntermediate:
                ProcessEscapeIntermediate(b);
                break;
            case State.CsiEntry:
                ProcessCsiEntry(b);
                break;
            case State.CsiParam:
                ProcessCsiParam(b);
                break;
            case State.CsiIntermediate:
                ProcessCsiIntermediate(b);
                break;
            case State.OscString:
                ProcessOscString(b);
                break;
            case State.DcsEntry:
                ProcessDcsEntry(b);
                break;
            case State.DcsParam:
                ProcessDcsParam(b);
                break;
            case State.DcsIntermediate:
                ProcessDcsIntermediate(b);
                break;
            case State.DcsPassthrough:
                ProcessDcsPassthrough(b);
                break;
            case State.SosPmApcString:
                ProcessSosPmApc(b);
                break;
        }
    }

    /// <summary>处理 ProcessGround 对应的终端协议或渲染逻辑。</summary>
    private void ProcessGround(byte b)
    {
        if (b == ESC)
        {
            FlushUtf8();
            _state = State.Escape;
            return;
        }

        if (b < 0x20 || b == 0x7F)
        {
            FlushUtf8();
            _handler.Execute(b);
            return;
        }

        _utf8.Add(b);
        DecodeUtf8ToHandler(flush: false);
    }

    /// <summary>处理 ProcessEscape 对应的终端协议或渲染逻辑。</summary>
    private void ProcessEscape(byte b)
    {
        switch (b)
        {
            case (byte)'[':
                BeginCsi();
                _state = State.CsiEntry;
                return;
            case (byte)']':
                BeginString();
                _state = State.OscString;
                return;
            case (byte)'P':
                BeginCsi();
                _state = State.DcsEntry;
                return;
            case (byte)'X':
            case (byte)'^':
            case (byte)'_':
                BeginString();
                _dcsFinal = b;
                _state = State.SosPmApcString;
                return;
            case ESC:
                return;
            case >= 0x20 and <= 0x2F:
                PushIntermediate(b);
                _state = State.EscapeIntermediate;
                return;
            case >= 0x30 and <= 0x7E:
                _handler.EscDispatch(b, IntermediatesSpan());
                ResetToGround();
                return;
            default:
                if (b < 0x20)
                    _handler.Execute(b);
                return;
        }
    }

    /// <summary>处理 ProcessEscapeIntermediate 对应的终端协议或渲染逻辑。</summary>
    private void ProcessEscapeIntermediate(byte b)
    {
        switch (b)
        {
            case ESC:
                ResetToGround();
                _state = State.Escape;
                return;
            case >= 0x20 and <= 0x2F:
                PushIntermediate(b);
                return;
            case >= 0x30 and <= 0x7E:
                _handler.EscDispatch(b, IntermediatesSpan());
                ResetToGround();
                return;
            default:
                if (b < 0x20)
                    _handler.Execute(b);
                return;
        }
    }

    /// <summary>处理 ProcessCsiEntry 对应的终端协议或渲染逻辑。</summary>
    private void ProcessCsiEntry(byte b)
    {
        if (IsDigit(b))
        {
            AccumulateDigit(b);
            _state = State.CsiParam;
            return;
        }

        if (b is (byte)';' or (byte)':')
        {
            OnSeparator(b);
            _state = State.CsiParam;
            return;
        }

        if (b is >= 0x3C and <= 0x3F)
        {
            _privateMarker = (char)b;
            _state = State.CsiParam;
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
}
