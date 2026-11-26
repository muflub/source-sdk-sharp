using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using TF2.Server.Entities.Rooms;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The level's own zones as the bots read them live (<see cref="TFPlacedEntities.Read"/>, the only source of a TF2 map's
/// knowledge: a shipped lump is compressed): a <c>func_nobuild</c> and a <c>trigger_hurt</c> placed in the level come out
/// as their brushes with the keys <see cref="TFMapKnowledge"/> reads, so a no-build zone and a death pit exist in a game.
/// Before round 2 neither was read: <c>ZoneKind.Hurt</c> never existed live (Fable's review of the Engineer lane).
/// </summary>
public sealed class TFPlacedZonesTests
{
    private static readonly Vector At = new(100f, 200f, 0f), Mins = new(-64f, -64f, -32f), Maxs = new(64f, 64f, 32f);

    /// <summary>The directory holds <paramref name="placed"/> under its class; every other class is empty. Origins and bounds are fixed.</summary>
    private static MapEntities Read(params (string Class, ScriptedEntity Entity)[] placed)
    {
        IEntityDirectory directory = Senses.FakeEngine.Of<IEntityDirectory>((m, a) => m.Name == "OfClass"
            ? placed.Where(p => p.Class == (string)a[0]!).Select(p => p.Entity).ToArray()
            : throw Senses.FakeEngine.Unasked(m));
        IEngineEntities entities = Senses.FakeEngine.Of<IEngineEntities>((m, _) => m.Name switch
        {
            "GetOrigin" => At,
            "GetBounds" => (Mins, Maxs),
            "GetName" => "",
            _ => throw Senses.FakeEngine.Unasked(m),
        });
        return new TFPlacedEntities(directory, entities).Read();
    }

    private static TriggerHurt Hurt(float damage, bool startDisabled = false)
    {
        var hurt = new TriggerHurt(null!, null!, null!, null!);
        hurt.TriggerHurtVars.Damage = damage;
        hurt.Vars.StartDisabled = startDisabled;
        return hurt;
    }

    private static FuncNoBuild NoBuild(int team, bool allowSentry = false, bool allowTeleporters = false)
    {
        var area = new FuncNoBuild(null!, null!, null!);
        area.Vars.TeamNum = team;
        area.Vars.AllowSentry = allowSentry;
        area.Vars.AllowTeleporters = allowTeleporters;
        return area;
    }

    private static TeamZone[] Zones(MapEntities level)
        => TFMapKnowledge.Build("ctf_test", level, new WeaponKnowledge([]), default, BodyKnowledge.Default()).TeamZones.ToArray();

    [Fact]
    public void ALethalTriggerHurtInTheLevelIsADeathPit()
        => Assert.Equal([new TeamZone(0, ZoneKind.Hurt, new AiVector(36f, 136f, -32f), new AiVector(164f, 264f, 32f))],
                        Zones(Read((TFMapKnowledge.TriggerHurtClass, Hurt(20000f)))));

    [Fact]
    public void ATriggerHurtThatStartsOffIsNoDeathPit()
        => Assert.Empty(Zones(Read((TFMapKnowledge.TriggerHurtClass, Hurt(20000f, startDisabled: true)))));

    [Fact]
    public void AGentleTriggerHurtIsNoDeathPit()
        => Assert.Empty(Zones(Read((TFMapKnowledge.TriggerHurtClass, Hurt(20f)))));

    [Fact]
    public void AFuncNoBuildInTheLevelIsANoBuildZoneOfItsTeam()
        => Assert.Equal([new TeamZone(3, ZoneKind.NoBuild, new AiVector(36f, 136f, -32f), new AiVector(164f, 264f, 32f))],
                        Zones(Read((FuncNoBuild.MapClass, NoBuild(3)))));

    /// <summary>The level's area as it allows: one that lets the sentry and the teleporters in still refuses the dispenser, a zone.</summary>
    [Fact]
    public void AFuncNoBuildThatRefusesOnlyTheDispenserIsStillAZone()
        => Assert.Single(Zones(Read((FuncNoBuild.MapClass, NoBuild(0, allowSentry: true, allowTeleporters: true)))));
}
