using System;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A blast announces itself to the sound ledger, as stock's global
/// <c>RadiusDamage</c> did after the rules' sweep: a combat sound flagged as
/// an explosion, 1.5 times the blast's radius and never under 128 units,
/// owned by the inflictor, at the origin the caller gave.
/// </summary>
public class BlastSoundTests
{
    private static readonly Vector Origin = new(10f, 20f, 30f);
    private static readonly EntityHandle Inflictor = new(0x00010005u);

    private static (BlastSweep Sweep, SoundEnt Sounds) Build()
    {
        var engine = new RecordingEngine { Strict = false };
        var sounds = new SoundEnt(engine, engine, engine);
        sounds.Initialize(1, 0f);
        var sweep = new BlastSweep(new RecordingLog(), engine, engine, engine, engine, engine, sounds);
        return (sweep, sounds);
    }

    private static HeardSound[] Heard(SoundEnt sounds)
    {
        var into = new HeardSound[8];
        int n = sounds.Query(SoundType.Combat, into);
        return into.AsSpan(0, n).ToArray();
    }

    [Fact]
    public void ABlastInsertsOneCombatExplosionSoundOwnedByTheInflictor()
    {
        var (sweep, sounds) = Build();

        sweep.Apply(Origin, 100f, 200f, DamageType.Blast, EntityHandle.None, Inflictor, EntityHandle.None);

        var heard = Assert.Single(Heard(sounds));
        Assert.Equal(Origin, heard.Origin);
        Assert.Equal(SoundType.Combat | SoundType.ContextExplosion, heard.Type);
        Assert.Equal(Inflictor, heard.Owner);
    }

    [Fact]
    public void TheSoundsRadiusIsOneAndAHalfTimesTheBlasts()
    {
        var (sweep, sounds) = Build();

        sweep.Apply(Origin, 100f, 200f, DamageType.Blast, EntityHandle.None, Inflictor, EntityHandle.None);

        Assert.Equal(300, Assert.Single(Heard(sounds)).Volume);
    }

    [Fact]
    public void ASmallBlastIsStillHeardAt128Units()
    {
        var (sweep, sounds) = Build();

        sweep.Apply(Origin, 10f, 20f, DamageType.Blast, EntityHandle.None, Inflictor, EntityHandle.None);

        Assert.Equal(128, Assert.Single(Heard(sounds)).Volume);
    }

    [Fact]
    public void DamageThatIsNotABlastMakesNoSound()
    {
        var (sweep, sounds) = Build();

        sweep.Apply(Origin, 100f, 200f, DamageType.Burn, EntityHandle.None, Inflictor, EntityHandle.None);

        Assert.Empty(Heard(sounds));
    }
}
