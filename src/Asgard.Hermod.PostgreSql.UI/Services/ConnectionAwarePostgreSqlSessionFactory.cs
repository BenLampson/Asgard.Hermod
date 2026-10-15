namespace Asgard.Hermod.PostgreSql.UI.Services;

/// <summary>元数据与独立查询会话统一报告连接失败；不会自动重放查询或写入。</summary>
internal sealed class ConnectionAwarePostgreSqlSessionFactory(IPostgreSqlSessionFactory inner, Action<int> connectionLost) : IPostgreSqlSessionFactory
{
    private int _generation;
    /// <summary>每次显式重连切换代次，页面线程再检查一次以丢弃排队的旧通知。</summary>
    public int Generation => _generation;
    /// <summary>重连前使旧会话及已排队回调失效。</summary>
    public void Reset() => _generation++;
    /// <inheritdoc />
    public async Task<IPostgreSqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        int generation = _generation;
        try
        {
            var session = await inner.OpenAsync(options, cancellationToken);
            session.ConnectionLost += (_, _) => { if (generation == _generation) connectionLost(generation); };
            return session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { if (!cancellationToken.IsCancellationRequested && generation == _generation) connectionLost(generation); throw; }
    }
}
