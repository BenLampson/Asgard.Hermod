// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>保存 CSI 指令的参数及子参数分隔信息。</summary>
public readonly struct CsiParams
{
    /// <summary>保存或提供 _values 对应的终端状态。</summary>
    private readonly int[] _values;
    /// <summary>保存或提供 _sub 对应的终端状态。</summary>
    private readonly bool[] _sub;
    /// <summary>保存或提供 _count 对应的终端状态。</summary>
    private readonly int _count;
    /// <summary>初始化 CsiParams 实例。</summary>
    internal CsiParams(int[] values, bool[] sub, int count)
    {
        _values = values;
        _sub = sub;
        _count = count;
    }

    /// <summary>保存或提供 Length 对应的终端状态。</summary>
    public int Length => _count;

    /// <summary>保存或提供 索引器 对应的终端状态。</summary>
    public int this[int index] => index >= 0 && index < _count ? _values[index] : 0;
    /// <summary>处理 At 对应的终端协议或渲染逻辑。</summary>
    public int At(int index, int defaultValue) => index >= 0 && index < _count ? _values[index] : defaultValue;
    /// <summary>处理 IsSubParam 对应的终端协议或渲染逻辑。</summary>
    public bool IsSubParam(int index) => index >= 0 && index < _count && _sub[index];
    /// <summary>保存或提供 IsEmpty 对应的终端状态。</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>处理 GetEnumerator 对应的终端协议或渲染逻辑。</summary>
    public Enumerator GetEnumerator() => new(this);
    /// <summary>表示 Enumerator 的终端协议数据。</summary>
    public ref struct Enumerator
    {
        /// <summary>保存或提供 _p 对应的终端状态。</summary>
        private readonly CsiParams _p;
        /// <summary>保存或提供 _i 对应的终端状态。</summary>
        private int _i;
        /// <summary>初始化 Enumerator 实例。</summary>
        internal Enumerator(CsiParams p)
        {
            _p = p;
            _i = -1;
        }

        /// <summary>处理 MoveNext 对应的终端协议或渲染逻辑。</summary>
        public bool MoveNext()
        {
            _i++;
            return _i < _p._count;
        }

        /// <summary>保存或提供 Current 对应的终端状态。</summary>
        public readonly (int Index, int Value, bool IsSubParam) Current => (_i, _p._values[_i], _p._sub[_i]);
    }
}
