using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.OracleTests;
using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// TF2's shared enums (<c>src/tf2/shared/TFSharedDefs.cs</c>) and <see cref="TFDamage"/> held against
/// the PRISTINE <c>tf_shareddefs.h</c> / <c>tf_item_constants.h</c> /
/// <c>shareddefs.h</c> (stage1 1.1), read through the oracle project's
/// <see cref="ValveSdk"/>, which fails on a missing clone or file.
///
/// <para>
/// Both directions: the member counts are equal and every stock enumerator
/// has a C# member of its value, so a member added, dropped or renumbered on
/// either side alone fails. Names map mechanically (prefix dropped, the rest
/// PascalCase) with the renames in <see cref="Renames"/>.
/// </para>
/// </summary>
public sealed class TFSharedDefsTests
{
    private const string SharedDefsH = "src/game/shared/tf/tf_shareddefs.h";
    private const string ItemConstantsH = "src/game/shared/tf/tf_item_constants.h";
    private const string CoreSharedDefsH = "src/game/shared/shareddefs.h";
    private const string SharedDefsCpp = "src/game/shared/tf/tf_shareddefs.cpp";

    /// <summary>Stock names whose mechanical spelling we do not use.</summary>
    private static string? Renames(string stock) => stock switch
    {
        "TF_CLASS_HEAVYWEAPONS" => nameof(TFClass.HeavyWeapons),
        "CLASS_LOADOUT_POSITION_COUNT" => nameof(TFLoadoutSlot.Count),
        "TF_NUM_FLAG_EVENTS" => nameof(TFFlagEvent.Count),
        _ => null,
    };

    [Fact]
    public void Every_ETFCond_enumerator_is_a_TfCond_member_of_the_same_value()
        => AssertMirrors<TFCond>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFCond"), "TF_COND_", 133);

    [Fact]
    public void Every_ETFWeaponType_enumerator_is_a_TfWeaponId_member_of_the_same_value()
        => AssertMirrors<TFWeaponId>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFWeaponType"), "TF_WEAPON_", 111);

    [Fact]
    public void Every_ETFClass_enumerator_is_a_TfClass_member_of_the_same_value()
        => AssertMirrors<TFClass>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFClass"), "TF_CLASS_", 13);

    [Fact]
    public void Every_loadout_positions_t_enumerator_is_a_TfLoadoutSlot_member_of_the_same_value()
        => AssertMirrors<TFLoadoutSlot>(Enumerators(ValveSdk.Read(ItemConstantsH), "enum loadout_positions_t"),
                                        "LOADOUT_POSITION_", 21);

    [Fact]
    public void Every_ETFFlagType_enumerator_is_a_TFFlagType_member_of_the_same_value()
        => AssertMirrors<TFFlagType>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFFlagType"), "TF_FLAGTYPE_", 7);

    [Fact]
    public void Every_ETFFlagEventTypes_enumerator_is_a_TFFlagEvent_member_of_the_same_value()
        => AssertMirrors<TFFlagEvent>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFFlagEventTypes"), "TF_FLAGEVENT_", 6);

