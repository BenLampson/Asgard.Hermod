namespace Asgard.Hermod.UI.Tests;

/// <summary>更新行为替身只提供查询和打开页面能力，不具备连接或安装操作。</summary>
internal sealed class UpdateTestService : IApplicationUpdateService
{
    public Version CurrentVersion => new(1, 0, 0, 0);
    public bool UsesStore => true;
    public int CheckCount { get; private set; }
    public int OpenCount { get; private set; }
    public Func<CancellationToken, Task<ApplicationUpdateResult>> Check { get; set; } = _ =>
        Task.FromResult(new ApplicationUpdateResult(true, new(new Version(2, 0, 0, 0), "Release notes")));
    public Task<ApplicationUpdateResult> CheckAsync(CancellationToken cancellationToken) { CheckCount++; return Check(cancellationToken); }
    public void OpenDownloadPage() => OpenCount++;
}
