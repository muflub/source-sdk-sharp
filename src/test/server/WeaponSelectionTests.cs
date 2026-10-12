/*
 * The five selection verbs, and the fact that they are five.
 *
 * They were three names for one move. shell_hud.cpp forwarded both invnext and
 * invprev as "lastinv", and the server answered lastinv by stepping FORWARD one
 * slot -- so next, previous and last-held were the same command, and lastinv did
 * not mean what its name says. With two weapons carried that is invisible:
 * forward and back are the same move, and the mod plays correctly right up to
 * the moment a third weapon is picked up. Which is why it was reported as
 * "switching is flaky in some states" rather than as a wiring mistake.
 *
 * So what is pinned here is that the five verbs DIVERGE -- a sequence of
 * switches, each landing on the weapon it was asked for -- rather than that any
 * one of them works.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class WeaponSelectionTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    /// <summary>
    /// An armed player. Four weapons, because three is the fewest that tells
    /// "next" from "previous" and four leaves room to be wrong in either
    /// direction without wrapping onto the right answer by luck.
    /// </summary>
    private static (Arsenal arsenal, RecordingEngine engine, WeaponSwitchTests.SpawningEntities world) Armed(
        bool alive = true)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", alive)
            // Where a dropped weapon lands. Stated here rather than per test
            // because dropping is part of switching and dying alike, and it
            // reaches the floor trace either way.
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f),
                                     new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        // THE PLAYER IS SPAWNED INTO THE REGISTRY, and this fixture used to
        // stub only the classname.
        //
        // plan-audit D19 measured Arsenal.IsStandalonePlayer asking the shell
        // for a classname 6.000 times a server frame and made it a registry
        // lookup. On a live server the two are the same fact -- SsPlayer IS the
        // [ScriptedEntity] bound to "ss_player" -- but a fixture that answers
        // the classname and holds no instance is a player the ARSENAL can see
        // and the REGISTRY cannot, which is a state no running game is ever in.
        // The stand-in had drifted from the thing it stands in for; this is the
        // agreement asserted rather than assumed.
        //
        // KeyValue and not Spawn, because Spawn is not what makes the instance
        // exist -- the registry's own comment says so: "Created on demand
        // rather than at spawn because of the order Source works in: an entity
        // is constructed, then handed every keyvalue in the map, and only then
        // spawned." A fixture stopping after the keyvalues is a state every
        // running server passes through; running OnSpawn here would put an
        // EntityRender.SetVisible into engine.Calls, which two facts in this
        // file read.
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine);

        return (arsenal, engine, world);
    }

    private static readonly string[] Loadout =
    {
        "weapon_crowbar", "weapon_pistol", "weapon_smg1", "weapon_shotgun",
    };

    private static Arsenal WithLoadout()
    {
        var (arsenal, _, _) = Armed();

        foreach (var name in Loadout)
            arsenal.GiveWeapon(Player, name);

        return arsenal;
    }

    /// <summary>What is in the hands, by classname — the same list cs_arsenal prints.</summary>
    private static string Held(Arsenal arsenal)
    {
        foreach (var (className, _, active) in arsenal.Holdings(Player))
        {
            if (active)
                return className;
        }

        return "";
    }

    //-------------------------------------------------------------------------
    // The three cycling verbs, which used to be one.
    //-------------------------------------------------------------------------

    [Fact]
    public void InvnextWalksForwardsAndWraps()
    {
        var arsenal = WithLoadout();

        // The loadout deploys its first weapon, so this starts on the crowbar.
        Assert.True(Held(arsenal) == "weapon_crowbar", "the first weapon given is in the hands");

        var walked = new List<string>();

        for (int i = 0; i < Loadout.Length; i++)
        {
            Assert.True(arsenal.SwitchNext(Player) == SwitchOutcome.Switched, "invnext switches");
            walked.Add(Held(arsenal));
        }

        Assert.True(walked.SequenceEqual(new[]
            { "weapon_pistol", "weapon_smg1", "weapon_shotgun", "weapon_crowbar" }),
            $"invnext walks the inventory forwards and wraps, rather than [{string.Join(", ", walked)}]");
    }

    [Fact]
    public void InvprevWalksBackwardsAndWraps()
    {
        var arsenal = WithLoadout();

        var walked = new List<string>();

        for (int i = 0; i < Loadout.Length; i++)
        {
            Assert.True(arsenal.SwitchPrev(Player) == SwitchOutcome.Switched, "invprev switches");
            walked.Add(Held(arsenal));
        }

        Assert.True(walked.SequenceEqual(new[]
            { "weapon_shotgun", "weapon_smg1", "weapon_pistol", "weapon_crowbar" }),
            $"invprev walks the OTHER way, rather than [{string.Join(", ", walked)}]");
    }

    /// <summary>
    /// The bug's exact shape, as a test. Both verbs answered by SwitchNext is
    /// what shipped; with four weapons it lands two slots on, not back where it
    /// started.
    /// </summary>
    [Fact]
    public void NextThenPreviousComesBackToWhereItStarted()
    {
        var arsenal = WithLoadout();

        string before = Held(arsenal);

        arsenal.SwitchNext(Player);
        arsenal.SwitchPrev(Player);

        Assert.True(Held(arsenal) == before,
            $"next and back again is where you were ({before}), not {Held(arsenal)}");
    }

    [Fact]
    public void LastinvIsThePreviouslyHeldWeaponAndNotTheNextOne()
    {
        var arsenal = WithLoadout();      // crowbar

        arsenal.SwitchSlot(Player, 2);    // smg1
        Assert.True(Held(arsenal) == "weapon_smg1", "the slot switch lands on the smg");

        Assert.True(arsenal.SwitchLast(Player) == SwitchOutcome.Switched, "lastinv switches");
        Assert.True(Held(arsenal) == "weapon_crowbar",
            $"lastinv goes back to what was held before, not to {Held(arsenal)}");

        // And it toggles, which is the whole reason the bind exists.
        arsenal.SwitchLast(Player);
        Assert.True(Held(arsenal) == "weapon_smg1", "and back again");
    }

    [Fact]
    public void LastinvHasNothingToGoBackToBeforeTheFirstSwitch()
    {
        var arsenal = WithLoadout();

        Assert.True(arsenal.SwitchLast(Player) == SwitchOutcome.NoHistory,
            "nothing was held before the first weapon, so lastinv says so");
    }

    /// <summary>
    /// A run of switches, each landing on the weapon asked for. The live check
    /// this mirrors is cs_arsenal plus cs_cl_viewmodel_report after each one.
    /// </summary>
    [Fact]
    public void ASequenceOfSwitchesEachLandsOnWhatWasAskedFor()
    {
        var arsenal = WithLoadout();

        var script = new (string Verb, Func<SwitchOutcome> Do, string Expect)[]
        {
            ("ss_slot 3", () => arsenal.SwitchSlot(Player, 3), "weapon_shotgun"),
            ("invnext",   () => arsenal.SwitchNext(Player),    "weapon_crowbar"),
            ("invnext",   () => arsenal.SwitchNext(Player),    "weapon_pistol"),
            ("invprev",   () => arsenal.SwitchPrev(Player),    "weapon_crowbar"),
            ("lastinv",   () => arsenal.SwitchLast(Player),    "weapon_pistol"),
            ("ss_use",    () => arsenal.Switch(Player, "weapon_smg1"), "weapon_smg1"),
            ("invprev",   () => arsenal.SwitchPrev(Player),    "weapon_pistol"),
            ("lastinv",   () => arsenal.SwitchLast(Player),    "weapon_smg1"),
        };

        foreach (var (verb, act, expect) in script)
        {
            Assert.True(act() == SwitchOutcome.Switched, $"{verb} switches");
            Assert.True(Held(arsenal) == expect, $"{verb} lands on {expect}, not {Held(arsenal)}");
        }
    }

    //-------------------------------------------------------------------------
    // Refusals, which are the half that used to be silent.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A dead player's switch is refused — correctly, since death strips the
    /// inventory — but it is refused BY NAME. The distinction from "carries
    /// nothing" is the point: a script driving switches has to be able to tell
    /// "wait for the respawn" from "you were never given anything".
    /// </summary>
    [Fact]
    public void ADeadPlayersSwitchIsRefusedAsDeathRatherThanAsSilence()
    {
        var (arsenal, _, _) = Armed();

        foreach (var name in Loadout)
            arsenal.GiveWeapon(Player, name);

        // Death, as the cleanup does it.
        var (dead, _, _) = Armed(alive: false);

        Assert.True(dead.SwitchNext(Player) == SwitchOutcome.NotArmed,
            "a player this arsenal has never armed is NotArmed, not dead");

        // The same arsenal, now told its player has died.
        arsenal.Strip(Player);

        Assert.True(arsenal.SwitchNext(Player) == SwitchOutcome.NothingCarried,
            "a stripped but living player carries nothing");
    }

    [Fact]
    public void DeathIsItsOwnRefusal()
    {
        var (arsenal, engine, _) = Armed();

        foreach (var name in Loadout)
            arsenal.GiveWeapon(Player, name);

        // The player dies. IsAlive is what the arsenal asks, and it is the only
        // thing that changes here — the inventory is deliberately left in place
        // so the refusal cannot be coming from an empty list.
        engine.Returns("Players.IsAlive", false);

        Assert.True(arsenal.SwitchNext(Player) == SwitchOutcome.PlayerIsDead,
            "the refusal names death, not an empty inventory");

        Assert.True(arsenal.SwitchSlot(Player, 1) == SwitchOutcome.PlayerIsDead, "and so does ss_slot");
        Assert.True(arsenal.SwitchPrev(Player) == SwitchOutcome.PlayerIsDead, "and invprev");
        Assert.True(arsenal.SwitchLast(Player) == SwitchOutcome.PlayerIsDead, "and lastinv");
        Assert.True(arsenal.Switch(Player, "weapon_pistol") == SwitchOutcome.PlayerIsDead, "and ss_use");
    }

    [Fact]
    public void TheOtherRefusalsAreDistinctFromOneAnother()
    {
        var arsenal = WithLoadout();

        Assert.True(arsenal.Switch(Player, "weapon_crowbar") == SwitchOutcome.AlreadyHeld,
            "asking for what is already in the hands is not a fault, and says so");

        Assert.True(arsenal.Switch(Player, "weapon_rpg") == SwitchOutcome.NotOwned,
            "a weapon this player does not own");

        Assert.True(arsenal.SwitchSlot(Player, 9) == SwitchOutcome.NoSuchSlot,
            "a slot past the end of the inventory");

        Assert.True(arsenal.SwitchSlot(Player, -1) == SwitchOutcome.NoSuchSlot,
            "and one before the start");
    }

    /// <summary>
    /// lastinv remembers a WEAPON, not a slot — because slots move. Dropping a
    /// weapon takes a row out of the list and shifts every slot after it down,
    /// so a remembered index would name a different gun, or nothing at all.
    ///
    /// <para>
    /// The arrangement is chosen so the two implementations disagree: the
    /// weapon to go back to is the LAST slot, and the drop takes the list one
    /// shorter than the index that named it. A slot-based lastinv is left
    /// pointing past the end.
    /// </para>
    /// </summary>
    [Fact]
    public void LastinvSurvivesTheSlotsMovingUnderIt()
    {
        var arsenal = WithLoadout();      // crowbar 0, pistol 1, smg1 2, shotgun 3

        arsenal.SwitchSlot(Player, 3);    // the shotgun, in the last slot
        arsenal.SwitchSlot(Player, 1);    // the pistol; the shotgun is now "last"

        arsenal.DropActive(Player, new Vector(0, 0, 0));

        // crowbar 0, smg1 1, shotgun 2 — the shotgun has moved from slot 3 to 2,
        // and slot 3 no longer exists.
        var slots = arsenal.Holdings(Player).Select(h => h.ClassName).ToList();

        Assert.True(slots.SequenceEqual(new[] { "weapon_crowbar", "weapon_smg1", "weapon_shotgun" }),
            $"the drop shifted the slots down: [{string.Join(", ", slots)}]");

        Assert.True(arsenal.SwitchLast(Player) == SwitchOutcome.Switched,
            "lastinv still has somewhere to go");

        Assert.True(Held(arsenal) == "weapon_shotgun",
            $"and it is the shotgun that was held, wherever its slot went — not {Held(arsenal)}");
    }
}
