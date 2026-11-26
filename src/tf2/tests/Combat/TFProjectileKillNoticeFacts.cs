using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// A projectile's kill in the notice, as stock names it: the inflictor's class
/// with the projectile's own weapon id (<c>GetKillingWeaponName</c>,
/// <c>tf_gamerules.cpp:12461-12474</c>), then the scorer's weapon of that id
/// renames it by its item's <c>item_iconname</c> (<c>DeathNotice</c>,
/// <c>:12905-12925</c>): the Huntsman's arrow is <c>huntsman</c>.
/// </summary>
public sealed class TFProjectileKillNoticeFacts
{
    private const string Bow = "tf_weapon_compound_bow";
    private const string FlareGun = "tf_weapon_flaregun";

    /// <summary>The trimmed shipped schema with these facts' two items added, their icon names as shipped.</summary>
    private static ItemSchema Schema()
    {
        const string items = """
                    "56"
                    {
                        "name"  "The Huntsman"
                        "item_class"    "tf_weapon_compound_bow"
                        "item_slot" "primary"
                        "item_iconname" "huntsman"
                    }
                    "740"
                    {
                        "name"  "The Scorch Shot"
                        "item_class"    "tf_weapon_flaregun"
                        "item_slot" "secondary"
                        "item_iconname" "scorch_shot"
                    }
            """;
        string text = EconTestFiles.Fixture("items_game.trimmed.txt");
        int at = text.IndexOf("\"items\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "the trimmed schema has its items block");
        int open = text.IndexOf('{', at);
        text = text.Insert(open + 1, "\n" + items + "\n");
        var schema = new ItemSchema(new EconTestFiles().With(ItemSchema.FilePath, text), new RecordingLog());
        Assert.True(schema.IsLoaded, schema.Failure);
        Assert.Equal("huntsman", schema.Item(56)!.Keys.GetString("item_iconname", null));
        return schema;
    }

    private static Dictionary<string, object> Notice(CombatRig rig, EntityHandle inflictor)
    {
        rig.Engine.ReturnsEachCall("Players.GetHandle", () =>
            new EntityHandle((uint)(0x0001_0000u + (uint)rig.Engine.Calls[^1].ArgAs<int>(0))));
        var notice = new TFDeathNotice(rig.Engine, rig.Engine, rig.Records, rig.Clock, rig.Carriers, rig.Level,
                                       new TFDominations(rig.Carriers), rig.Owners, new AttributeHooks(),
                                       rig.Healing, rig.Conditions, rig.Entities, rig.Stats);
        var fields = new TFDeathNoticeTests.Fields();
        notice.Fill(new DeathNoticeFacts(CombatRig.Victim, CombatRig.Attacker, CombatRig.Attacker, inflictor, ""), fields);
        return fields.Set;
    }

    /// <summary>The arrow's kill, its shooter's bow carrying item 56 (or nothing when <paramref name="item"/> is null).</summary>
    private static Dictionary<string, object> ArrowKill(int? item)
    {
        var rig = new CombatRig { AttackerWeapon = Bow };
        TFArrow arrow = new ProjectileRig().Arrow();
        arrow.Handle = ProjectileRig.ProjectileHandle;
        arrow.ArmedBy(TFWeaponId.CompoundBow, Bow, critical: false, lit: false, 120f, TFProjectileDefs.ArrowGravity(1f));
        rig.Entities.Placed[arrow.Handle] = arrow;
        rig.Engine.Returns("Entities.GetClassName", TFProjectileEntityNames.Arrow);
        if (item is { } defindex)
            rig.Owners.ByName[CombatRig.Attacker.EntryIndex] = new() { [Bow] = new AttributeContainer(Schema().Item(defindex)) };

        using (rig.Inflictors.Push(arrow.Handle, default))
        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.CompoundBow, Bow))
            rig.Hit(500f, TFWeaponDamageTypes.Of(TFWeaponId.CompoundBow));
        return Notice(rig, arrow.Handle);
    }

    [Fact]
    public void An_arrow_kill_is_the_huntsman_with_the_bows_id()
    {
        var f = ArrowKill(item: 56);

        Assert.Equal("huntsman", f["weapon"]);
        Assert.Equal((int)TFWeaponId.CompoundBow, f["weaponid"]);
    }

    /// <summary>The control: a bow with no item keeps the inflictor's class, and still carries the bow's id.</summary>
    [Fact]
    public void An_arrow_kill_from_a_bow_without_an_item_is_the_arrows_class()
    {
        var f = ArrowKill(item: null);

        Assert.Equal(TFProjectileEntityNames.Arrow, f["weapon"]);
        Assert.Equal((int)TFWeaponId.CompoundBow, f["weaponid"]);
    }

    /// <summary>
    /// The Scorch Shot's direct hit (no custom, the flare the inflictor): the
    /// flare gun's id, renamed by item 740's <c>item_iconname</c>, stock's
    /// <c>scorch_shot</c>.
    /// </summary>
    [Fact]
    public void A_scorch_shots_direct_kill_is_the_scorch_shot()
    {
        var rig = new CombatRig { AttackerWeapon = FlareGun };
        var projectiles = new ProjectileRig();
        TFFlare flare = projectiles.Flare();
        flare.Handle = ProjectileRig.ProjectileHandle;
        projectiles.Engine.Returns("Players.GetTeam", 3).Returns("Players.GetHandle", ProjectileRig.Player);
        flare.ArmedBy(30f, TFWeaponId.Flaregun, FlareGun, critical: false, TFProjectileDefs.FlareGravity,
                      ownerIndex: CombatRig.Attacker.EntryIndex, flareMode: TFProjectileBehaviour.FlareScorch);
        rig.Entities.Placed[flare.Handle] = flare;
        rig.Engine.Returns("Entities.GetClassName", TFProjectileEntityNames.Flare);
        rig.Owners.ByName[CombatRig.Attacker.EntryIndex] = new() { [FlareGun] = new AttributeContainer(Schema().Item(740)) };

        using (rig.Inflictors.Push(flare.Handle, default))
        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.Flaregun, FlareGun))
            rig.Hit(500f, TFWeaponDamageTypes.Of(TFWeaponId.Flaregun));
        var f = Notice(rig, flare.Handle);

        Assert.Equal("scorch_shot", f["weapon"]);
        Assert.Equal((int)TFWeaponId.Flaregun, f["weaponid"]);
    }
}
