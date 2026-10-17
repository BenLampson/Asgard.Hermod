namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>保留输入与尺寸更新的发送顺序。</summary>
internal readonly record struct ConPtyWriteRequest(byte[]? Input, TerminalSize? Size);
