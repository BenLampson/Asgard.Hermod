using System.Diagnostics;
using System.Net.Http;
using System.Windows.Interop;
using Asgard.Hermod.Abstractions.Services;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Asgard.Hermod.Updates;

/// <summary>使用商店官方只读 API 检查 MSIX；未打包的程序查询公开稳定发布。</summary>
public sealed class WindowsApplicationUpdateService : IApplicationUpdateService, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Func<Window?> _owner;

    /// <summary>获取实际包版本，普通开发运行不要求安装包身份。</summary>
    public WindowsApplicationUpdateService(Func<Window?> owner)
    {
        _owner = owner;
        CurrentVersion = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        try
        {
            var version = Package.Current.Id.Version;
            CurrentVersion = new Version(version.Major, version.Minor, version.Build, version.Revision);
            UsesStore = true;
        }
        catch (InvalidOperationException) { /* 无包身份时使用 GitHub 分发渠道。 */ }
    }

    /// <inheritdoc />
    public Version CurrentVersion { get; }
    /// <inheritdoc />
    public bool UsesStore { get; }

    /// <inheritdoc />
    public async Task<ApplicationUpdateResult> CheckAsync(CancellationToken cancellationToken)
    {
        bool storeUpdate = false;
        if (UsesStore)
        {
            var context = StoreContext.GetDefault();
            if (_owner() is { } window)
                WinRT.Interop.InitializeWithWindow.Initialize(context, new WindowInteropHelper(window).Handle);
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            // 过滤其他可选包；Package.Id.Version 是已安装版本，不能冒充目标更新版本。
            storeUpdate = updates.Any(update => update.Package.Id.FamilyName == Package.Current.Id.FamilyName);
        }
        PublishedApplicationRelease? release = null;
        try { release = await new GitHubReleaseClient(_client).ReadAsync(cancellationToken); }
        catch (Exception) when (UsesStore && !cancellationToken.IsCancellationRequested)
        {
            // 发布说明暂时不可用不应隐藏商店已经确认的更新。
        }
        return new(UsesStore ? storeUpdate : release!.Version > CurrentVersion, release);
    }

    /// <inheritdoc />
    public void OpenDownloadPage() => Process.Start(new ProcessStartInfo(UsesStore
        ? "ms-windows-store://pdp/?productid=9P3215BJV74Q"
        : "https://github.com/BenLampson/Asgard.Hermod/releases/latest")
    { UseShellExecute = true });

    /// <summary>退出时释放只读网络客户端。</summary>
    public void Dispose() => _client.Dispose();
}
