using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// Tells the SDK where the rules module is on disk (plan_host_sdk §1.2).
///
/// <para>
/// The SDK's default (<see cref="LoadedAssemblyModuleSource"/>) describes the
/// loaded assembly that carries <c>[HostRulesModule]</c> by its
/// <see cref="Assembly.Location"/>. The game loads every managed assembly from
/// a copy in memory, so that location is empty and the default finds nothing:
/// Booting would announce no module. The file is where mods.yaml's managed
/// directory says, so this names it there, with the same dependency rule the
/// default uses: the assemblies it references that sit beside it, never
/// Host.Contracts or the SDK's own.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class PathModuleSource(IModInfo mod) : IRulesModuleSource
{
    /// <summary>The rules module's assembly; touching it loads it.</summary>
    public Assembly Module { get; } = typeof(TF2.Descent.Rules.TF2DescentRules).Assembly;

    public RulesModuleFile? Find()
    {
        var name = Module.GetName();
        string dir = mod.ManagedDir;
        string path = Path.Combine(dir, name.Name + ".dll");

        if (!File.Exists(path))
            return null;

        var never = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(HostContract).Assembly.GetName().Name!,
            typeof(HostSdk).Assembly.GetName().Name!,
            "SourceSharp.Host.Proto",
        };

        var deps = new List<RulesModuleDependency>();
        foreach (var reference in Module.GetReferencedAssemblies())
        {
            if (reference.Name is null || never.Contains(reference.Name))
                continue;

            string beside = Path.Combine(dir, reference.Name + ".dll");
            if (File.Exists(beside))
                deps.Add(new RulesModuleDependency(reference.Name, beside));
        }

        return new RulesModuleFile(name.Name!, name.Version?.ToString() ?? "0.0.0", path, HostContract.Version, deps);
    }
}
