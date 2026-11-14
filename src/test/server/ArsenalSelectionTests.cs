using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What the client's weapon selection walks (stock's m_hMyWeapons and each
/// weapon's CanBeSelected), published on the player's state as bits by
/// WeaponTypes index: the weapons carried, and those with nothing to fire.
/// </summary>
public class ArsenalSelectionTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private static uint Bit(string weapon) => 1u << WeaponCatalog.Stock().IndexOf(weapon);

    private static (Arsenal Arsenal, PlayerNetState State, ScriptedEntityRegistry Registry,
                    WeaponSwitchTests.SpawningEntities World) Armed(params string[] weapons)
    {
        var (arsenal, _, world, registry) = WeaponDeathTests.LiveWorld();
        foreach (string weapon in weapons)
            Assert.True(arsenal.GiveWeapon(Player, weapon), weapon);

        return (arsenal, ((SsPlayer)registry.Get(Player)!).State, registry, world);
    }

    [Fact]
    public void Every_carried_weapon_is_a_bit_by_its_type()
    {
        // Given in the other order from the table's, so a bit by slot would differ.
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponSmg1, EntityNames.Weapon357);
        Assert.NotEqual(3u, Bit(EntityNames.WeaponSmg1) | Bit(EntityNames.Weapon357));

        arsenal.Frame();

        Assert.Equal(Bit(EntityNames.WeaponSmg1) | Bit(EntityNames.Weapon357), state.CarriedWeapons);
    }

    [Fact]
    public void A_weapon_that_uses_no_ammunition_is_never_empty()
    {
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponCrowbar);

        arsenal.Frame();

        Assert.Equal(0u, state.EmptyWeapons);
    }

    [Fact]
    public void A_weapon_with_nothing_in_its_clip_or_pool_is_empty()
    {
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponCrowbar, EntityNames.WeaponPistol);
        var carry = arsenal.CarryFor(Player);
        carry.Ammo.Clear();
        carry.Weapons.Find(o => o.TypeIndex == WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol))!.Clip = 0;

        arsenal.Frame();

        Assert.Equal(Bit(EntityNames.WeaponPistol), state.EmptyWeapons);
    }

    [Fact]
    public void The_active_weapons_own_clip_counts_for_it()
    {
        var (arsenal, state, registry, world) = Armed(EntityNames.WeaponPistol, EntityNames.WeaponCrowbar);
        var carry = arsenal.CarryFor(Player);
        carry.Ammo.Clear();
        int pistol = carry.Weapons.FindIndex(o => o.TypeIndex == WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol));
        carry.Weapons[pistol].Clip = 0;
        Assert.Equal(pistol, carry.ActiveSlot);
        ((SsWeapon)registry.Get(world.WeaponHandles[^1])!).State.Clip = 3;

        arsenal.Frame();

        Assert.Equal(0u, state.EmptyWeapons);
    }

    [Fact]
    public void A_holstered_weapons_kept_clip_counts_for_it()
    {
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponCrowbar, EntityNames.WeaponPistol);
        var carry = arsenal.CarryFor(Player);
        carry.Ammo.Clear();
        int pistol = carry.Weapons.FindIndex(o => o.TypeIndex == WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol));
        Assert.NotEqual(pistol, carry.ActiveSlot);

        carry.Weapons[pistol].Clip = 2;
        arsenal.Frame();
        Assert.Equal(0u, state.EmptyWeapons);

        carry.Weapons[pistol].Clip = 0;
        arsenal.Frame();
        Assert.Equal(Bit(EntityNames.WeaponPistol), state.EmptyWeapons);
    }

    [Fact]
    public void Every_pool_is_published_by_its_wire_index()
    {
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponSmg1);
        var carry = arsenal.CarryFor(Player);
        carry.Ammo.Clear();
        carry.Ammo["SMG1"] = 120;
        carry.Ammo["smg1_grenade"] = 2;

        arsenal.Frame();

        var ammo = AmmoCatalog.Stock();
        Assert.Equal((120, 2, 0), (state.Ammo[ammo.WireIndex("SMG1")], state.Ammo[ammo.WireIndex("SMG1_Grenade")],
                                   state.Ammo[ammo.WireIndex("Pistol")]));
        Assert.Equal(0, state.Ammo[0]);
    }

    [Fact]
    public void A_pool_counts_for_a_weapon_with_an_empty_clip()
    {
        var (arsenal, state, _, _) = Armed(EntityNames.WeaponCrowbar, EntityNames.WeaponPistol);
        var carry = arsenal.CarryFor(Player);
        carry.Ammo.Clear();
        int pistol = carry.Weapons.FindIndex(o => o.TypeIndex == WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol));
        carry.Weapons[pistol].Clip = 0;
        carry.Ammo["Pistol"] = 4;

        arsenal.Frame();

        Assert.Equal(0u, state.EmptyWeapons);
    }

    /// <summary>
    /// Rows past bit 63: the padded catalog lands "wep_pad_52" on type index
    /// 64, the first row the old 64-bit mask could not hold. The bit rides the
    /// Extra word, and the CarriedMask reader must still see it — a reader
    /// that composes the words in a ulong loses that bit.
    /// </summary>
    [Fact]
    public void AWeaponPastBit64IsCarriedOnTheWire()
    {
        var padRows = new WeaponType[53];
        for (int i = 0; i < padRows.Length; i++)
            padRows[i] = new WeaponType($"wep_pad_{i}", Interval: 0.5f);
        var padded = new WeaponCatalog(() => new IWeaponTypeSource[] { new PadSource(padRows) }, new RecordingLog());
        Assert.Equal(64, padded.IndexOf("wep_pad_52"));

        var (_, engine, world, registry) = WeaponDeathTests.LiveWorld();
        var pastMask = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine, engine, engine,
                                   registry, engine, new DamageHitGroups(), weapons: padded);
        Assert.True(pastMask.GiveWeapon(Player, "wep_pad_52"));
        pastMask.Frame();

        var paddedState = ((SsPlayer)registry.Get(Player)!).State;
        Assert.Equal(0u, paddedState.CarriedWeapons);          // the bit is not in the low word
        Assert.Equal(1u, paddedState.CarriedWeaponsExtra);     // it rides the Extra word
        Assert.True((paddedState.CarriedMask & ((UInt128)1 << 64)) != 0);
    }

    /// <summary>A padded rows source, as WeaponCatalogTests builds one.</summary>
    private sealed class PadSource(params WeaponType[] rows) : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => rows;
    }
}
