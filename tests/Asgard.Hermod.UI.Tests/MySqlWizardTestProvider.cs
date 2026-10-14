using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.UI.Tests;

/// <summary>提供真实 MySQL 元数据和可控测试失败，绝不访问数据库或用户凭据。</summary>
internal sealed class MySqlWizardTestProvider : IInstanceProvider
{
    /// <summary>复用模块声明的全部连接字段。</summary>
    public InstanceProviderDescriptor Descriptor { get; } = MySqlProfile.Descriptor(MySqlIds.Direct);
    /// <summary>返回可预期失败，使测试能够验证返回信息页后重新选择安全选项。</summary>
    public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default) => Task.FromResult(new InstanceConnectionTestResult(false, Text.Get("MySqlConnectionFailed")));
}
