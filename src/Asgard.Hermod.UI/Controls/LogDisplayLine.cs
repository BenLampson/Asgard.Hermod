namespace Asgard.Hermod.UI.Controls;

/// <summary>原生字符坐标确定的可见日志行，索引始终指向原始文本。</summary>
internal readonly record struct LogDisplayLine(int Start, int End, int PhysicalStart, int PhysicalNumber, Rect Bounds);
