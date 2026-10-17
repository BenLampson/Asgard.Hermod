using Asgard.Hermod.Server;
using Asgard.PluginSdk;

// API 宿主与 WPF 进程完全分离，认证与生命周期统一由 Yggdrasil 承载。
var path = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "app.yaml");
await PluginWebAppDefaults.RunAsync<HermodServerPlugin>(path);
