using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>把标准条件写入映射为厂商支持的禁止覆盖头，不检查厂商品牌或业务文案。</summary>
internal sealed class S3ConditionalWriteHandler(string header) : PipelineHandler
{
    /// <inheritdoc />
    public override Task<T> InvokeAsync<T>(IExecutionContext executionContext)
    {
        Apply(executionContext);
        return base.InvokeAsync<T>(executionContext);
    }

    /// <inheritdoc />
    public override void InvokeSync(IExecutionContext executionContext)
    {
        Apply(executionContext);
        base.InvokeSync(executionContext);
    }

    /// <summary>完成分片与小文件 PUT 都在远端提交时携带同一条件。</summary>
    private void Apply(IExecutionContext context)
    {
        if (context.RequestContext.OriginalRequest is PutObjectRequest { IfNoneMatch: "*" } or
            CompleteMultipartUploadRequest { IfNoneMatch: "*" })
            context.RequestContext.Request.Headers[header] = "true";
    }
}
