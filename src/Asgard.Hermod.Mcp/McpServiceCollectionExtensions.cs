using Asgard.Hermod.MySql;
using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.RabbitMq;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace Asgard.Hermod.Mcp;

/// <summary>装配授权 MCP 工具；调用与桌面工作区拥有独立网络会话。</summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>由桌面宿主提供本机 CLI 的注册命令。</summary>
    public static IServiceCollection AddHermodMcp(this IServiceCollection services, string command)
    {
        services.AddSingleton(provider => new McpAccessService(provider.GetRequiredService<IMcpLocalStore>(), command));
        services.AddSingleton<IMcpSettingsService>(provider => provider.GetRequiredService<McpAccessService>());
        services.AddKeyedSingleton<ISshTcpStreamFactory>("mcp", (provider, _) =>
            new SshTcpStreamFactory(provider.GetRequiredService<IHostKeyTrustStore>(), new McpSshTrustPrompt()));
        services.AddSingleton(provider => new McpSqlTools(new MySqlSessionFactory(Tunnel(provider)), new PostgreSqlSessionFactory(Tunnel(provider))));
        services.AddSingleton<McpStorageTools>();
        services.AddSingleton<McpStorageTransfer>();
        services.AddSingleton(provider => new McpStorageTransferJobs(provider.GetRequiredService<McpAccessService>(),
            provider.GetRequiredService<McpStorageTransfer>(), TimeProvider.System));
        services.AddSingleton<McpSshTools>();
        services.AddSingleton(provider => new McpRedisTools(new RedisSessionFactory(Tunnel(provider))));
        services.AddSingleton(provider => new McpRabbitMqTools(new RabbitMqSessionFactory(provider.GetRequiredService<IHttpClientFactory>(), Tunnel(provider))));
        services.AddSingleton<McpTools>();
        services.AddSingleton<McpProtocolSession>();
        services.AddSingleton(provider => new McpDesktopServer(provider.GetRequiredService<McpAccessService>(),
            provider.GetRequiredService<McpProtocolSession>(), provider.GetService<IAccountService>()));
        return services;
    }

    private static ISshTcpStreamFactory Tunnel(IServiceProvider services) => services.GetRequiredKeyedService<ISshTcpStreamFactory>("mcp");
}
