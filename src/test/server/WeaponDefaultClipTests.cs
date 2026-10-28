using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What a weapon brings when it is first picked up, stock's Weapon_Equip over
/// the script's default_clip: the clip filled and the rest into the pool (the
/// crossbow's 1 + 4, the AR2's 30 + 30), all of it in the pool for a weapon
/// with no clip (the RPG's 3), and a duplicate worth the same
/// (Weapon_EquipAmmoOnly).
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): a
/// given crossbow had no second bolt, so it never reloaded where stock's did.
/// </para>
/// </summary>
public class WeaponDefaultClipTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private static Arsenal Arsenal()
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f).Returns("EntityRender.SequenceDuration", 1f)
            .Ignores("EntityRender.LookupSequenceByActivity");
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");
        return new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry), engine, engine,
                           engine, engine, engine, engine, engine, registry, engine, new DamageHitGroups());
    }

    private static (int Clip, int Pool) Given(string weapon, string ammo, int times = 1)
    {
        var arsenal = Arsenal();
        for (int i = 0; i < times; i++)
            arsenal.GiveWeapon(Player, weapon);
        int clip = arsenal.Holdings(Player).First(h => h.Item1 == weapon).Item2;
        int pool = arsenal.Pools(Player).TryGetValue(ammo, out int p) ? p : 0;
        return (clip, pool);
    }

    [Fact]
    public void ACrossbowBringsABoltLoadedAndFourSpare() => Assert.Equal((1, 4), Given("weapon_crossbow", "XBowBolt"));

    [Fact]
    public void AnAr2BringsTwoClips() => Assert.Equal((30, 30), Given("weapon_ar2", "AR2"));

    [Fact]
    public void AnRpgBringsThreeRockets() => Assert.Equal(3, Given("weapon_rpg", "RPG_Round").Pool);

    [Fact]
    public void APistolBringsItsClipAndNoSpare() => Assert.Equal((18, 0), Given("weapon_pistol", "Pistol"));

    [Fact]
    public void ASecondCrossbowIsWorthFiveBolts() => Assert.Equal((1, 9), Given("weapon_crossbow", "XBowBolt", times: 2));
}
