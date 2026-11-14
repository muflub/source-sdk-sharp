using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The melted knife at the switch, where stock refuses it one step earlier
/// than the swing: <c>CTFKnife::CanDeploy</c>
/// (<c>tf_weapon_knife.cpp:521-531</c>) answers no until the knife
/// regenerates, so <c>Weapon_CanSwitchTo</c>
/// (<c>basecombatcharacter_shared.cpp:88</c>) stands the spy at his holstered
/// knife — the slot key, the wheel and the named weapon alike. The chain runs
/// over the real <see cref="TFSpy"/> the burn layer stamps.
/// </summary>
public sealed class TFKnifeDeployFacts
{
    private static readonly EntityHandle Player = new(0x0001_0000u | 3);

    private const string Knife = "tf_weapon_knife";
    private const string Revolver = "tf_weapon_revolver";

    /// <summary>
    /// The switch's own walk over the real behaviour chain: the knife
    /// holstered, the melt stamped as the burn layer stamps it, the refused
    /// ask, and the switch once the window is past — the hands' answer read
    /// from the arsenal each time.
    /// </summary>
    [Fact]
    public void TheMeltedKnifeRefusesTheSwitchUntilItRegenerates()
    {
        var rig = new SwitchRig();
        var spies = rig.Spies.Spies;

        // The revolver in the hands, the knife holstered beside it.
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Revolver));
        Assert.Equal(Revolver, rig.Arsenal.ActiveWeaponName(Player));

        // The melt, the damage layer's own ask (TFDamageLayer's
        // spies?.MeltKnife): the Spy-cicle's ten seconds from now.
        spies.MeltKnife(Player.EntryIndex, rig.Now, seconds: 10f);

        // Inside the window the knife does not come out: the slot key and the
        // named weapon stand refused, the revolver stays.
        Assert.Equal(SwitchOutcome.CannotDeploy, rig.Arsenal.Switch(Player, Knife));
        Assert.Equal(SwitchOutcome.CannotDeploy, rig.Arsenal.SwitchSlot(Player, rig.KnifeSlot()));
        Assert.Equal(Revolver, rig.Arsenal.ActiveWeaponName(Player));

        // Past it (:523): the same ask walks, and the knife is the hands.
        rig.Now = 110.5f;
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.SwitchSlot(Player, rig.KnifeSlot()));
        Assert.Equal(Knife, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>
    /// Stock's order on the press when the HELD item carries
    /// <c>holster_anim_time</c>: the holster window runs first — the first ask
    /// starts the anim, lowers the held weapon and waits
    /// (<c>tf_player_shared.cpp:11798-11815</c>, returning before anything is
    /// asked of the candidate), mid-window asks likewise (:11819-11821), and
    /// only the press the window expired on asks the melted knife's
    /// <c>CanDeploy</c> (:11836 through <c>BaseClass::Weapon_Switch</c>), the
    /// window cleared first (:11825).
    /// </summary>
    [Fact]
    public void AHolsteredItemLowersBeforeTheRefusedDeployIsJudged()
    {
        var rig = new SwitchRig();
        var spies = rig.Spies.Spies;

        // The held revolver's item carries holster_anim_time — the owners
        // seam HolsterSeconds reads (:11808), answered through the rig's
        // FakeOwners the way the holster facts answer it.
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(57, TFWeaponBehaviour.HolsterAnimAttribute, TFWeaponBehaviour.HolsterAnimAttribute,
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("holster")),
            0.5f));
        rig.Weapons.Owners.ByName[Player.EntryIndex] = new() { [Revolver] = item };

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Revolver));
        spies.MeltKnife(Player.EntryIndex, rig.Now, seconds: 10f);

        // The first ask: the anim starts, the window arms — a Deferred answer,
        // not yet the melt's refusal; the held weapon lowers inside the
        // armed window (the armed window IS the lowering).
        Assert.Equal(SwitchOutcome.Deferred, rig.Arsenal.Switch(Player, Knife));
        Assert.Equal(100.5f, rig.Arsenal.CarryFor(Player).HolsterUntil, 4);   // :11813's curtime + anim time
        Assert.Equal(Revolver, rig.Arsenal.ActiveWeaponName(Player));

        // Mid-window asks stand at the same (:11819-11821), asking nothing of
        // the candidate either side of the answer.
        Assert.Equal(SwitchOutcome.Deferred, rig.Arsenal.Switch(Player, Knife));
        Assert.Equal(Revolver, rig.Arsenal.ActiveWeaponName(Player));

        // Past the window: it clears before anything else (:11825) and the
        // press walks on — there, and only there, the melted knife's answer
        // stands the switch at the revolver (:523 through Weapon_CanSwitchTo).
        rig.Now = 100.6f;
        Assert.Equal(SwitchOutcome.CannotDeploy, rig.Arsenal.Switch(Player, Knife));
        Assert.Equal(0f, rig.Arsenal.CarryFor(Player).HolsterUntil, 4);       // the else-clear runs before the base ask
        Assert.Equal(Revolver, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>
    /// The control: the same walk over an unmelted knife — the fresh stamps
    /// answer stock's base true (:77), the switch the melt refused.
    /// </summary>
    [Fact]
    public void AnUnmeltedKnifeTakesTheSameSwitch()
    {
        var rig = new SwitchRig();

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Revolver));
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Knife));
        Assert.Equal(Knife, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>
    /// The melt's wire (<c>tf_weapon_knife.cpp:33-40</c>): the same ask that
    /// stamps the slice's pair stamps the carrier's
    /// (<see cref="TFPlayerNetState.KnifeMeltTimestamp"/>, the window beside
    /// it), the words the client's <c>CanDeploy</c> recomputes the refusal
    /// from (:523) where no spy slice stands.
    /// </summary>
    [Fact]
    public void TheMeltStampsTheCarrierTheClientReads()
    {
        var rig = new SwitchRig();

        rig.Spies.Spies.MeltKnife(Player.EntryIndex, rig.Now, seconds: 10f);

        Assert.Equal(100f, rig.Spies.State.KnifeMeltTimestamp, 4);
        Assert.Equal(10f, rig.Spies.State.KnifeRegenerateDuration, 4);
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// One spy's carry over the real TF behaviour chain — the knife hook over
    /// the spy slice's state — and the arsenal whose switch asks it.
    /// </summary>
    private sealed class SwitchRig
    {
        public readonly SpyRig Spies = new();
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;

        /// <summary>The behaviour world, so facts can answer the owners seam (item words).</summary>
        public readonly TFWeaponWorld Weapons;

        /// <summary>The clock, the commands' and the switch's own.</summary>
        public float Now = 100f;

        private readonly EntityHandle _weapon = new(0x0003_0010u);
        private readonly EntityHandle _viewModel = new(0x0003_0011u);

        public SwitchRig()
        {
            var created = new Queue<EntityHandle>([_weapon, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", 3)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", default(Vector))
                .Returns("Entities.GetAngles", default(Vector))
                .Returns("Players.IsAlive", true)
                .Returns("Entities.GetEyeAngles", default(Vector))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .ReturnsEachCall("Clock.Now", () => Now)
                .ReturnsEachCall("Entities.Create", () => created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_spy");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            // The TF behaviours over the knife hook, the melt answered from
            // the spy slice the rig attached.
            Weapons = new TFWeaponWorld(new TFKnifeServerHook(Spies.Spies));
            Arsenal = new Arsenal(Spies.Log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: Weapons.Ammo, behaviours: Weapons.Behaviours);

            Assert.True(Arsenal.GiveWeapon(Player, Knife), "the rig's spy carries the knife");
            Assert.True(Arsenal.GiveWeapon(Player, Revolver), "the rig's spy carries the revolver");
        }

        /// <summary>The knife's carry slot, as the switch's own walk names it.</summary>
        public int KnifeSlot()
        {
            int slot = Arsenal.Holdings(Player).ToList()
                .FindIndex(h => h.ClassName == Knife);
            Assert.True(slot >= 0, "the carry holds the knife");
            return slot;
        }
    }
}
