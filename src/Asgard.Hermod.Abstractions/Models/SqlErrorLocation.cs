using System.Text;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>执行文本内的零基 UTF-16 偏移与一基行列；不携带 SQL、服务器消息或连接信息。</summary>
public sealed record SqlErrorLocation(int Statement, int Offset, int Line, int Column, bool IsExact)
{
    /// <summary>协议位置相对于已 Trim 的单条语句；PostgreSQL 按 Unicode 字符计数，WPF 按 UTF-16 计数。</summary>
    public static SqlErrorLocation? InStatement(string document, int start, int length, int statement, int? serverPosition = null)
    {
        if (statement < 1 || start < 0 || length <= 0 || start > document.Length - length) return null;
        var raw = document.AsSpan(start, length); int leading = raw.Length - raw.TrimStart().Length;
        var text = raw.Trim(); if (text.IsEmpty) return null;
        int relative = 0; bool exact = false;
        if (serverPosition is > 0)
        {
            int remaining = serverPosition.Value - 1;
            foreach (var rune in text.EnumerateRunes()) { if (remaining == 0) break; relative += rune.Utf16SequenceLength; remaining--; }
            if (remaining == 0) exact = true; else relative = 0;
        }
        return AtOffset(document, statement, start + leading + relative, exact);
    }
    /// <summary>从已验证的 UTF-16 文稿偏移计算一基行列，不受当前显示语言影响。</summary>
    public static SqlErrorLocation? AtOffset(string document, int statement, int offset, bool exact)
    {
        if (statement < 1 || offset < 0 || offset > document.Length) return null;
        int line = 1, column = 1;
        foreach (var rune in document.AsSpan(0, offset).EnumerateRunes())
        {
            if (rune.Value == '\n') { line++; column = 1; }
            else if (rune.Value != '\r') column++;
        }
        return new(statement, offset, line, column, exact);
    }
}
