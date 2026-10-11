namespace Asgard.Hermod.UI.Tests;

/// <summary>在创建边界模拟成功和失败，并记录每个独立会话。</summary>
internal sealed class FakeTerminalSessionFactory : ITerminalSessionFactory
{
    /// <summary>已经成功创建的工作区会话。</summary>
    internal List<FakeTerminalWorkspaceSession> Sessions { get; } = [];

    /// <summary>终端创建尝试的总次数，包含失败的尝试。</summary>
    internal int OpenAttempts { get; private set; }

    /// <summary>暂停后续新会话的释放，以检查生命周期等待。</summary>
    internal bool HoldDisposal { get; set; }

    /// <summary>下次启动时抛出的异常，模拟环境或进程创建失败。</summary>
    internal Exception? NextFailure { get; set; }

    /// <inheritdoc />
    public ITerminalWorkspaceSession OpenLocal()
    {
        OpenAttempts++;
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }

        var session = new FakeTerminalWorkspaceSession(HoldDisposal);
        Sessions.Add(session);
        return session;
    }

    /// <summary>失败断言后的清理也能继续，不让测试遗留未完成的任务。</summary>
    internal void ReleaseAllDisposals()
    {
        foreach (var session in Sessions)
            session.ReleaseDisposal();
    }
}
