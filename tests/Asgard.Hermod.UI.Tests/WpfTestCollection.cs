namespace Asgard.Hermod.UI.Tests;

/// <summary>共享一个独立 STA Application，避免 WPF 的进程级应用单例冲突。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfTestCollection : ICollectionFixture<WpfTestFixture>
{
    /// <summary>所有界面回归测试所属的串行集合。</summary>
    public const string Name = "Hermod WPF";
}