    [Fact]
    public void The_TF_FLAGINFO_defines_are_TFFlagStatus()
    {
        // #define TF_FLAGINFO_HOME 0, TF_FLAGINFO_STOLEN (1<<0), TF_FLAGINFO_DROPPED (1<<1)
        string text = ValveSdk.Read(SharedDefsH);
        var stock = new[] { "TF_FLAGINFO_HOME", "TF_FLAGINFO_STOLEN", "TF_FLAGINFO_DROPPED" }
            .Select(name => (name, Shift(Define(text, name)))).ToList();

        AssertMirrors<TFFlagStatus>(stock, "TF_FLAGINFO_", 3);

        static int Shift(string value)
        {
            var m = Regex.Match(value, @"^\(1<<(\d)\)$");
            return m.Success ? 1 << int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                             : int.Parse(value, CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public void The_team_enum_and_the_shared_team_defines_are_TfTeam()
    {
        // shareddefs.h: #define TEAM_UNASSIGNED 0, TEAM_SPECTATOR 1, LAST_SHARED_TEAM TEAM_SPECTATOR;
        // tf_shareddefs.h: enum { TF_TEAM_RED = LAST_SHARED_TEAM+1, TF_TEAM_BLUE, TF_TEAM_COUNT };
        string core = ValveSdk.Read(CoreSharedDefsH);
        var symbols = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["TEAM_UNASSIGNED"] = IntDefine(core, "TEAM_UNASSIGNED"),
            ["TEAM_SPECTATOR"] = IntDefine(core, "TEAM_SPECTATOR"),
        };
        symbols["LAST_SHARED_TEAM"] = symbols[Define(core, "LAST_SHARED_TEAM")];

        var stock = new List<(string Name, int Value)>
        {
            ("TF_TEAM_UNASSIGNED", symbols["TEAM_UNASSIGNED"]),
            ("TF_TEAM_SPECTATOR", symbols["TEAM_SPECTATOR"]),
        };
        stock.AddRange(Enumerators(ValveSdk.Read(SharedDefsH), "TF_TEAM_RED", symbols));

        AssertMirrors<TFTeam>(stock, "TF_TEAM_", 5);
    }

    [Fact]
    public void The_ten_TF_DAMAGE_defines_are_TfDamage_constants_of_the_same_value_and_type()
    {
        var defines = Regex.Matches(ValveSdk.Read(SharedDefsH), @"^#define\s+TF_DAMAGE_(\w+)\s+([0-9.]+)(f?)\s*$",
                                    RegexOptions.Multiline);
        Assert.Equal(10, defines.Count);

        var consts = typeof(TFDamage).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(10, consts.Length);

        foreach (Match d in defines)
        {
            string name = Pascal(d.Groups[1].Value);
            var field = typeof(TFDamage).GetField(name);
            Assert.True(field is { IsLiteral: true }, $"TF_DAMAGE_{d.Groups[1].Value} has no TFDamage.{name} const");

            object value = field!.GetRawConstantValue()!;
            if (d.Groups[2].Value.Contains('.'))
                Assert.Equal(float.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture), Assert.IsType<float>(value));
            else
                Assert.Equal(int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture), Assert.IsType<int>(value));
        }
    }

    [Fact]
    public void Every_ETFAmmoType_enumerator_is_a_TfAmmo_member_of_the_same_value()
        => AssertMirrors<TFAmmo>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFAmmoType"), "TF_AMMO_", 8);

    [Fact]
    public void Every_TFStatType_t_enumerator_is_a_TFStat_member_of_the_same_value()
        => AssertMirrors<TFStat>(Enumerators(ValveSdk.Read("src/game/shared/tf/tf_gamestats_shared.h"), "enum TFStatType_t"),
                                 "TFSTAT_", 46);

    [Fact]
    public void Every_ObjectType_t_enumerator_is_a_TfObjectType_member_of_the_same_value()
        => AssertMirrors<TFObjectType>(Enumerators(ValveSdk.Read(SharedDefsH), "enum ObjectType_t"), "OBJ_", 5);

    [Fact]
    public void The_TF_PLAYER_count_defines_are_TfClassLimits()
    {
        string h = ValveSdk.Read(SharedDefsH);
        Assert.Equal(TFClassLimits.WeaponCount, IntDefine(h, "TF_PLAYER_WEAPON_COUNT"));
        Assert.Equal(TFClassLimits.GrenadeCount, IntDefine(h, "TF_PLAYER_GRENADE_COUNT"));
        Assert.Equal(TFClassLimits.BuildableCount, IntDefine(h, "TF_PLAYER_BUILDABLE_COUNT"));
        Assert.Equal(TFClassLimits.BlueprintCount, IntDefine(h, "TF_PLAYER_BLUEPRINT_COUNT"));
    }

    [Fact]
    public void TFEncryption_Key_is_GetTFEncryptionKeys_literal()
    {
        var m = Regex.Match(ValveSdk.Read(SharedDefsCpp),
                            @"GetTFEncryptionKey\s*\(\s*void\s*\)\s*\{\s*return\s*\(unsigned char \*\)""([^""]*)""\s*;");
        Assert.True(m.Success, "GetTFEncryptionKey's body is not where the oracle expects it");
        Assert.Equal(TFEncryption.Key, m.Groups[1].Value);
    }

    [Fact]
    public void The_tf2_mod_publishes_this_key_for_the_core_readers()
    {
        // The core's weapon-script readers cannot name TFEncryption.Key (core
        // may not name TF content), so stock's GetEncryptionKey() seat is
        // filled from this mod's mods.yaml row. This fact is the seam that
        // keeps the published row TF2's own key: change the key here, and the
        // row must follow or the sealed .ctx stop opening.
        string yaml = File.ReadAllText(RepoLayout.Repo("game", "mods.yaml"));
        var m = Regex.Match(yaml, @"encryption_key:\s*(\S+)");
        Assert.True(m.Success, "no mod publishes an encryption_key row");
        Assert.Equal(TFEncryption.Key, m.Groups[1].Value);
    }

