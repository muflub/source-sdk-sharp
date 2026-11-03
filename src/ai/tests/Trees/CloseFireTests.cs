using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// User ruling 2026-11-02: "bots should also fire if close to the player instead
/// of trying to do other things". Someone seen within
/// <see cref="Hl2mpDecider.CloseRange"/> comes first, whatever branch the bot
/// was in; the exceptions are physical (an empty gun, a weapon that cannot reach).
/// </summary>
public sealed class CloseFireTests
{
    private static readonly AiVector Close = new(200f, 0f, 0f), Far = new(900f, 0f, 0f);
    private const int HoldsCrowbarPistolSmg = (1 << Duel.Crowbar) | (1 << Duel.Pistol) | (1 << Duel.Smg1);

    private static Duel Facing(string preset, AiVector at)
    {
        var d = new Duel(Duel.Preset(preset));
        d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        return d;
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("normal")]
    [InlineData("hard")]
    [InlineData("expert")]
    public void ABotGoingToHealFires(string preset)
    {
        Duel d = Facing(preset, Close);
        IntentCommand i = d.Step(d.Self(health: 5), [(Duel.Opponent, Close)]);
        Assert.Equal("Survive", d.Decider.Doing.Branch);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(Duel.Opponent, i.Look.Actor);
    }

    /// <summary>A bot short of ammo, with some rounds and a box in reach (the Arm branch's want), fires.</summary>
    [Fact]
    public void ABotShortOfAmmoFires()
    {
        var box = new ItemSpawn(0, "item_ammo_smg1", ItemKind.Ammo, new AiVector(-100f, 0f, 0f), 30f);
        var d = new Duel(Duel.Preset("normal"), box);
        d.Step(d.Self(clip: 3, reserve: 0), [(Duel.Opponent, Close)], d.SeeAt(Duel.Opponent, Close));
        Assert.Equal(ActionKind.Fire, d.Step(d.Self(clip: 3, reserve: 0), [(Duel.Opponent, Close)]).Action.Kind);
    }

