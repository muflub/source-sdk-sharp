using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The disguise weapon (<c>CTFPlayerShared::DetermineDisguiseWeapon</c>,
/// tf_player_shared.cpp:8394): the weapon a disguised spy is seen holding.
/// The core keeps one carried weapon entity per player, so the disguise is
/// the carried entity dressed with the disguise class's primary world model
/// for as long as the disguise stands — over the deploys a weapon switch
/// makes, and back to the carried weapon's own model when it ends.
/// </summary>
public sealed class TFDisguiseWeaponTests
{
    private static readonly EntityHandle Weapon = new(0x0003_0001u);

    private static List<(EntityHandle Entity, string Model)> Models(SpyRig rig)
        => rig.Engine.AllOf("EntityRender.SetModel")
                      .Select(c => (c.ArgAs<EntityHandle>(0), c.ArgAs<string>(1)))
                      .ToList();

    private static WeaponType Row(SpyRig rig, string name)
    {
        WeaponType? found = rig.Catalog.For(name);
        Assert.True(found is not null, $"no {name} in the table");
        Assert.NotEqual("", found!.Value.WorldModel);
        return found.Value;
    }

    private static string SoldierPrimary(SpyRig rig)
    {
        var row = Row(rig, "tf_weapon_rocketlauncher");
        Assert.Contains("rocket", row.WorldModel, StringComparison.OrdinalIgnoreCase);
        return row.WorldModel;
    }

    private static string SpyPrimary(SpyRig rig) => Row(rig, "tf_weapon_revolver").WorldModel;

    [Fact]
    public void Completing_the_disguise_dresses_the_carried_weapon_with_the_disguise_class_primary()
    {
        var rig = new SpyRig();
        string rocket = SoldierPrimary(rig);

        // While the disguise is still running, nothing is dressed.
        Assert.True(rig.Slice.Disguise(100f, TFClass.Soldier, TFTeam.Blue));
        rig.Slice.Tick(101.9f);
        Assert.Empty(Models(rig));

        rig.Slice.Tick(102.1f);

        var models = Models(rig);
        Assert.Equal([(Weapon, rocket)], models);
    }

    [Fact]
    public void A_deploy_while_disguised_dresses_the_disguise_back()
    {
        var rig = new SpyRig();
        string rocket = SoldierPrimary(rig);

        Assert.True(rig.Slice.Disguise(100f, TFClass.Soldier, TFTeam.Blue));
        rig.Slice.Tick(102.1f);
        Assert.Equal(rocket, Models(rig).Single().Model);

        // The switch's deploy: the arsenal re-dresses the entity with the
        // carried weapon, and the next think puts the disguise back.
        rig.Deploy();
        rig.Slice.Tick(102.2f);

        Assert.Equal(rocket, Models(rig).Last().Model);
        Assert.Equal(2, Models(rig).Count);
    }

    [Fact]
    public void Losing_the_disguise_restores_the_carried_weapon_model()
    {
        var rig = new SpyRig
        {
            ActiveWeapon = "tf_weapon_revolver",
        };
        string rocket = SoldierPrimary(rig);
        string revolver = SpyPrimary(rig);

        Assert.True(rig.Slice.Disguise(100f, TFClass.Soldier, TFTeam.Blue));
        rig.Slice.Tick(102.1f);

        // Disguising as the spy's own class and team ends the disguise
        // (tf_player_shared.cpp:8244-8249); the next think dresses the
        // carried weapon with its own model again.
        Assert.True(rig.Slice.Disguise(103f, TFClass.Spy, TFTeam.Red));
        rig.Slice.Tick(103.1f);

        var models = Models(rig);
        Assert.Equal(2, models.Count);
        Assert.Equal((Weapon, rocket), models[0]);
        Assert.Equal((Weapon, revolver), models[1]);
    }

    [Fact]
    public void A_disguise_change_while_disguised_dresses_the_new_class()
    {
        var rig = new SpyRig();
        string rocket = SoldierPrimary(rig);

        var demo = Row(rig, "tf_weapon_grenadelauncher");

        Assert.True(rig.Slice.Disguise(100f, TFClass.Soldier, TFTeam.Blue));
        rig.Slice.Tick(102.1f);

        // The quick disguise: a new class while the first stands, no deploy
        // between (the epoch has not moved) — the row is what moves.
        Assert.True(rig.Slice.Disguise(103f, TFClass.Demoman, TFTeam.Blue));
        rig.Slice.Tick(103.6f);

        var models = Models(rig);
        Assert.Equal(2, models.Count);
        Assert.Equal((Weapon, rocket), models[0]);
        Assert.Equal((Weapon, demo.WorldModel), models[1]);
    }

    [Fact]
    public void A_spy_with_no_carried_weapon_is_left_alone()
    {
        var rig = new SpyRig(withWeapon: false);

        Assert.True(rig.Slice.Disguise(100f, TFClass.Soldier, TFTeam.Blue));

        rig.Slice.Tick(102.1f);
        rig.Slice.Tick(103.1f);

        Assert.Empty(Models(rig));
    }
}