    [Fact]
    public void TFWeaponNames_is_g_aWeaponNames_entry_for_entry()
    {
        var stock = StringTable(ValveSdk.Read(SharedDefsCpp), "g_aWeaponNames");
        Assert.Equal((int)TFWeaponId.Count, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal(stock[i], TFWeaponNames.Name((TFWeaponId)i));
    }

    [Fact]
    public void TFAmmoNames_is_g_aAmmoNames_entry_for_entry()
    {
        var stock = StringTable(ValveSdk.Read(SharedDefsCpp), "g_aAmmoNames");
        Assert.Equal((int)TFAmmo.Count, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal(stock[i], TFAmmoNames.Name((TFAmmo)i));
    }

    [Fact]
    public void TFObjectNames_is_g_ObjectInfos_names_entry_for_entry()
    {
        string cpp = ValveSdk.Read(SharedDefsCpp);
        int at = cpp.IndexOf("CObjectInfo g_ObjectInfos[OBJ_LAST] =", StringComparison.Ordinal);
        Assert.True(at >= 0, "no g_ObjectInfos table");
        string body = cpp[at..cpp.IndexOf("};", at, StringComparison.Ordinal)];
        var stock = Regex.Matches(body, @"CObjectInfo\(\s*""([^""]*)""\s*\)").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal((int)TFObjectType.Last, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal(stock[i], TFObjectNames.Name((TFObjectType)i));
    }

    [Fact]
    public void TFClassNames_Raw_is_g_aRawPlayerClassNames_entry_for_entry()
    {
        var stock = StringTable(ValveSdk.Read(SharedDefsCpp), "g_aRawPlayerClassNames");
        Assert.Equal(Enum.GetValues<TFClass>().Length, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal(stock[i], TFClassNames.Raw((TFClass)i));
    }

    [Fact]
    public void TFClassNames_NonLocalized_is_g_aPlayerClassNames_NonLocalized_entry_for_entry()
    {
        var stock = StringTable(ValveSdk.Read(SharedDefsCpp), "g_aPlayerClassNames_NonLocalized");
        Assert.Equal(Enum.GetValues<TFClass>().Length, stock.Count);
        for (int i = 0; i < stock.Count; i++)
            Assert.Equal(stock[i], TFClassNames.NonLocalized((TFClass)i));
    }

    [Fact]
    public void GetWeaponId_matches_without_regard_to_case()
        => Assert.Equal(TFWeaponId.Scattergun, TFWeaponNames.IdOf("tf_weapon_scattergun"));

    [Fact]
    public void GetWeaponId_answers_None_for_an_unknown_or_empty_name()
    {
        Assert.Equal(TFWeaponId.None, TFWeaponNames.IdOf("TF_WEAPON_NOT_A_WEAPON"));
        Assert.Equal(TFWeaponId.None, TFWeaponNames.IdOf(""));
        Assert.Equal(TFWeaponId.None, TFWeaponNames.IdOf(null));
    }

    [Fact]
    public void GetWeaponId_keeps_stocks_FLAME_BALL_misspelling()
    {
        Assert.Equal(TFWeaponId.FlameBall, TFWeaponNames.IdOf("TF_WEPON_FLAME_BALL"));
        Assert.Equal(TFWeaponId.None, TFWeaponNames.IdOf("TF_WEAPON_FLAME_BALL"));
    }

    [Fact]
    public void GetBuildableId_answers_Last_for_an_unknown_name()
    {
        Assert.Equal(TFObjectType.Sentrygun, TFObjectNames.IdOf("obj_sentrygun"));
        Assert.Equal(TFObjectType.Last, TFObjectNames.IdOf("OBJ_NOPE"));
    }

    [Fact]
    public void A_missing_header_fails_the_oracle_rather_than_passing_it()
    {
        var e = Assert.Throws<FileNotFoundException>(() => ValveSdk.Read("src/game/shared/tf/no_such_header.h"));
        Assert.Contains("no_such_header.h", e.Message);
    }

    [Fact]
    public void A_header_without_the_enum_fails_the_parser_rather_than_yielding_nothing()
    {
        // Positive arm first: the same parser finds the real enum.
        Assert.NotEmpty(Enumerators(ValveSdk.Read(SharedDefsH), "enum ETFCond"));
        Assert.Throws<InvalidDataException>(() => Enumerators("// no enums here\n", "enum ETFCond"));
    }

    private static void AssertMirrors<TEnum>(IReadOnlyList<(string Name, int Value)> stock, string prefix, int expected)
        where TEnum : struct, Enum
    {
        Assert.Equal(expected, stock.Count);
        Assert.Equal(stock.Count, Enum.GetValues<TEnum>().Length);

        foreach (var (stockName, value) in stock)
        {
            string member = Renames(stockName) ?? Pascal(Strip(stockName, prefix));
            Assert.True(Enum.TryParse<TEnum>(member, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed),
                        $"{stockName} has no {typeof(TEnum).Name}.{member}");
            Assert.True(Convert.ToInt32(parsed, CultureInfo.InvariantCulture) == value,
                        $"{typeof(TEnum).Name}.{member} is {Convert.ToInt32(parsed, CultureInfo.InvariantCulture)}, stock {stockName} is {value}");
        }
    }

    private static string Strip(string name, string prefix)
    {
        Assert.True(name.StartsWith(prefix, StringComparison.Ordinal), $"{name} lacks the prefix {prefix}");
        return name[prefix.Length..];
    }

    private static string Pascal(string upperSnake)
        => string.Concat(upperSnake.Split('_').Select(w => w[..1] + w[1..].ToLowerInvariant()));

    private static string Define(string text, string name)
    {
        var m = Regex.Match(text, $@"^#define\s+{name}\s+(\S+)", RegexOptions.Multiline);
        if (!m.Success)
            throw new InvalidDataException($"no #define {name}");
        return m.Groups[1].Value;
    }

    private static int IntDefine(string text, string name) => int.Parse(Define(text, name), CultureInfo.InvariantCulture);

    /// <summary>
    /// The enumerators of the first enum body after <paramref name="anchor"/>,
    /// in order, with C's implicit numbering. A value is a literal or
    /// <c>SYMBOL+N</c> over <paramref name="symbols"/>; anything else throws.
    /// </summary>
    private static IReadOnlyList<(string Name, int Value)> Enumerators(
        string text, string anchor, IReadOnlyDictionary<string, int>? symbols = null)
    {
        int at = text.IndexOf(anchor, StringComparison.Ordinal);
        // An "enum X" anchor opens the body after it; an enumerator anchor (an
        // anonymous enum) sits inside the body that opened before it.
        int open = at < 0 ? -1
            : anchor.StartsWith("enum ", StringComparison.Ordinal) ? text.IndexOf('{', at)
            : text.LastIndexOf('{', at);
        if (open < 0)
            throw new InvalidDataException($"no '{anchor}' enum in the header");
        int close = text.IndexOf('}', open);

        string body = Regex.Replace(text[(open + 1)..close], @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        var result = new List<(string, int)>();
        int next = 0;
        foreach (string raw in body.Split(','))
        {
            string item = raw.Trim();
            if (item.Length == 0)
                continue;

            string name = item;
            int value = next;
            int eq = item.IndexOf('=');
            if (eq >= 0)
            {
                name = item[..eq].Trim();
                value = Evaluate(item[(eq + 1)..].Trim(), symbols);
            }

            result.Add((name, value));
            next = value + 1;
        }

        if (result.Count == 0)
            throw new InvalidDataException($"the '{anchor}' enum is empty");
        return result;
    }

    private static int Evaluate(string expression, IReadOnlyDictionary<string, int>? symbols)
    {
        if (int.TryParse(expression, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int literal))
            return literal;

        var m = Regex.Match(expression, @"^(\w+)\s*\+\s*(\d+)$");
        if (m.Success && symbols is not null && symbols.TryGetValue(m.Groups[1].Value, out int baseValue))
            return baseValue + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

        throw new InvalidDataException($"cannot evaluate enumerator value '{expression}'");
    }

    /// <summary>The string literals of <c>NAME[...] = { ... };</c>, in order; a missing table throws.</summary>
    private static IReadOnlyList<string> StringTable(string text, string name)
    {
        var m = Regex.Match(text, $@"\b{name}\s*\[[^\]]*\]\s*=\s*\{{");
        if (!m.Success)
            throw new InvalidDataException($"no {name}[] table");
        int close = text.IndexOf("};", m.Index, StringComparison.Ordinal);
        string body = Regex.Replace(text[(m.Index + m.Length)..close], @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Matches(body, @"""([^""]*)""").Select(s => s.Groups[1].Value).ToList();
    }
}
