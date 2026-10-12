using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Asgard.Yggdrasil.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>真实 Yggdrasil 装配验证，防止请求测试宿主掩盖生产注册缺失。</summary>
public sealed class ServerCompositionTests
{
    /// <summary>独立 API 宿主从安全配置完成构建，初始化数据库并可解析服务 Client。</summary>
    [Fact]
    public async Task Yggdrasil_ComposesHermodPluginAndInitializesDatabase()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HermodCompositionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "sync.db").Replace('\\', '/');
        string configPath = Path.Combine(directory, "app.yaml");
        try
        {
            await File.WriteAllTextAsync(configPath, $$"""
                Asgard:
                  Encryption:
                    Key: "{{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}}"
                    Iv: "{{Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))}}"
                database:
                  enabled: false
                caching:
                  enabled: false
                messaging:
                  enabled: false
                job:
                  enabled: false
                host:
                  auth:
                    enabled: true
                    jwt:
                      issuerTemplate: "https://issuer.test/{tenant}"
                      audience: "hermod-api"
                hermodServer:
                  authority: "https://issuer.test/tenant"
                  identityApiBaseUrl: "https://issuer.test"
                  tenantId: "a922e624-c532-43ae-b4a5-c4f9153f24cf"
                  applicationId: "083f96e2-a9d7-4300-9386-f7ef470d2378"
                  desktopClientId: "desktop"
                  serviceClientId: "server"
                  serviceClientSecret: "{{new string('X', 48)}}"
                  databasePath: "{{databasePath}}"
                """, TestContext.Current.CancellationToken);
            await using var app = await YggdrasilHost.CreateBuilder(configPath).UseBuiltInPlugin<HermodServerPlugin>()
                .BuildAsync(TestContext.Current.CancellationToken);
            using var scope = app.Services.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<CurrentHermodUser>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkspaceSyncApplicationService>());
            using var client = app.Services.GetRequiredService<Asgard.Heimdall.Client.HeimdallClientFactory>().Create("hermod-directory");
            Assert.True(File.Exists(databasePath));
            var jwt = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
            var authentication = new TokenValidatedContext(new DefaultHttpContext { RequestServices = scope.ServiceProvider },
                new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), jwt)
            {
                SecurityToken = new JwtSecurityToken(issuer: "https://issuer.test/another-tenant"),
            };
            await jwt.Events.OnTokenValidated(authentication);
            Assert.NotNull(authentication.Result?.Failure);
        }
        finally { Directory.Delete(directory, true); }
    }
}
