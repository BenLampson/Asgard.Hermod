using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.UI.Models;

/// <summary>一次执行的安全消息快照；定位只消费数字与本地原文，不解析异常消息。</summary>
public sealed class DatabaseOutputMessage(DateTimeOffset time, bool succeeded, string message, SqlErrorLocation? location = null, SqlDocumentAnchor? anchor = null)
{
    private readonly SqlErrorLocation? _mapped = anchor?.Map(location);
    /// <summary>时间使用当前区域格式，不改变协议数值。</summary>
    public string TimeLabel => time.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
    /// <summary>状态来自执行结果，不比较翻译。</summary>
    public string StatusLabel => Text.Get(succeeded ? "DatabaseMessageSucceeded" : "DatabaseMessageFailed");
    /// <summary>仅包含已本地化的安全错误或成功模板。</summary>
    public string Message { get; } = message;
    /// <summary>文稿映射可验证时展示行号，否则只显示失败语句序号。</summary>
    public string PositionLabel => _mapped is { } mapped ? Text.Format("DatabaseMessageLine", mapped.Line) : location is { } item ? Text.Format("DatabaseMessageStatement", item.Statement) : Text.Get("DatabaseMessageNoPosition");
    /// <summary>明确区分服务器精确位置与语句起点。</summary>
    public string PositionHint => Text.Get(_mapped is { IsExact: true } ? "DatabaseMessageExactPosition" : "DatabaseMessageStatementStart");
    /// <summary>用于底栏显示的可验证行号。</summary>
    public int? Line => _mapped?.Line;
    /// <summary>文稿一旦修改，旧执行位置失效；不会查找相似 SQL 或移动到猜测位置。</summary>
    public int? GetOffset(string document) => _mapped is { } mapped && anchor is not null && string.Equals(document, anchor.Document, StringComparison.Ordinal) ? mapped.Offset : null;
}
