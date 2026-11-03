using SourceSharp.Abi;

using TF2.Server.Combat;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The burn's immune arm as <see cref="TFAfterburn"/> composes it, stock's
/// walks in their order (<c>tf_player_shared.cpp:6672-6720</c>): the active
/// weapon's own — a holstered weapon's attribute is not consulted — the
/// <c>TF_COND_AFTERBURN_IMMUNE</c> condition, and, for a sniper only, every
/// worn item.
/// </summary>
public sealed class TFAfterburnImmunityTests
{
    private static readonly EntityHandle Victim = CombatRig.Victim;

    /// <summary>The trimmed schema's wearable, with a player model (the MvM canteen).</summary>
    private const int Canteen = 1163;

    private static AttributeDefinition ImmunityDef(int id) => new(id, "attr " + id, TFBurn.ImmunityAttrib,
        AttributeDescriptionFormat.Additive, AttributeEffectType.Positive, false, false,
        AttributeValueType.Default, MergedKeys.Block("x"));

    private static AttributeContainer ImmunityKit()
    {
        var container = new AttributeContainer();
        container.Set(AttributeValue.OfFloat(ImmunityDef(7001), 1f));
        return container;
    }

    /// <summary>The victim's kit as the loadout leaves it: the item provided into the manager, its row answered by name.</summary>
    private static void Equipped(CombatRig rig, AttributeContainer item)
    {
        var manager = new AttributeContainer();
        item.ProvideTo(manager);
        rig.Owners.Managers[Victim.EntryIndex] = manager;
        rig.Owners.ByName[Victim.EntryIndex] = new Dictionary<string, IAttributeProvider>
        {
            ["tf_weapon_pistol"] = item,
        };
    }

    /// <summary>Attaches the victim's wearables and equips one item bearing <paramref name="attribute"/>.</summary>
    private static void Wearing(CombatRig rig, TFClass cls, AttributeValue attribute)
    {
        ItemSchema schema = EconTestFiles.TrimmedSchema();
        var item = new ItemInstance(1, schema.Item(Canteen)!, 6, [attribute]);

        var wearables = new TFWearables(rig.Carriers, rig.Engine, rig.Entities, rig.Engine, rig.Log);
        wearables.Attach(Victim);

        rig.Engine.Returns("Entities.GetOrigin", default(Vector));
        rig.Engine.Returns("Precache.PrecacheModel", 42);
        rig.Engine.ReturnsEachCall("Entities.Create", () =>
        {
            var handle = new EntityHandle(0x0001_00C8u);
            var entity = new TFWearableEntity(rig.Engine);
            entity.State.BindLocal();
            rig.Entities.Wearables[handle] = entity;
            return handle;
        });

        wearables.Rebuild(Victim, cls, TFTeam.Red, [new LoadoutItem(TFLoadoutSlot.Head, item)]);
    }

    private static TFAfterburn Ignited(CombatRig rig)
    {
        var afterburn = (TFAfterburn)rig.Afterburns.Create(Victim).First();
        afterburn.Attach(Victim);
        afterburn.Ignite(0f, CombatRig.Attacker, TFWeaponId.Flamethrower, "tf_weapon_flamethrower");
        return afterburn;
    }

    [Fact]
    public void A_holstered_weapons_immunity_is_not_asked()
    {
        // The kit's manager holds every item's grant, the pistol's among them
        // but the shotgun is the weapon out: stock asks the active weapon
        // alone (:6674-6682), so the holstered immunity does not protect.
        var rig = new CombatRig();
        Equipped(rig, ImmunityKit());
        rig.VictimWeapon = "tf_weapon_shotgun";

        TFAfterburn afterburn = Ignited(rig);

        Assert.Equal(3.4f, afterburn.Burn.AfterburnDuration, 4);
    }

    [Fact]
    public void The_active_weapons_immunity_is_asked()
    {
        var rig = new CombatRig();
        Equipped(rig, ImmunityKit());   // the pistol is the weapon out

        TFAfterburn afterburn = Ignited(rig);

        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, afterburn.Burn.AfterburnDuration, 4);
    }

    [Fact]
    public void The_condition_immunes_by_itself()
    {
        var rig = new CombatRig();
        Assert.Equal(TFCondAdd.Added, rig.Conditions.Of(Victim.EntryIndex)!.Add(TFCond.AfterburnImmune));

        TFAfterburn afterburn = Ignited(rig);

        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, afterburn.Burn.AfterburnDuration, 4);
    }

    [Fact]
    public void A_snipers_wearable_immunes()
    {
        var rig = new CombatRig();
        rig.VictimState.SetClass(TFClass.Sniper);
        Wearing(rig, TFClass.Sniper, AttributeValue.OfFloat(ImmunityDef(7002), 1f));

        TFAfterburn afterburn = Ignited(rig);

        Assert.Equal(TFCombatDefs.BurningFlameLifePyro, afterburn.Burn.AfterburnDuration, 4);
    }

    [Fact]
    public void Another_classs_wearable_does_not()
    {
        var rig = new CombatRig();   // the victim stays a scout
        Wearing(rig, TFClass.Scout, AttributeValue.OfFloat(ImmunityDef(7002), 1f));

        TFAfterburn afterburn = Ignited(rig);

        Assert.Equal(3.4f, afterburn.Burn.AfterburnDuration, 4);
    }
}
