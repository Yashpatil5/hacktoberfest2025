using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IPlugin
{
    string Name { get; }
    string Description { get; }
    Task<int> ExecuteAsync(string[] args);
}

class PluginLoadContext : AssemblyLoadContext
{
    private AssemblyDependencyResolver _resolver;
    public PluginLoadContext(string pluginPath) : base(true)
        => _resolver = new AssemblyDependencyResolver(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path == null ? null : LoadFromAssemblyPath(path);
    }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory("plugins");
        var cmd = args.Length == 0 ? "list" : args[0].ToLowerInvariant();
        if (cmd == "list")
        {
            var plugins = DiscoverPlugins().ToList();
            if (!plugins.Any()) { Console.WriteLine("No plugins found in ./plugins"); return 0; }
            foreach (var p in plugins) Console.WriteLine($"{p.Name} - {p.Description}");
            return 0;
        }
        if (cmd == "run")
        {
            if (args.Length < 2) { Console.WriteLine("Usage: run <pluginName> [args...]"); return 1; }
            var name = args[1];
            var plugin = DiscoverPlugins().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (plugin == null) { Console.WriteLine($"Plugin '{name}' not found."); return 2; }
            return await plugin.ExecuteAsync(args.Skip(2).ToArray());
        }
        Console.WriteLine("Unknown command.");
        return 1;
    }

    static IEnumerable<IPlugin> DiscoverPlugins()
    {
        var pluginDir = Path.GetFullPath("plugins");
        foreach (var dll in Directory.EnumerateFiles(pluginDir, "*.dll",*
