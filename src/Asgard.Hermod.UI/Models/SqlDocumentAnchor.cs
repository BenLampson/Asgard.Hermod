using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.UI.Models;

/// <summary>只在发送时保留本地文稿映射；编辑后禁止使用过期位置定位新文稿。</summary>
public sealed record SqlDocumentAnchor(string Document, int DocumentStart, int ExecutionStart = 0)
{
    /// <summary>将发送范围内的位置映射回完整文稿；客户端 DELIMITER 前缀不计入编辑器。</summary>
    public SqlErrorLocation? Map(SqlErrorLocation? location)
    {
        if (location is null || location.Offset < ExecutionStart) return null;
        long offset = (long)DocumentStart + location.Offset - ExecutionStart;
        if (offset < 0 || offset > Document.Length) return null;
        return SqlErrorLocation.AtOffset(Document, location.Statement, (int)offset, location.IsExact);
    }
}
