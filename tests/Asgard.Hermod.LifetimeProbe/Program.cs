using Asgard.Hermod.Lifetime;

namespace Asgard.Hermod.LifetimeProbe;

/// <summary>独立进程协议探针，只使用隔离实例标识，不创建数据库、提供者或窗口。</summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        await using var instance = await SingleInstanceCoordinator.AcquireOrActivateAsync(args[0]);
        if (instance is null) { Console.WriteLine("secondary"); return; }
        Console.WriteLine("owner");
        while (await Console.In.ReadLineAsync() is { } command)
        {
            if (command == "ready") instance.SetActivationHandler(() => Console.WriteLine("activation"));
            else if (command == "exiting") { instance.BeginExit(); Console.WriteLine("exiting"); }
            else if (command == "exit") break;
        }
    }
}
