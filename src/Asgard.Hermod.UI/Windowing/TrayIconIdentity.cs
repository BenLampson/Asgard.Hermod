using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>Windows 将托盘 GUID 绑定到可执行文件路径；不同宿主及安装目录必须使用不同身份。</summary>
internal static class TrayIconIdentity
{
    /// <summary>同一路径重启保持稳定；迁移安装目录及旧测试宿主绑定不会阻止新程序注册。</summary>
    internal static Guid ForExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string identity = "Asgard.Hermod.Tray.v2|" + Path.GetFullPath(path).ToUpperInvariant();
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }
}
