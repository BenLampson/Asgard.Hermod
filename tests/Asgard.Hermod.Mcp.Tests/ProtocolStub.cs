using System.Reflection;

namespace Asgard.Hermod.Mcp.Tests;

/// <summary>替代远程协议边界；任何未明确安排的调用（包括写入）都视为失败。</summary>
public class ProtocolStub<T> : DispatchProxy where T : class
{
    private Func<string, object?[], object?> _handler = null!;
    internal static T For(Func<string, object?[], object?>? handler = null)
    {
        var proxy = Create<T, ProtocolStub<T>>();
        ((ProtocolStub<T>)(object)proxy)._handler = handler ?? ((name, _) => throw new InvalidOperationException(name));
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
    {
        "DisposeAsync" => ValueTask.CompletedTask,
        "Dispose" => null,
        var name => _handler(name, args ?? []),
    };
}
