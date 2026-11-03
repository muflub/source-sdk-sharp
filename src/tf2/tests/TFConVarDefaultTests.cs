extern alias client;

using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// A game's default for a convar the core declares (<see cref="ConVarDefaultAttribute"/>).
/// Stock's own shape: the core guards its mp_waitingforplayers_time with
/// <c>#ifndef TF_DLL — "TF overrides the default value of this convar"</c>
/// (multiplay_gamerules.cpp:131) and the game DLL declares 30
/// (tf_gamerules.cpp:1089). TF's registration is the server module's walk,
/// which reads every assembly the side loaded, the mod's rows among them.
/// </summary>
public class TFConVarDefaultTests
{
    private static ConVarRegistry Registered(params Type[] types)
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterTypes(types);

        return registry;
    }

    [Fact]
    public void TFsRegistrationDefaultsTheWaitingClockToThirty()
    {
        // TF2.Server in the walk, as the game's side context holds it before
        // the module registers: the sweep reads every loaded assembly.
        _ = typeof(TFWaitingForPlayersTimeDefault).Assembly;

        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(ServerModule).Assembly);

        var convar = registry.Get<MpWaitingForPlayersTimeConVar>();

        Assert.True(convar is not null, "the server walk declares the waiting clock");
        Assert.True(convar!.Value == 30f,
            $"under TF's registration the waiting clock reads {convar.Value}, not the game's 30");
    }

    [Fact]
    public void TFsRegistrationDefaultsTheDefaultFovToSeventyFive()
    {
        // Stock TF declares default_fov 75 (clientmode_tf.cpp:113); SetUpViews
        // moves the viewmodel by default_fov - fov (view.cpp:724-729), so the
        // core's 90 under TF's fov_desired 75 drew the viewmodel at 39, not 54.
        _ = typeof(TFDefaultFovDefault).Assembly;

        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(ServerModule).Assembly);

        var convar = registry.Get<DefaultFovConVar>();

        Assert.True(convar is not null, "the server walk declares default_fov");
        Assert.True(convar!.Value == 75,
            $"under TF's registration default_fov reads {convar.Value}, not the game's 75");
    }

    /// <summary>
    /// Reads one declaration's attribute through <see cref="CustomAttributeData"/>:
    /// the alias gives the client's types their own nominal
    /// <c>ConVarAttribute</c>, so a typed
    /// <c>GetCustomAttribute&lt;ConVarAttribute&gt;</c> misses across it and the
    /// cross-side reads here go by <c>FullName</c>, as TFConVarsTests' do.
    /// </summary>
    private static (string Name, string Value)? DeclaredIn(Assembly asm, string attributeName, string name)
    {
        foreach (Type type in asm.GetTypes())
        {
            foreach (CustomAttributeData data in type.GetCustomAttributesData())
            {
                string full = data.AttributeType.FullName ?? data.AttributeType.Name;
                if (!full.EndsWith("." + attributeName, StringComparison.Ordinal) && full != attributeName)
                    continue;

                if (data.ConstructorArguments.Count < 2)
                    continue;

                if ((data.ConstructorArguments[0].Value as string) == name)
                    return (name, data.ConstructorArguments[1].Value as string ?? string.Empty);
            }
        }

        return null;
    }

    /// <summary>
    /// The client half of the replicated pair. Each side registers its own
    /// half into one engine list; the halves must agree or the engine logs
    /// its parent/child mismatch on every listen-server join (Source's
    /// conpair). TF's number is 75 on both sides, so the client walk answers
    /// it through TF2.Client's own <c>ConVarDefault</c> row beside the
    /// <c>fov_desired</c> declaration it belongs to.
    /// </summary>
    [Fact]
    public void TFsClientRegistrationAlsoDefaultsTheDefaultFovToSeventyFive()
    {
        // What the client walk finds: the core's declaration in SourceSharp.
        // Client, TF's row beside fov_desired in TF2.Client. The registry's
        // rule is that the game's row replaces the carried default, so the
        // walk's answer for default_fov is the pair below.
        var core = DeclaredIn(typeof(client::SourceSharp.Client.ClientModule).Assembly,
                               "ConVarAttribute", "default_fov");
        var game = DeclaredIn(typeof(client::TF2.Client.TFDefaultFovDefault).Assembly,
                              "ConVarDefaultAttribute", "default_fov");

        Assert.True(core is not null && core.Value.Value == "90",
            $"the core's client declares default_fov '{core?.Value ?? "<missing>"}', not its 90");
        Assert.True(game is not null && game.Value.Value == "75",
            $"TF's client row redefaults default_fov to '{game?.Value ?? "<missing>"}', not the game's 75");

        string effective = game is not null ? game.Value.Value : core!.Value.Value;
        Assert.True(effective == "75",
            $"under TF's client registration default_fov reads '{effective}', not the game's 75");
    }

    /// <summary>
    /// The core's client, with no mod assembly in the walk: the HL2MP test
    /// mod has no client DLL of its own, so its client answers the core's
    /// number, which is what stock's HL2MP client chain ships (the stock
    /// clientmode files: sdk's 90, inherited through hl2mp's empty chain).
    /// </summary>
    [Fact]
    public void TheCoresOwnClientRegistrationKeepsNinety()
    {
        Assembly coreAssembly = typeof(client::SourceSharp.Client.ClientModule).Assembly;

        var core = DeclaredIn(coreAssembly, "ConVarAttribute", "default_fov");
        var override_ = DeclaredIn(coreAssembly, "ConVarDefaultAttribute", "default_fov");

        Assert.True(core is not null && core.Value.Value == "90",
            $"the core's client declares default_fov '{core?.Value ?? "<missing>"}', not its declared 90");
        Assert.True(override_ is null,
            $"the core's client alone redefaults default_fov to '{override_?.Value}', where it should keep its own");

        string effective = override_ is not null ? override_.Value.Value : core!.Value.Value;
        Assert.True(effective == "90", $"the core's client alone reads '{effective}', not its declared 90");
    }

    [Fact]
    public void TheCoresOwnRegistrationKeepsZero()
    {
        // The override lives with the game: a registry that never walks
        // TF2.Server still binds the core's own number.
        var convar = Registered(typeof(MpWaitingForPlayersTimeConVar))
            .Get<MpWaitingForPlayersTimeConVar>();

        Assert.True(convar is not null, "the core declares the waiting clock");
        Assert.True(convar!.Value == 0f,
            $"the core alone reads {convar!.Value}, not its declared 0");
    }

    [Fact]
    public void TheRefusedBindStillCarriesTheGamesDefault()
    {
        // No engine here, so the registration is refused and the bound value
        // is the default alone — which must be the game's row, not the
        // attribute the core declared, or every fallback read the old number.
        string value = Registered(typeof(MpWaitingForPlayersTimeConVar), typeof(TFWaitingForPlayersTimeDefault))
            .ValueOf("mp_waitingforplayers_time")!;

        Assert.True(value == "30", $"the bound value is '{value}', not the game's 30");
    }

    [Fact]
    public void ADefaultForANameTheWalkNeverDeclaresIsRefused()
    {
        var registry = ConVarRegistry.For(TestServices.UnattachedConsole());

        // The marker alone: the core's declaration is not in this walk, so
        // the row names nothing and is refused rather than redefaulting
        // silently.
        var e = Record.Exception(() => registry.RegisterTypes([typeof(TFWaitingForPlayersTimeDefault)]));

        Assert.True(e is InvalidOperationException,
            $"an override of an undeclared name is refused, not silent: {e?.GetType().Name}");
        Assert.Contains("nothing declares", e!.Message);
    }
}
