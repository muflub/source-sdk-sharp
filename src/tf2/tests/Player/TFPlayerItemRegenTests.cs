using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The item arm of <c>CTFPlayer::RegenThink</c>
/// (<c>tf_player.cpp:1831-1854, 1861-1901</c>): the held item's
/// <c>add_health_regen</c> is walked over zero once a second, scaled by the
/// peace since the last hit, and its whole points heal through the resupply
/// seam every second while the fraction waits in the accumulator.
/// </summary>
public sealed class TFPlayerItemRegenTests
{
    private const int Index = 3;
    private static readonly EntityHandle Player = new(0x0001_0000u | Index);

    private sealed class RecordingResupply : ITFPlayerResupply
    {
        public List<float> Healed { get; } = [];

        public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }

        public int MaxHealth(EntityHandle player) => 100;

        public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false)
        {
            Healed.Add(health);
            return (int)health;
        }

        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => 0;

        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
    }

    /// <summary>The rig: an alive player whose manager carries one additive hook.</summary>
    private static (RecordingEngine Engine, FakeOwners Owners, RecordingResupply Resupply, TFDamageRecords Records,
                    TFPlayerItemRegen Slice) Rig(string hook, float perSecond)
    {
        var engine = new RecordingEngine();
        engine.Returns("Players.IsAlive", true);

        var owners = new FakeOwners();
        var manager = new AttributeContainer();
        manager.Set(AttributeValue.OfFloat(new AttributeDefinition(9120, "attr 9120", hook,
                                                                   AttributeDescriptionFormat.Additive,
                                                                   AttributeEffectType.Positive, false, false,
                                                                   AttributeValueType.Default,
                                                                   MergedKeys.Block("x")), perSecond));
        owners.Managers[Index] = manager;

        var resupply = new RecordingResupply();
        var records = new TFDamageRecords();
        var slice = new TFPlayerItemRegen(resupply, new AttributeHooks(), owners, engine, records);
        slice.Attach(Player);
        return (engine, owners, resupply, records, slice);
    }

    private static void NoteHit(TFDamageRecords records, float time) =>
        records.Note(Index, new TFHitRecord(EntityHandle.None, 0f, default, default, default, default, "", time),
                      damager: false);

    /// <summary>The whole points walk out once a second: two a second, twice.</summary>
    [Fact]
    public void TheWholePointsHealOnceASecond()
    {
        var (_, _, resupply, _, slice) = Rig("add_health_regen", 2f);

        slice.Tick(1f);
        slice.Tick(2f);

        Assert.Equal([2f, 2f], resupply.Healed);
    }

    /// <summary>A manager with no regen attribute leaves the seam unasked.</summary>
    [Fact]
    public void AnAttributeLessManagerHealsNothing()
    {
        var (_, _, resupply, _, slice) = Rig("some other hook", 2f);

        slice.Tick(1f);

        Assert.Empty(resupply.Healed);
    }

    /// <summary>The quarter scale inside five seconds of the last hit (:1843-1846).</summary>
    [Fact]
    public void ARecentHitQuartersTheRegen()
    {
        var (_, _, resupply, records, slice) = Rig("add_health_regen", 4f);
        NoteHit(records, time: 7f);

        slice.Tick(10f);   // three seconds of peace: a quarter of four

        Assert.Equal([1f], resupply.Healed);
    }

    /// <summary>Full regen once the peace passes ten (:1849's clamp at its far end).</summary>
    [Fact]
    public void ALongPeaceKeepsTheRegenWhole()
    {
        var (_, _, resupply, records, slice) = Rig("add_health_regen", 4f);
        NoteHit(records, time: -2f);   // twelve seconds of peace

        slice.Tick(10f);

        Assert.Equal([4f], resupply.Healed);
    }

    /// <summary>Half points wait in the accumulator until a whole fits (:1854, :1900).</summary>
    [Fact]
    public void TheHalfPointWaitsInTheAccumulator()
    {
        var (_, _, resupply, _, slice) = Rig("add_health_regen", 1.5f);

        slice.Tick(1f);    // 1.5 in, one heals, half a point stays
        slice.Tick(1.5f);  // too soon: the interval has not turned
        slice.Tick(2f);    // 0.5 held + 1.5 = two whole points

        Assert.Equal([1f, 2f], resupply.Healed);
    }

    /// <summary>The dead do not regen (:1784).</summary>
    [Fact]
    public void TheDeadDoNotRegen()
    {
        var (engine, _, resupply, _, slice) = Rig("add_health_regen", 2f);
        engine.Returns("Players.IsAlive", false);

        slice.Tick(1f);

        Assert.Empty(resupply.Healed);
    }
}
