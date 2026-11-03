using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Entities.Rooms;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// The resupply locker's touch sound is precached before it plays: stock's
/// <c>CRegenerateZone::Precache</c> (func_regenerate.cpp) sounds
/// <c>TF_REGENERATE_SOUND</c> through <c>PrecacheScriptSound</c>, and an
/// unprecached script name is <c>SV_StartSound</c> refusing before the
/// broadcast — the locker the player stands in, silent.
/// </summary>
public sealed class FuncRegeneratePrecacheTests
{
    [Fact]
    public void The_regenerate_sound_is_declared_on_the_class()
    {
        var declared = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<PrecacheAttribute>(typeof(FuncRegenerate), false);

        Assert.True(declared.Any(a => a.Kind == PrecacheKind.Sound && a.Asset == FuncRegenerate.RegenerateSound),
            "func_regenerate declares its touch sound on the class, so the sweep carries it "
          + "whatever the spawn path does");
    }

    /// <summary>And the sweep really reaches the engine with it, as a sound.</summary>
    [Fact]
    public void The_sweep_hands_the_regenerate_sound_to_precache_sound()
    {
        var engine = TestServices.Mock();
        engine.Strict = false;

        ServerPrecache.PrecacheAll(engine, new RecordingLog(), typeof(FuncRegenerate).Assembly);

        Assert.True(engine.AllOf("Precache.PrecacheSound").Any(c => c.Args.Contains(FuncRegenerate.RegenerateSound)),
            "the sweep precaches Regenerate.Touch as a SOUND");
    }
}
