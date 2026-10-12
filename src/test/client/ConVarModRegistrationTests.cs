using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mod assembly's convars (C7, ahead of viewpostprocess.cpp's eviction, whose
/// pyro_* convars become TF2.Client's): a side's registry declares every
/// [ConVar] class in the assemblies loaded in its context, the core's and each
/// mod's alike; one name has one declaration, so a second -- core and mod, or
/// two mods -- is refused loudly; and a mod's convar keeps its value across a
/// hot reload, because native owns the engine convar for the process and a
/// reload's registry binds to it by name. This test assembly stands for a mod:
/// an assembly other than the core's, in the same context.
/// </summary>
public class ConVarModRegistrationTests
{
    // Native's registration: one engine convar per name, kept for the process
    // (console_registry.h), so a reload's registration answers the same id.
    private sealed class ProcessBackend : IConVarBackend
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            if (!_ids.TryGetValue(name, out int id))
            {
                id = _ids.Count;
                _ids[name] = id;
                _values[id] = defaultValue;
            }
            return id;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name) => _ids.TryGetValue(name, out int id) ? _values[id] : null;
    }

    [ConVar("ss_test_mod_vignette", "2", Help = "a mod's convar")]
    private sealed class ModConVar : IntConVar { }

    // The core declares mat_bloomscale (MatBloomscaleConVar, viewpostprocess.cpp's).
    [ConVar("mat_bloomscale", "1", Help = "a mod redeclaring a core name")]
    private sealed class ModRedeclaresCore : BoolConVar { }

    [ConVar("ss_test_mod_vignette", "3", Help = "a second declaration of the mod's name")]
    private sealed class SecondModDeclaration : IntConVar { }

    [Fact]
    public void TheSideScanFindsAMods()
    {
        IReadOnlyList<Assembly> loaded = LoadedAssemblies.All(typeof(PostProcessPlanner).Assembly, new StandardErrorLog());
        Assert.Contains(typeof(ModConVar).Assembly, loaded);
        Assert.Contains(typeof(PostProcessPlanner).Assembly, loaded);
    }

    [Fact]
    public void AModsConvarRegistersBesideTheCores()
    {
        var registry = new ConVarRegistry(new ProcessBackend());
        Assert.Equal(2, registry.RegisterTypes([typeof(MatBloomscaleConVar), typeof(ModConVar)]));
        Assert.Equal("2", registry.ValueOf("ss_test_mod_vignette"));
        Assert.NotNull(registry.Get<ModConVar>());
    }

    [Fact]
    public void AModsConvarKeepsItsValueAcrossAHotReload()
    {
        var backend = new ProcessBackend();
        var before = new ConVarRegistry(backend);
        before.RegisterTypes([typeof(ModConVar)]);
        before.Get<ModConVar>()!.SetValue(7);
        Assert.Equal(7, before.Get<ModConVar>()!.Value);

        // The reload: a new module, a new registry, the same engine convar.
        var after = new ConVarRegistry(backend);
        Assert.Equal(1, after.RegisterTypes([typeof(ModConVar)]));
        Assert.Equal(7, after.Get<ModConVar>()!.Value);
    }

    [Fact]
    public void AModRedeclaringACoreNameIsRefusedNamingBoth()
    {
        var registry = new ConVarRegistry(new ProcessBackend());
        var e = Assert.Throws<InvalidOperationException>(() => registry.RegisterTypes([typeof(MatBloomscaleConVar), typeof(ModRedeclaresCore)]));
        Assert.Contains("'mat_bloomscale'", e.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(MatBloomscaleConVar).Assembly.GetName().Name!, e.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(ModRedeclaresCore).Assembly.GetName().Name!, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDeclarationsOfOneModNameAreRefused()
    {
        var registry = new ConVarRegistry(new ProcessBackend());
        Assert.Throws<InvalidOperationException>(() => registry.RegisterTypes([typeof(ModConVar), typeof(SecondModDeclaration)]));
    }

    [Fact]
    public void TheSameDeclarationMetTwiceIsOneConvar()
    {
        var registry = new ConVarRegistry(new ProcessBackend());
        Assert.Equal(1, registry.RegisterTypes([typeof(ModConVar), typeof(ModConVar)]));
        Assert.Single(registry.All, c => c.Name == "ss_test_mod_vignette");
    }
}
