namespace Asgard.Hermod.UI.Tests;

/// <summary>可控的连接器替身，不执行网络请求并允许验证取消与迟到结果。</summary>
internal sealed class NewInstanceWizardTestProvider(string id = "aliyun-oss", string name = "阿里云 OSS",
    InstanceTypeDescriptor? type = null) : IInstanceProvider
{
    /// <summary>存储处于顶层，OSS 是其下的具体服务。</summary>
    public static InstanceTypeDescriptor StorageType { get; } = new("storage", "存储", "对象存储服务", "box");
    /// <summary>向导从真实注册表契约读取此展示信息。</summary>
    public InstanceProviderDescriptor Descriptor { get; } = new(id, name, "Object Storage Service", "cloud", type ?? StorageType,
        [new("accessKeyId", "AccessKey ID（AK）", MaxLength: 128, IsSensitive: true,
            ValidationRule: InstanceFieldValidationRule.AsciiLettersAndDigits),
         new("accessKeySecret", "AccessKey Secret（SK）", InstanceFieldKind.Secret, MaxLength: 256,
            Hint: "SK 默认隐藏，不记录到日志。", IsSensitive: true)])
    {
        ConnectionTestDescription = "验证 Bucket 列表访问，并在可用 Bucket 写入临时对象、回读核对后清理；仅验证通过的 Bucket。",
    };
    /// <summary>记录连接测试调用次数。</summary>
    public int CallCount { get; private set; }
    /// <summary>记录传给边界的取消令牌。</summary>
    public CancellationToken LastCancellationToken { get; private set; }
    /// <summary>记录传给连接器的配置，测试不将凭据写入输出。</summary>
    public InstanceConnectionOptions? LastConnection { get; private set; }
    /// <summary>模拟连接器冻结输入文件等设备资源。</summary>
    public Func<InstanceConnectionOptions, InstanceConnectionOptions>? PreparationHandler { get; set; }
    /// <inheritdoc />
    public Task<InstanceConnectionOptions> PrepareConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(PreparationHandler?.Invoke(options) ?? options);
    /// <summary>测试可替换完成时机及结果。</summary>
    public Func<InstanceConnectionOptions, CancellationToken, Task<InstanceConnectionTestResult>> TestHandler { get; set; } =
        (_, _) => Task.FromResult(new InstanceConnectionTestResult(true, "阿里云 OSS 已响应", Checks:
            [new("AccessKey 身份认证", true), new("Bucket 列表读取", true), new("临时对象写入", true),
             new("临时对象回读", true), new("临时对象清理", true)]));

    /// <summary>只记录边界调用，然后执行用例指定的结果。</summary>
    public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastCancellationToken = cancellationToken;
        LastConnection = options;
        return TestHandler(options, cancellationToken);
    }
}