    [Fact]
    public void ARoamingBotFires()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), []);
        d.Step(d.Self(), []);
        Assert.Equal("Roam", d.Decider.Doing.Branch);
        d.Step(d.Self(), [(Duel.Opponent, Close)], d.SeeAt(Duel.Opponent, Close));
        Assert.Equal(ActionKind.Fire, d.Step(d.Self(), [(Duel.Opponent, Close)]).Action.Kind);
    }

    /// <summary>
    /// The Hunt branch's pull: a Headhunter after the leader, last seen and out
    /// of sight, with someone else 200 u in front of it. The one in front gets the shots.
    /// </summary>
    [Fact]
    public void ALeaderOutOfSightDoesNotTakeAHuntersFireFromSomeoneInFrontOfIt()
    {
        SkillProfile hunter = Duel.Preset("normal");
        var d = new Duel(hunter.With(hunter.IndexOf("headhunter"), 10));
        d.Step(d.Self(), [(Duel.Opponent, Far)], [.. d.SeeAt(Duel.Opponent, Far), d.Row(Duel.Opponent, 9, 1), d.Row(Duel.Other, 0, 2), d.Row(Duel.Bot, 0, 3)]);
        d.Step(d.Self(), []);
        d.Step(d.Self(), [(Duel.Other, Close)], d.SeeAt(Duel.Other, Close));
        IntentCommand i = d.Step(d.Self(), [(Duel.Other, Close)]);
        Assert.Equal(Duel.Other, i.Look.Actor);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>Two in plain sight: within close range the nearer gets the shots, the leader or not.</summary>
    [Fact]
    public void TheLeaderInSightAtRangeDoesNotTakeFireFromSomeoneClose()
    {
        SkillProfile hunter = Duel.Preset("normal");
        var d = new Duel(hunter.With(hunter.IndexOf("headhunter"), 10));
        (ActorId, AiVector)[] both = [(Duel.Opponent, Far), (Duel.Other, Close)];
        d.Step(d.Self(), both, [.. d.SeeAt(Duel.Opponent, Far), .. d.SeeAt(Duel.Other, Close), d.Row(Duel.Opponent, 9, 1), d.Row(Duel.Other, 0, 2), d.Row(Duel.Bot, 0, 3)]);
        Assert.Equal(Duel.Other, d.Step(d.Self(), both).Look.Actor);
    }

    [Fact]
    public void ReloadingWithRoundsInTheClipItFiresClose()
    {
        Duel d = Facing("normal", Close);
        IntentCommand i = d.Step(d.Self(clip: 2, extra: SelfFlags.Reloading), [(Duel.Opponent, Close)]);
        Assert.Equal(ActionKind.Fire, i.Action.Kind);
    }

    /// <summary>The control: far off, it reloads in peace as it did.</summary>
    [Fact]
    public void ReloadingFarOffItHoldsFire()
    {
        Duel d = Facing("normal", Far);
        Assert.Equal(ActionKind.None, d.Step(d.Self(clip: 2, extra: SelfFlags.Reloading), [(Duel.Opponent, Far)]).Action.Kind);
    }

    /// <summary>The exception: an empty clip with rounds to load, it reloads, then resumes.</summary>
    [Fact]
    public void AnEmptyClipReloads()
    {
        Duel d = Facing("normal", Close);
        Assert.Equal(ActionKind.Reload, d.Step(d.Self(clip: 0), [(Duel.Opponent, Close)]).Action.Kind);
    }

    /// <summary>The first select within a second and a bit (a bot selects at most once a second).</summary>
    private static IntentCommand FirstSelect(Duel d, SelfState self, AiVector at)
    {
        IntentCommand i = default;
        for (int step = 0; step < 15 && i.Action.Kind != ActionKind.SelectWeapon; step++)
            i = d.Step(self, [(Duel.Opponent, at)]);
        return i;
    }

    /// <summary>The exception: a crowbar does not reach; a gun in the bag does.</summary>
    [Theory]
    [InlineData(200f)]
    [InlineData(50f)]
    public void AMeleeWeaponGivesWayToAGun(float distance)
    {
        var at = new AiVector(distance, 0f, 0f);
        var d = new Duel(Duel.Preset("normal"));
        SelfState self = d.Self(clip: 0, reserve: 0, weapon: Duel.Crowbar);
        d.Step(self, [(Duel.Opponent, at)], [.. d.SeeAt(Duel.Opponent, at), AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), HoldsCrowbarPistolSmg)]);
        IntentCommand i = FirstSelect(d, self, at);
        Assert.Equal(ActionKind.SelectWeapon, i.Action.Kind);
        Assert.Equal(Hl2mpDecider.SelectByKind + Duel.Smg1, i.Action.WeaponSlot);
    }

    /// <summary>A gun with nothing left gives way to a loaded one, however it ranks.</summary>
    [Fact]
    public void ADryGunGivesWayToALoadedOneOfLowerRank()
    {
        var d = new Duel(Duel.Preset("normal"));
        SelfState self = d.Self(clip: 0, reserve: 0);
        d.Step(self, [(Duel.Opponent, Close)], [.. d.SeeAt(Duel.Opponent, Close), AiEvent.Holdings(Duel.Bot, new Tick(d.Tick + 6), HoldsCrowbarPistolSmg)]);
        IntentCommand i = FirstSelect(d, self, Close);
        Assert.Equal(ActionKind.SelectWeapon, i.Action.Kind);
        Assert.Equal(Hl2mpDecider.SelectByKind + Duel.Pistol, i.Action.WeaponSlot);
    }

    /// <summary>The fire gate never refuses a gun at close range: its lowest gate is 1 and no gun's effective range is under close.</summary>
    [Theory]
    [InlineData("weapon_pistol")]
    [InlineData("weapon_357")]
    [InlineData("weapon_smg1")]
    [InlineData("weapon_ar2")]
    [InlineData("weapon_shotgun")]
    [InlineData("weapon_crossbow")]
    [InlineData("weapon_rpg")]
    public void NoGunsRangeGateRefusesAtCloseRange(string weapon)
    {
        float lowest = SourceSharp.Ai.Tests.Skills.Curves.Game()["fireRangeGate"].Levels.Min();
        Assert.True(Hl2mpWeaponRanges.For(weapon).EffectiveRange * lowest >= Hl2mpDecider.CloseRange, weapon);
    }
}
