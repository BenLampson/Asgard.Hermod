using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>身份生成只依赖应用命名空间和可执行文件路径，不注册图标、不读写 Windows 注册表。</summary>
public sealed class TrayIconIdentityTests
{
    /// <summary>Windows 路径大小写及等价目录不改变同一安装的托盘身份。</summary>
    [Fact]
    public void SameExecutablePathRetainsIdentityAcrossRestartsAndNormalization()
    {
        var id = TrayIconIdentity.ForExecutable(@"C:\Applications\Hermod\Asgard.Hermod.exe");
        Assert.Equal(id, TrayIconIdentity.ForExecutable(@"c:\applications\Hermod\.\Asgard.Hermod.exe"));
        Assert.NotEqual(Guid.Empty, id);
    }

    /// <summary>测试宿主、开发构建和另一个安装目录不能与正式应用共用托盘 GUID。</summary>
    [Fact]
    public void DistinctHostsAndInstallationsNeverReuseLegacyOrProductionIdentity()
    {
        var id = TrayIconIdentity.ForExecutable(@"C:\Applications\Hermod\Asgard.Hermod.exe");
        Assert.NotEqual(id, TrayIconIdentity.ForExecutable(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.NotEqual(id, TrayIconIdentity.ForExecutable(@"D:\Development\Hermod\Asgard.Hermod.exe"));
        Assert.NotEqual(new Guid("5db3d722-11c6-498d-969a-5fcc8ecad204"), id);
    }
}
