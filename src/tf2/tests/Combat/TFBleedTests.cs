using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// <see cref="TFBleedState"/>: <c>CTFPlayerShared::MakeBleed</c> and the
/// bleeding arm of <c>ConditionGameRulesThink</c> at stock's constants:
/// <c>TF_BLEEDING_FREQUENCY</c> 0.5 s, <c>TF_BLEEDING_DMG</c> 4.
/// </summary>
public sealed class TFBleedTests
{
    private static readonly EntityHandle Attacker = new(0x0001_0002u);
    private const float Tick = 1f / 66f;

    /// <summary>Thinks every server tick from <paramref name="start"/>; the ticks' times and damages.</summary>
    private static List<(float Time, int Damage)> Run(TFBleedState bleeds, TFCondMask conds, float start, float until)
    {
        var ticks = new List<(float, int)>();
        var asked = new List<TFBleedTick>();
        for (int i = 1; i * Tick < until - start; i++)
        {
            asked.Clear();
            bleeds.Think(start + i * Tick, Tick, conds, asked);
            foreach (var tick in asked)
                ticks.Add((start + i * Tick, tick.Damage));
        }
        return ticks;
    }

    [Fact]
    public void A_bleed_ticks_four_every_half_second_until_its_time_is_spent()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, alive: true, default, Attacker, TFWeaponId.BatGiftwrap,
                                "tf_weapon_bat_giftwrap", 10f);

        Assert.True(conds.Has(TFCond.Bleeding));

        // Ten seconds of bleeding, a tick each half second: twenty ticks of
        // TF_BLEEDING_DMG (the live server is long past the raw-duration seed
        // of stock's flBleedingTime, so the first tick is at once).
        var ticks = Run(bleeds, conds, 1000f, 1010f + Tick);
        Assert.Equal(20, ticks.Count);
        Assert.All(ticks, t => Assert.Equal(TFCombatDefs.BleedingDamage, t.Damage));
        Assert.InRange(ticks[0].Time, 1000f, 1000f + Tick);
        for (int i = 1; i < ticks.Count; i++)
            Assert.InRange(ticks[i].Time - ticks[i - 1].Time, TFCombatDefs.BleedingFrequency - 0.001f,
                           TFCombatDefs.BleedingFrequency + Tick + 0.001f);
    }

    [Fact]
    public void The_bleed_ends_when_its_time_is_spent()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);

        var asked = new List<TFBleedTick>();
        Assert.False(bleeds.Think(1009f, Tick, conds, asked));
        Assert.True(bleeds.Think(1010f + Tick, Tick, conds, asked));
        Assert.Empty(bleeds.Bleeds);
    }

    [Fact]
    public void The_same_attacker_and_weapon_extend_their_bleed()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);
        conds = bleeds.Make(1003f, true, conds, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);

        Assert.Single(bleeds.Bleeds);
        Assert.Equal(1013f, bleeds.Bleeds[0].RemoveTime, 4);
    }

    [Fact]
    public void A_different_weapon_opens_its_own_bleed()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);
        bleeds.Make(1000f, true, conds, Attacker, TFWeaponId.Cleaver, "tf_weapon_cleaver", 5f);

        Assert.Equal(2, bleeds.Bleeds.Count);
    }

    [Fact]
    public void The_dead_do_not_bleed()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, alive: false, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);

        Assert.False(conds.Has(TFCond.Bleeding));
        Assert.Empty(bleeds.Bleeds);
    }

    [Fact]
    public void Healers_wear_the_bleed_off_twice_as_fast()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);
        conds.Set(TFCond.HealthBuff);

        var ticks = Run(bleeds, conds, 1000f, 1010f);

        // The 10 s remove time also runs down at 2 x frametime: out after
        // about a third of the time, six ticks, not twenty.
        Assert.InRange(ticks.Count, 6, 7);
    }

    [Fact]
    public void A_bleed_whose_condition_went_forgives_its_entries()
    {
        // OnRemoveBleeding's server half (tf_player_shared.cpp:4153-4156): the
        // entries go with the condition (an uber's OnAddInvulnerable took it).
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);
        conds.Clear(TFCond.Bleeding);

        var asked = new List<TFBleedTick>();
        Assert.False(bleeds.Think(1000f + Tick, Tick, conds, asked));
        Assert.Empty(bleeds.Bleeds);
        Assert.Empty(asked);
    }

    [Fact]
    public void StopBleed_takes_its_attacker_and_weapon_bleed_back()
    {
        var bleeds = new TFBleedState();
        var conds = bleeds.Make(1000f, true, default, Attacker, TFWeaponId.BatGiftwrap, "tf_weapon_bat_giftwrap", 10f);
        bleeds.Make(1000f, true, conds, new EntityHandle(0x0001_0009u), TFWeaponId.Cleaver, "tf_weapon_cleaver", 5f);

        bleeds.Stop(Attacker, TFWeaponId.BatGiftwrap);

        Assert.Single(bleeds.Bleeds);
        Assert.Equal(TFWeaponId.Cleaver, bleeds.Bleeds[0].Weapon);
    }
}
