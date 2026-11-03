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
