using System.Globalization;

namespace Asgard.Hermod.UI.Models;

/// <summary>文件类型的官方图标、主题色与本地化名称，不代表文件可预览或执行。</summary>
public sealed record FileTypeInfo(string Icon, string BrushKey, string LabelKey, string Extension = "")
{
    /// <summary>完整句式由资源决定，扩展名作为协议标识保持不翻译。</summary>
    public string DisplayName => Extension.Length == 0 || LabelKey == "FileTypePdf" ? Text.Get(LabelKey)
        : Text.Format("FileTypeWithExtension", Text.Get(LabelKey), Extension.ToUpper(CultureInfo.InvariantCulture));
}
