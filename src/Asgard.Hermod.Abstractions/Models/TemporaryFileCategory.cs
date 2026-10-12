namespace Asgard.Hermod.Abstractions.Models;

/// <summary>临时文件的稳定分类，不使用翻译文案作为目录或持久化标识。</summary>
public enum TemporaryFileCategory
{
    /// <summary>为查看或打开远程文件生成的临时下载。</summary>
    Download,
    /// <summary>保留给外部程序使用、可在后续会话重新生成的缓存。</summary>
    Cache,
    /// <summary>尚未原子提交的处理中间文件。</summary>
    Processing
}
