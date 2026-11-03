using System.Globalization;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.OracleTests;

using TF2.Shared;
using TF2.Shared.Combat;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// <see cref="TFWeaponDamageTypes"/> against the PRISTINE
/// <c>g_aWeaponDamageTypes</c> (<c>tf_shareddefs.cpp:715-828</c>), read
/// through the oracle project's <see cref="ValveSdk"/>. Both directions: the
/// array's comments name the <c>TF_WEAPON_*</c> of the <see cref="TFWeaponId"/>
/// enum member at the same position, and every entry's bits are the ones
/// <see cref="TFWeaponDamageTypes.Of"/> answers for that member.
/// </summary>
public sealed class TFWeaponDamageTypesTests
{
    private const string SharedDefsCpp = "src/game/shared/tf/tf_shareddefs.cpp";

    private static DamageType Bits(string expression) => expression.Split('|')
        .Select(token => token.Trim())
        .Select(token => token switch
        {
            "DMG_GENERIC" => DamageType.Generic,
            "DMG_CLUB" => DamageType.Club,
            "DMG_SLASH" => DamageType.Slash,
            "DMG_BUCKSHOT" => DamageType.Buckshot,
            "DMG_BULLET" => DamageType.Bullet,
            "DMG_BLAST" => DamageType.Blast,
            "DMG_IGNITE" => TFDamageBits.Ignite,
            "DMG_BURN" => DamageType.Burn,
            "DMG_SONIC" => DamageType.Sonic,
            "DMG_POISON" => DamageType.Poison,
            "DMG_PARALYZE" => DamageType.Paralyze,
            "DMG_PREVENT_PHYSICS_FORCE" => DamageType.PreventPhysicsForce,
            "DMG_USEDISTANCEMOD" => TFDamageBits.UseDistanceMod,
            "DMG_NOCLOSEDISTANCEMOD" => TFDamageBits.NoCloseDistanceMod,
            "DMG_USE_HITLOCATIONS" => TFDamageBits.UseHitLocations,
            "DMG_HALF_FALLOFF" => TFDamageBits.HalfFalloff,
            "DMG_RADIUS_MAX" => TFDamageBits.RadiusMax,
            _ => throw new InvalidOperationException($"a DMG_ set the table never named: {token}"),
        })
        .Aggregate(DamageType.Generic, (set, bit) => set | bit);

    /// <summary>The table's entries, positionally: the bits and the TF_WEAPON_* the comment names.</summary>
    private static List<(DamageType Bits, string? Comment)> Stock()
    {
        string text = ValveSdk.Read(SharedDefsCpp);
        string body = Regex.Match(text, @"int g_aWeaponDamageTypes\[\]\s*=\s*\{(.*?)\};", RegexOptions.Singleline)
            .Groups[1].Value;
        var entries = new List<(DamageType, string?)>();
        foreach (Match entry in Regex.Matches(body, @"(DMG_\w+(?:\s*\|\s*DMG_\w+)*)\s*,?\s*(?://\s*(TF_\w+))?"))
            entries.Add((Bits(entry.Groups[1].Value),
                         entry.Groups[2].Success ? entry.Groups[2].Value : null));
        return entries;
    }

    /// <summary><c>BatWood</c> as stock comments it: <c>TF_WEAPON_BAT_WOOD</c>.</summary>
    private static string Commented(TFWeaponId weapon)
        => "TF_WEAPON_" + Regex.Replace(weapon.ToString(), "([A-Z])", "_$1", RegexOptions.CultureInvariant)
                               .TrimStart('_').ToUpper(CultureInfo.InvariantCulture);

    [Fact]
    public void Every_entry_is_the_weapon_at_its_position()
    {
        var stock = Stock();
        var members = Enum.GetValues<TFWeaponId>().TakeWhile(w => w != TFWeaponId.Count).ToList();

        Assert.Equal(members.Count, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.True(stock[i].Comment == Commented(members[i]),
                        $"{Commented(members[i])}: stock names {stock[i].Comment ?? "nothing"} at {i}");
    }

    [Fact]
    public void Every_entrys_bits_are_the_switchs()
    {
        var stock = Stock();
        var members = Enum.GetValues<TFWeaponId>().TakeWhile(w => w != TFWeaponId.Count).ToList();

        Assert.Equal(members.Count, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.True(TFWeaponDamageTypes.Of(members[i]) == stock[i].Bits,
                        $"{members[i]}: stock has 0x{(int)stock[i].Bits:X}, the switch answers "
                        + $"0x{(int)TFWeaponDamageTypes.Of(members[i]):X}");
    }
}
