using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The general play by the bot's own kit (plan_bots_teams_tf2 P7 step 2; the Medic's A4/A5, the Soldier's and the
/// Demoman's asks): the hands take out the kit's own weapons, not the Soldier's by name; a weapon's facts are its item's
/// (a Black Box holds three); a falling projectile is lobbed; a melee weapon is fought at its reach; and a class may
/// hold the trigger at a teammate on purpose without the friend gate letting it go.
/// </summary>
public sealed class TFKitPlayTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private const int Scattergun = 20, Pistol = 21, Bat = 22, ReserveShooter = 31, Pipes = 40;

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Scattergun, "tf_weapon_scattergun", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Pistol, "tf_weapon_pistol_scout", 12, 0.15f, 1f, true, false, 0f),
        new WeaponFacts(Bat, "tf_weapon_bat", 0, 0.5f, 0f, false, false, 0f),
        new WeaponFacts(ReserveShooter, "tf_weapon_shotgun_soldier", 4, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Pipes, "tf_weapon_grenadelauncher", 4, 0.6f, 3f, true, true, 1200f, ProjectileGravity: 400f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    private static TFHud Kit(params TFKitItem[] items) => new([.. items], [], []);

    private static TFHud ScoutKit()
        => Kit(Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun), Item(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", Pistol),
               Item(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", Bat));

    /// <summary>A Scout in a lull takes out its own primary, the scattergun: not the Soldier's launcher, which it does not carry.</summary>
    [Fact]
    public void InALullAScoutTakesOutItsOwnPrimary()
    {
        var f = new TFField(weapons: Weapons) { Hud = ScoutKit() };
        IntentCommand i = f.Step(TFField.Self(Mid, weapon: Bat, clip: 0, playerClass: TFClass.Scout), TFField.Ctf());
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Scattergun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>
    /// A Heavy in a lull with its own primary in hand and its clip not full reloads it (the review of 2026-11-20): at
    /// 225fe2e96 the hands took out the level's rocket launcher's kind every second instead, and no non-Soldier ever
    /// reloaded in a lull.
    /// </summary>
    [Fact]
    public void ANonSoldierReloadsItsOwnPrimaryInALull()
    {
        var f = new TFField(weapons: Weapons) { Hud = Kit(Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun)) };
        IntentCommand[] decides = [.. Enumerable.Range(0, 30).Select(_ => f.Step(TFField.Self(Mid, weapon: Scattergun, clip: 3, playerClass: TFClass.HeavyWeapons), TFField.Ctf()))];
        Assert.Contains(decides, c => c.Action.Kind == ActionKind.Reload);
        Assert.DoesNotContain(decides, c => c.Action.Kind == ActionKind.SelectWeapon);
    }

    /// <summary>With no kit read (a replay), a non-Soldier asks for no weapon by the Soldier's names: it carries none of them.</summary>
    [Fact]
    public void WithNoKitANonSoldierAsksForNoSoldiersWeapon()
    {
        var f = new TFField(weapons: Weapons);
        IntentCommand[] decides = [.. Enumerable.Range(0, 30).Select(_ => f.Step(TFField.Self(Mid, weapon: Bat, clip: 0, playerClass: TFClass.Scout), TFField.Ctf()))];
        Assert.DoesNotContain(decides, c => c.Action.Kind == ActionKind.SelectWeapon);
    }

    /// <summary>A Soldier whose launcher is dry up close swaps to the secondary its kit carries (a Reserve Shooter), not the stock shotgun.</summary>
    [Fact]
    public void ADryPrimaryIsSwappedForTheKitsOwnSecondary()
    {
        var f = new TFField(weapons: Weapons)
        {
            Hud = Kit(Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher), Item(TFLoadoutSlot.Secondary, 415, "tf_weapon_shotgun", ReserveShooter)),
        };
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 0), TFField.Ctf(), (Enemy, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + ReserveShooter), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The item's attributes make its facts: a Black Box (mult_clipsize 0.75) holds three, so three loaded is full and no lull reload is asked.</summary>
    [Fact]
    public void ABlackBoxWithThreeLoadedIsNotReloaded()
    {
        int Reloads(TFHud hud)
        {
            var f = new TFField(weapons: Weapons) { Hud = hud };
            return Enumerable.Range(0, 30).Select(_ => f.Step(TFField.Self(Mid, clip: 3), TFField.Ctf())).Count(c => c.Action.Kind == ActionKind.Reload);
        }

        // The control: the stock launcher with three of four loaded is reloaded in a lull.
        Assert.True(Reloads(Kit(Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher))) > 0);
        Assert.Equal(0, Reloads(Kit(Item(TFLoadoutSlot.Primary, 228, "tf_weapon_rocketlauncher", TFField.Launcher, new TFOwnKitAttribute(TFItemFacts.ClipScale, 0.75f)))));
    }

    /// <summary>An item's clip, cadence, reload and speed scale its kind's facts as the tooltip states them.</summary>
    [Fact]
    public void AnItemsAttributesScaleItsKindsFacts()
    {
        WeaponFacts launcher = TFField.Weapons.All.Single(w => w.Kind == TFField.Launcher);
        WeaponFacts liberty = TFItemFacts.Apply(in launcher, Item(TFLoadoutSlot.Primary, 414, "tf_weapon_rocketlauncher", TFField.Launcher,
                                                                    new TFOwnKitAttribute(TFItemFacts.SpeedScale, 1.4f), new TFOwnKitAttribute(TFItemFacts.ClipScale, 1.25f),
                                                                    new TFOwnKitAttribute(TFItemFacts.CadenceScale, 0.75f)));
        Assert.Equal((5, 1540f, 0.6f), (liberty.ClipSize, liberty.ProjectileSpeed, liberty.CadenceSeconds), new FactsClose());
    }

    private sealed class FactsClose : IEqualityComparer<(int, float, float)>
    {
        public bool Equals((int, float, float) a, (int, float, float) b) => a.Item1 == b.Item1 && MathF.Abs(a.Item2 - b.Item2) < 0.01f && MathF.Abs(a.Item3 - b.Item3) < 1e-4f;

        public int GetHashCode((int, float, float) obj) => obj.Item1;
    }

    /// <summary>A pipe that falls is aimed up the arc that lands it on a level target 900 u off, not at the body, where it would land short.</summary>
    [Fact]
    public void AFallingProjectileIsLobbed()
    {
        var f = new TFField(weapons: Weapons) { Hud = Kit(Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes)) };
        IntentCommand i = f.Step(TFField.Self(Mid, weapon: Pipes, clip: 4, playerClass: TFClass.Demoman), TFField.Ctf(),
                                 (Enemy, Mid + new AiVector(900f, 0f, 0f), AiVector.Zero, TFField.Blue));
        // The arc's point over the target: the low arc's rise over 900 u, well over the body's middle (41 u), where a flat aim goes.
        Assert.Equal(LookKind.Actor, i.Look.Kind);
        Assert.True(i.Look.Point.Z > 100f, $"aimed at z {i.Look.Point.Z}");
    }

    /// <summary>A bat is swung only at its reach: an enemy 300 u off is closed on, not swung at; one at 60 u is.</summary>
    [Fact]
    public void AMeleeWeaponIsFoughtAtItsReach()
    {
        IntentCommand At(float distance)
        {
            var f = new TFField(weapons: Weapons) { Hud = ScoutKit() };
            return f.Step(TFField.Self(Mid, weapon: Bat, clip: 0, playerClass: TFClass.Scout), TFField.Ctf(),
                          (Enemy, Mid + new AiVector(distance, 0f, 0f), AiVector.Zero, TFField.Blue));
        }

        IntentCommand far = At(300f), near = At(60f);
        Assert.Equal((ActionKind.None, ActionKind.Fire), (far.Action.Kind, near.Action.Kind));
    }

    private static readonly ActorId Between = new(6, 0x4000_0001);

    /// <summary>A class's play that holds the trigger at a teammate on purpose (a heal beam), at the teammate 400 u off.</summary>
    private sealed class BeamPlay(TFDecider d) : TFDecider.ClassPlay(d, TFClass.Medic)
    {
        public override void Engage() => FireAtFriend(Mate, Mid + new AiVector(400f, 0f, 40f));
    }

    /// <summary>
    /// Fire held at a teammate on purpose is not let go by the friend gate (the Medic's A4): the beam stays on its patient
    /// with another teammate between. The control: the general fire at an enemy with that teammate on the line is held.
    /// </summary>
    [Fact]
    public void FireAtAFriendOnPurposeIsNotHeldForThem()
    {
        var between = new ActorFacts(Between, Mid + new AiVector(200f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red);
        var patient = new ActorFacts(Mate, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red);
        var enemy = new ActorFacts(Enemy, Mid + new AiVector(500f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue);
        var general = new TFField();
        Assert.Equal(ActionKind.None, general.StepDrawn(TFField.Self(Mid), TFField.Ctf(), between, enemy).Action.Kind);

        var medic = new TFField(plays: (d, c) => c == TFClass.Medic ? new BeamPlay(d) : null);
        IntentCommand i = medic.StepDrawn(TFField.Self(Mid, playerClass: TFClass.Medic), TFField.Ctf(), between, patient);
        Assert.Equal((ActionKind.Fire, LookKind.Actor, Mate), (i.Action.Kind, i.Look.Kind, i.Look.Actor));
    }
}
