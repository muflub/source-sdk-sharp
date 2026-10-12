using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The MANAGED half of the weapon script database's gate.
///
/// <para>
/// <c>sourcesharp/test/weapon_script_oracle_test.cpp</c> runs the REAL
/// <c>FileWeaponInfo_t::Parse</c> over the REAL
/// <c>game/mod_sharp/scripts/weapon_*.txt</c> and asserts it produces
/// <c>sourcesharp/test/weapon_script_cases.txt</c>. This file asserts the
/// managed database produces the same table from the same files. Neither side
/// can drift without one of them going red, and the two suites need no
/// ordering between them.
/// </para>
///
/// <para>
/// The golden is the ONLY thing tying them together, which is why the first
/// fact here checks it is present and substantial: every comparison below
/// would pass against an empty table.
/// </para>
/// </summary>
public class WeaponScriptGoldenTests
{
    /// <summary>The thirteen scripts this mod ships — the same list, in the same
    /// order, as the oracle's <c>kWeapons</c>.</summary>
    public static readonly string[] Weapons =
    {
        "weapon_357",
        "weapon_ar2",
        "weapon_crossbow",
        "weapon_crowbar",
        "weapon_cubemap",
        "weapon_frag",
        "weapon_physcannon",
        "weapon_pistol",
        "weapon_rpg",
        "weapon_shotgun",
        "weapon_slam",
        "weapon_smg1",
        "weapon_stunstick",
    };

    private static string Root => RepoLayout.SdkTree;

    private static string GoldenPath =>
        Path.Combine(Root, "src", "sourcesharp", "test", "weapon_script_cases.txt");

    private static string ScriptPath(string weapon) =>
        Path.Combine(Root, "game", "mod_sharp", "scripts", weapon + ".txt");

    /// <summary>weapon -> field -> value, as the golden carries it.</summary>
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> GoldenTable =
        new(() =>
        {
            var table = new Dictionary<string, Dictionary<string, string>>();

            foreach (string line in File.ReadAllLines(GoldenPath))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] parts = line.Split('\t');

                if (parts.Length != 3)
                    continue;

                if (!table.TryGetValue(parts[0], out var fields))
                    table[parts[0]] = fields = new Dictionary<string, string>();

                fields[parts[1]] = parts[2];
            }

            return table;
        });

    /// <summary>The managed parse of every shipped script, done once.</summary>
    private static readonly Lazy<Dictionary<string, WeaponScript>> Parsed =
        new(() =>
        {
            var parsed = new Dictionary<string, WeaponScript>();

            foreach (string weapon in Weapons)
            {
                var root = KeyValuesText.Parse(File.ReadAllText(ScriptPath(weapon))).Children[0];

                // useConsoleBuckets: false -- the oracle's C++ TU takes the
                // IsX360() arm, which is a compile-time false on linux64, and
                // the client arm (hud_fastswitch == 2) is false too because
                // SourceSharp.Client/ConVars.cs declares that convar defaulting to "1". The
                // bucket_360 keys get their own fact below instead.
                parsed[weapon] = WeaponScriptParser.Parse(root, weapon, useConsoleBuckets: false);
            }

            return parsed;
        });

    private static string Golden(string weapon, string field) =>
        GoldenTable.Value.TryGetValue(weapon, out var fields) &&
        fields.TryGetValue(field, out string? value)
            ? value
            : "";

    private static string B(bool v) => v ? "true" : "false";
    private static string I(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Compare one field across every weapon.</summary>
    private static void FieldAgrees(string field, Func<WeaponScript, string> managed)
    {
        foreach (string weapon in Weapons)
        {
            Assert.True(
                Golden(weapon, field) == managed(Parsed.Value[weapon]),
                $"{weapon}.{field}: the golden says '{Golden(weapon, field)}' and the " +
                $"managed parse says '{managed(Parsed.Value[weapon])}'. The golden is " +
                "what the REAL FileWeaponInfo_t::Parse answered -- so this is the " +
                "managed port disagreeing with the C++ it replaces.");
        }
    }

    //-------------------------------------------------------------------------
    // THE ANCHORS. Without these, every comparison below passes against an
    // empty table -- the check that cannot fail.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheGoldenTableIsPresentAndCoversEveryShippedScript()
    {
        Assert.True(File.Exists(GoldenPath), $"the golden table is missing: {GoldenPath}");
        Assert.Equal(Weapons.Length, GoldenTable.Value.Count);

        foreach (string weapon in Weapons)
            Assert.True(GoldenTable.Value.ContainsKey(weapon), $"{weapon} is not in the golden");
    }

    [Fact]
    public void TheGoldenTableIsSubstantialRatherThanJustPresent()
    {
        // A table holding one field per weapon would satisfy the count above
        // and check almost nothing.
        foreach (string weapon in Weapons)
        {
            Assert.True(GoldenTable.Value[weapon].Count >= 21,
                        $"{weapon} has {GoldenTable.Value[weapon].Count} golden fields; " +
                        "the parse always produces at least 21 non-sound ones, so some " +
                        "were dropped when the table was written.");
        }
    }

    [Fact]
    public void EveryShippedScriptActuallyParsed()
    {
        Assert.Equal(Weapons.Length, Parsed.Value.Count);

        foreach (string weapon in Weapons)
            Assert.Equal(weapon, Parsed.Value[weapon].ClassName);
    }

    //-------------------------------------------------------------------------
    // AGREEMENT WITH THE NATIVE PARSE, one behaviour per fact.
    //-------------------------------------------------------------------------

    [Fact]
    public void ClipSizesMatchTheNativeParse()
    {
        FieldAgrees("maxclip1", w => I(w.MaxClip1));
        FieldAgrees("maxclip2", w => I(w.MaxClip2));
    }

    [Fact]
    public void DefaultClipsMatchTheNativeParse()
    {
        FieldAgrees("defaultclip1", w => I(w.DefaultClip1));
        FieldAgrees("defaultclip2", w => I(w.DefaultClip2));
    }

    [Fact]
    public void BucketsMatchTheNativeParse()
    {
        FieldAgrees("slot", w => I(w.Slot));
        FieldAgrees("position", w => I(w.Position));
    }

    [Fact]
    public void SoundsMatchTheNativeParse()
    {
        // Keyed by the LITERAL category names, not by walking
        // WeaponScriptSounds.Categories: a key derived from the array under
        // test renames itself on both sides at once, which is the gap a
        // mutation found in the native oracle.
        string[] literal =
        {
            "empty", "single_shot", "single_shot_npc", "double_shot",
            "double_shot_npc", "burst", "reload", "reload_npc",
            "melee_miss", "melee_hit", "melee_hit_world",
            "special1", "special2", "special3", "taunt", "deploy",
        };

        for (int i = 0; i < literal.Length; i++)
        {
            int index = i;
            FieldAgrees("sound." + literal[i], w => w.Sounds[index]);
        }
    }

    [Fact]
    public void AmmoNamesMatchTheNativeParse()
    {
        FieldAgrees("ammo1", w => w.Ammo1);
        FieldAgrees("ammo2", w => w.Ammo2);
    }

    [Fact]
    public void FlagsAndWeightMatchTheNativeParse()
    {
        FieldAgrees("flags", w => I((int)w.Flags));
        FieldAgrees("weight", w => I(w.Weight));
        FieldAgrees("rumble", w => I(w.RumbleEffect));
    }

    [Fact]
    public void ModelsAndNamesMatchTheNativeParse()
    {
        FieldAgrees("classname", w => w.ClassName);
        FieldAgrees("printname", w => w.PrintName);
        FieldAgrees("viewmodel", w => w.ViewModel);
        FieldAgrees("worldmodel", w => w.WorldModel);
        FieldAgrees("animprefix", w => w.AnimationPrefix);
    }

    [Fact]
    public void SwitchingAndHandednessMatchTheNativeParse()
    {
        FieldAgrees("autoswitchto", w => B(w.AutoSwitchTo));
        FieldAgrees("autoswitchfrom", w => B(w.AutoSwitchFrom));
        FieldAgrees("melee", w => B(w.MeleeWeapon));
        FieldAgrees("builtrighthanded", w => B(w.BuiltRightHanded));
        FieldAgrees("allowflipping", w => B(w.AllowFlipping));
        FieldAgrees("showusagehint", w => B(w.ShowUsageHint));
    }

    [Fact]
    public void TheSoundCategoryNamesAndTheirOrderMatchTheScriptKeys()
    {
        // The managed twin of the oracle's TheSoundCategoryNamesAreLiteralAndInOrder.
        // WeaponScriptSound's VALUES are indices into this list, so a
        // renumbering here silently re-points every weapon's sounds.
        string[] expected =
        {
            "empty", "single_shot", "single_shot_npc", "double_shot",
            "double_shot_npc", "burst", "reload", "reload_npc",
            "melee_miss", "melee_hit", "melee_hit_world",
            "special1", "special2", "special3", "taunt", "deploy",
        };

        Assert.Equal(WeaponScriptSounds.Count, expected.Length);
        Assert.Equal(expected, WeaponScriptSounds.Categories);

        Assert.Equal(0, (int)WeaponScriptSound.Empty);
        Assert.Equal(1, (int)WeaponScriptSound.Single);
        Assert.Equal(10, (int)WeaponScriptSound.MeleeHitWorld);
        Assert.Equal(15, (int)WeaponScriptSound.Deploy);
    }
}

/// <summary>
/// The behaviours the shipped corpus does NOT exercise.
///
/// <para>
/// A golden taken from thirteen files pins only what those thirteen files
/// contain. Mutating the <c>clip_size</c> default in <c>weapon_parse.cpp</c>
/// from <c>WEAPON_NOCLIP</c> to <c>4242</c> left the whole native oracle green,
/// because every shipped script carries a <c>clip_size</c> key and the default
/// is never reached. These facts cover the gaps with synthetic scripts.
/// </para>
/// </summary>
public class WeaponScriptDefaultsTests
{
    private static WeaponScript Parse(string body, bool consoleBuckets = false)
    {
        var root = KeyValuesText.Parse("WeaponData\n{\n" + body + "\n}\n").Children[0];
        return WeaponScriptParser.Parse(root, "weapon_test", consoleBuckets);
    }

    [Fact]
    public void AnAbsentClipSizeIsNoClip()
    {
        Assert.Equal(WeaponScript.NoClip, Parse("").MaxClip1);
    }

    [Fact]
    public void AnAbsentSecondaryClipSizeIsNoClip()
    {
        Assert.Equal(WeaponScript.NoClip, Parse("").MaxClip2);
    }

    [Fact]
    public void AnAbsentDefaultClipFollowsTheClipSize()
    {
        Assert.Equal(7, Parse("\"clip_size\" \"7\"").DefaultClip1);
    }

    [Fact]
    public void ADefaultClipMayExceedTheClipSizeAndIsNotClamped()
    {
        // weapon_ar2 really does this: clip_size 30, default_clip 60.
        var w = Parse("\"clip_size\" \"30\"\n\"default_clip\" \"60\"");

        Assert.Equal(30, w.MaxClip1);
        Assert.Equal(60, w.DefaultClip1);
    }

    [Fact]
    public void AnAbsentItemFlagsIsLimitInWorld()
    {
        // Stock's own comment calls this the "LAME old way"; the default is
        // ITEM_FLAG_LIMITINWORLD and NOT zero.
        Assert.Equal(WeaponItemFlags.LimitInWorld, Parse("").Flags);
    }

    [Fact]
    public void ANamedItemFlagSetToOneSetsItsBit()
    {
        var w = Parse("\"item_flags\" \"0\"\n\"ITEM_FLAG_EXHAUSTIBLE\" \"1\"");

        Assert.Equal(WeaponItemFlags.Exhaustible, w.Flags);
    }

    [Fact]
    public void ANamedItemFlagSetToZeroClearsItsBit()
    {
        // item_flags asks for LIMITINWORLD, the named key takes it away again.
        var w = Parse("\"item_flags\" \"8\"\n\"ITEM_FLAG_LIMITINWORLD\" \"0\"");

        Assert.Equal(WeaponItemFlags.None, w.Flags);
    }

    [Fact]
    public void ANamedItemFlagWithAnyOtherValueLeavesTheBitAlone()
    {
        // Stock acts on 0 and 1 ONLY: its sentinel for "absent" is -1, and
        // every other number falls through both branches untouched.
        var w = Parse("\"item_flags\" \"8\"\n\"ITEM_FLAG_LIMITINWORLD\" \"2\"");

        Assert.Equal(WeaponItemFlags.LimitInWorld, w.Flags);
    }

    [Fact]
    public void AnAbsentPrintNameIsTheMissingSentinel()
    {
        Assert.Equal(WeaponScript.PrintNameMissing, Parse("").PrintName);
    }

    [Fact]
    public void AnAbsentRumbleEffectIsMinusOne()
    {
        Assert.Equal(-1, Parse("").RumbleEffect);
    }

    [Fact]
    public void AutoSwitchDefaultsToTrueOnBothSides()
    {
        var w = Parse("");

        Assert.True(w.AutoSwitchTo);
        Assert.True(w.AutoSwitchFrom);
    }

    [Fact]
    public void HandednessAndFlippingDefaultToTrue()
    {
        var w = Parse("");

        Assert.True(w.BuiltRightHanded);
        Assert.True(w.AllowFlipping);
    }

    [Fact]
    public void MeleeAndUsageHintDefaultToFalse()
    {
        var w = Parse("");

        Assert.False(w.MeleeWeapon);
        Assert.False(w.ShowUsageHint);
    }

    [Fact]
    public void AnAbsentAmmoKeyIsTheEmptyString()
    {
        // The default is the literal "None", which then compares equal to the
        // sentinel -- so absent and explicitly "None" answer the same thing.
        Assert.Equal("", Parse("").Ammo1);
        Assert.Equal("", Parse("\"primary_ammo\" \"None\"").Ammo1);
    }

    [Fact]
    public void TheNoneAmmoSentinelIsCaseSensitive()
    {
        // Stock compares with strcmp, not stricmp. A script saying "none" gets
        // an ammo type literally named none. Reproduced rather than fixed.
        Assert.Equal("none", Parse("\"primary_ammo\" \"none\"").Ammo1);
    }

    [Fact]
    public void ConsoleBucketsAreIgnoredUnlessAsked()
    {
        var w = Parse("\"bucket\" \"1\"\n\"bucket_360\" \"5\"", consoleBuckets: false);

        Assert.Equal(1, w.Slot);
    }

    [Fact]
    public void ConsoleBucketsOverrideWhenAsked()
    {
        var w = Parse("\"bucket\" \"1\"\n\"bucket_360\" \"5\"", consoleBuckets: true);

        Assert.Equal(5, w.Slot);
    }

    [Fact]
    public void AConsoleBucketPositionFallsBackToTheParsedPositionRatherThanZero()
    {
        // Stock reads the 360 keys THROUGH the values it already has, so a
        // script with bucket_360 and no bucket_position_360 keeps its position.
        var w = Parse("\"bucket\" \"1\"\n\"bucket_position\" \"3\"\n\"bucket_360\" \"5\"",
                      consoleBuckets: true);

        Assert.Equal(5, w.Slot);
        Assert.Equal(3, w.Position);
    }

    [Fact]
    public void AStringLongerThanItsBufferIsTruncated()
    {
        // Q_strncpy keeps at most N-1 characters. MAX_WEAPON_STRING is 80.
        string tooLong = new string('x', 200);
        var w = Parse($"\"printname\" \"{tooLong}\"");

        Assert.Equal(WeaponScript.MaxStringBuffer - 1, w.PrintName.Length);
    }

    [Fact]
    public void AnAnimationPrefixUsesTheSmallerPrefixBuffer()
    {
        // MAX_WEAPON_PREFIX is 16, not 80 -- a different buffer from the rest.
        string tooLong = new string('y', 200);
        var w = Parse($"\"anim_prefix\" \"{tooLong}\"");

        Assert.Equal(WeaponScript.MaxPrefixBuffer - 1, w.AnimationPrefix.Length);
    }

    [Fact]
    public void AnAmmoNameUsesTheAmmoNameBuffer()
    {
        // MAX_WEAPON_AMMO_NAME is 32.
        string tooLong = new string('z', 200);
        var w = Parse($"\"primary_ammo\" \"{tooLong}\"");

        Assert.Equal(WeaponScript.MaxAmmoNameBuffer - 1, w.Ammo1.Length);
    }

    [Fact]
    public void AnAbsentSoundDataBlockLeavesEverySoundEmpty()
    {
        var w = Parse("");

        Assert.Equal(WeaponScriptSounds.Count, w.Sounds.Count);
        Assert.All(w.Sounds, s => Assert.Equal("", s));
    }

    [Fact]
    public void AnEmptySoundValueIsNotStored()
    {
        // Stock tests `soundname && soundname[0]` before copying, so a category
        // written as "" stays empty rather than becoming an empty sound name.
        var w = Parse("SoundData\n{\n\"reload\" \"\"\n}");

        Assert.Equal("", w.Sound(WeaponScriptSound.Reload));
    }

    [Fact]
    public void ASoundLandsInTheSlotItsCategoryNames()
    {
        var w = Parse("SoundData\n{\n\"melee_hit_world\" \"Weapon_Crowbar.Melee_HitWorld\"\n}");

        Assert.Equal("Weapon_Crowbar.Melee_HitWorld", w.Sound(WeaponScriptSound.MeleeHitWorld));
        Assert.Equal("", w.Sound(WeaponScriptSound.MeleeHit));
    }
}

/// <summary>
/// <c>GetWeaponSoundFromString</c> — stock's category lookup.
/// </summary>
public class WeaponScriptSoundLookupTests
{
    [Fact]
    public void AKnownCategoryAnswersItsIndex()
    {
        Assert.Equal((int)WeaponScriptSound.Reload, WeaponScriptSounds.FromString("reload"));
    }

    [Fact]
    public void TheLookupIsCaseInsensitiveAsStocksStricmpIs()
    {
        Assert.Equal((int)WeaponScriptSound.Burst, WeaponScriptSounds.FromString("BURST"));
    }

    [Fact]
    public void AnUnknownCategoryAnswersMinusOne()
    {
        Assert.Equal(-1, WeaponScriptSounds.FromString("no_such_category"));
    }

    [Fact]
    public void ANullNameAnswersMinusOneRatherThanThrowing()
    {
        Assert.Equal(-1, WeaponScriptSounds.FromString(null));
    }
}

/// <summary>
/// The database itself — handles, lookups and the manifest walk.
/// </summary>
public class WeaponScriptDatabaseTests
{

    private const string Pistol =
        "WeaponData\n{\n\"clip_size\" \"18\"\n\"bucket\" \"1\"\n\"primary_ammo\" \"Pistol\"\n}\n";

    private const string Ar2 =
        "WeaponData\n{\n\"clip_size\" \"30\"\n\"bucket\" \"3\"\n\"primary_ammo\" \"AR2\"\n}\n";

    /// <summary>
    /// A database over a fixed set of files. The reader is a DELEGATE because
    /// the database is engine-free — see WeaponScriptDatabase._read for why it
    /// cannot take <c>IEngineFiles</c>.
    /// </summary>
    private static WeaponScriptDatabase WithFiles(params (string Path, string Text)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Text);

        return new WeaponScriptDatabase(new RecordingLog(), path => map.TryGetValue(path, out string? t) ? t : null);
    }

    [Fact]
    public void ADatabaseWithNoReaderIsEmptyRatherThanBroken()
    {
        // THE SIDE AUDIT PUT THIS FACT HERE.
        // IEngineFiles.ReadGameFile is backed by FilesReadGameFile and
        // FilesGameFileSize, both filled only by game/server/shell_files.cpp.
        // SourceSharp.Shared may lean only on slots BOTH sides fill, so this
        // database cannot read its own files; the reader is injected, and the
        // CLIENT has none to inject today.
        var db = new WeaponScriptDatabase(new RecordingLog());

        Assert.False(db.HasReader);
        Assert.Equal(0, db.PrecacheFromManifest());
        Assert.Null(db.ReadScript("weapon_pistol"));
        Assert.Equal(0, db.Count);

        // And it still answers the invalid handle correctly rather than throwing.
        Assert.Equal(WeaponScriptDatabase.InvalidHandle, db.LookupSlot("weapon_pistol"));
        Assert.Same(WeaponScriptDatabase.Null, db.FromHandle(0));
    }

    [Fact]
    public void ADatabaseWithAReaderSaysSo()
    {
        Assert.True(WithFiles(("scripts/weapon_pistol.txt", Pistol)).HasReader);
    }

    [Fact]
    public void AHandleRoundTripsBackToItsScript()
    {
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));
        db.ReadScript("weapon_pistol");

        int handle = db.LookupSlot("weapon_pistol");

        Assert.Equal(18, db.FromHandle(handle).MaxClip1);
    }

    [Fact]
    public void TheFirstScriptsHandleIsZeroAndZeroIsAValidHandle()
    {
        // `default-handle-is-entry-zero`: two lanes have already measured the
        // wrong path by treating a default-constructed handle as an absence.
        // Entry zero is a REAL weapon; the sentinel for "not found" is 0xFFFF.
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));
        db.ReadScript("weapon_pistol");

        Assert.Equal(0, db.LookupSlot("weapon_pistol"));
        Assert.NotEqual(WeaponScriptDatabase.InvalidHandle, db.LookupSlot("weapon_pistol"));
        Assert.Equal(18, db.FromHandle(0).MaxClip1);
    }

    [Fact]
    public void AnUnknownNameAnswersTheInvalidHandle()
    {
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));
        db.ReadScript("weapon_pistol");

        Assert.Equal(WeaponScriptDatabase.InvalidHandle, db.LookupSlot("weapon_nothing"));
    }

    [Fact]
    public void TheInvalidHandleIsCUtlDictsAndNotZeroOrMinusOne()
    {
        Assert.Equal(0xFFFF, WeaponScriptDatabase.InvalidHandle);
    }

    [Fact]
    public void AnInvalidHandleAnswersTheNullScriptRatherThanNull()
    {
        var db = WithFiles();

        Assert.Same(WeaponScriptDatabase.Null, db.FromHandle(WeaponScriptDatabase.InvalidHandle));
        Assert.Same(WeaponScriptDatabase.Null, db.FromHandle(-1));
        Assert.Same(WeaponScriptDatabase.Null, db.FromHandle(0));
    }

    [Fact]
    public void TheNullScriptCarriesTheConstructorDefaultsNotTheParsersDefaults()
    {
        // FileWeaponInfo_t's CONSTRUCTOR zeroes the clip sizes and sets
        // bAutoSwitchTo false, where a PARSED script defaults them to
        // WEAPON_NOCLIP and true. Two different sets of defaults in one class,
        // and a port that used the parser's for both would answer -1 where
        // stock answers 0.
        var n = WeaponScriptDatabase.Null;

        Assert.Equal(0, n.MaxClip1);
        Assert.Equal(0, n.MaxClip2);
        Assert.False(n.AutoSwitchTo);
        Assert.False(n.AutoSwitchFrom);
        Assert.Equal(WeaponItemFlags.None, n.Flags);

        // ...but the two the constructor DOES set true are still true.
        Assert.True(n.BuiltRightHanded);
        Assert.True(n.AllowFlipping);
        Assert.Equal(-1, n.RumbleEffect);
    }

    [Fact]
    public void NameLookupIsCaseInsensitiveAsCUtlDictsDefaultIs()
    {
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));
        db.ReadScript("weapon_pistol");

        Assert.Equal(0, db.LookupSlot("WEAPON_PISTOL"));
    }

    [Fact]
    public void ReadingTheSameScriptTwiceReusesTheSlot()
    {
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));

        var first = db.ReadScript("weapon_pistol");
        var second = db.ReadScript("weapon_pistol");

        Assert.Same(first, second);
        Assert.Equal(1, db.Count);
    }

    [Fact]
    public void AMissingScriptFileAnswersNull()
    {
        var db = WithFiles();

        Assert.Null(db.ReadScript("weapon_pistol"));
        Assert.Equal(0, db.Count);
    }

    [Fact]
    public void PrecacheWalksTheManifestAndReadsEveryFileItNames()
    {
        var db = WithFiles(
            (WeaponScriptDatabase.ManifestPath,
             "weaponscripts\n{\n\"file\" \"scripts/weapon_pistol.txt\"\n" +
             "\"file\" \"scripts/weapon_ar2.txt\"\n}\n"),
            ("scripts/weapon_pistol.txt", Pistol),
            ("scripts/weapon_ar2.txt", Ar2));

        Assert.Equal(2, db.PrecacheFromManifest());
        Assert.Equal(2, db.Count);
        Assert.Equal(18, db.FromHandle(db.LookupSlot("weapon_pistol")).MaxClip1);
        Assert.Equal(30, db.FromHandle(db.LookupSlot("weapon_ar2")).MaxClip1);
    }

    [Fact]
    public void PrecacheIsIdempotent()
    {
        var db = WithFiles(
            (WeaponScriptDatabase.ManifestPath,
             "weaponscripts\n{\n\"file\" \"scripts/weapon_pistol.txt\"\n}\n"),
            ("scripts/weapon_pistol.txt", Pistol));

        Assert.Equal(1, db.PrecacheFromManifest());

        // Stock returns immediately when the database already holds anything,
        // so a level init may call it blind.
        Assert.Equal(0, db.PrecacheFromManifest());
        Assert.Equal(1, db.Count);
    }

    [Fact]
    public void AnAbsentManifestReadsAsNoScriptsRatherThanThrowing()
    {
        // This is the state of THIS mod: there is no scripts/weapon_manifest.txt
        // on any search path, so stock's precache loaded nothing either.
        var db = WithFiles(("scripts/weapon_pistol.txt", Pistol));

        Assert.Equal(0, db.PrecacheFromManifest());
        Assert.Equal(0, db.Count);
    }

    [Fact]
    public void AManifestEntryThatIsNotFileIsSkippedRatherThanFatal()
    {
        // Stock calls Error(), which exits the process. A stated divergence:
        // losing the game over a typo in a data file is not worth porting.
        var db = WithFiles(
            (WeaponScriptDatabase.ManifestPath,
             "weaponscripts\n{\n\"notfile\" \"scripts/weapon_ar2.txt\"\n" +
             "\"file\" \"scripts/weapon_pistol.txt\"\n}\n"),
            ("scripts/weapon_pistol.txt", Pistol),
            ("scripts/weapon_ar2.txt", Ar2));

        Assert.Equal(1, db.PrecacheFromManifest());
        Assert.Equal(WeaponScriptDatabase.InvalidHandle, db.LookupSlot("weapon_ar2"));
    }

    [Fact]
    public void AMalformedScriptDoesNotEnterTheDatabase()
    {
        var db = WithFiles(("scripts/weapon_pistol.txt", "WeaponData\n{\n\"clip_size\""));

        Assert.Null(db.ReadScript("weapon_pistol"));
        Assert.Equal(0, db.Count);
    }

    [Theory]
    [InlineData("scripts/weapon_pistol.txt", "weapon_pistol")]
    [InlineData("weapon_pistol.txt", "weapon_pistol")]
    [InlineData("weapon_pistol", "weapon_pistol")]
    [InlineData("a/b/c/weapon_x.y.txt", "weapon_x.y")]
    [InlineData("", "")]
    public void FileBaseStripsTheDirectoryAndTheExtension(string path, string expected)
    {
        Assert.Equal(expected, WeaponScriptDatabase.FileBase(path));
    }

    [Fact]
    public void TheConsoleBucketFlagReachesTheParseThroughTheDatabase()
    {
        // This is the property that replaces weapon_parse.cpp's
        // `extern ConVar hud_fastswitch` -- the only referencer of that symbol
        // on the client link line.
        var db = WithFiles(("scripts/weapon_x.txt",
                            "WeaponData\n{\n\"bucket\" \"1\"\n\"bucket_360\" \"5\"\n}\n"));
        db.UseConsoleBuckets = true;

        Assert.Equal(5, db.ReadScript("weapon_x")!.Slot);
    }
}

/// <summary>
/// Stock's <c>KeyValues</c> accessor semantics, which are a type detection
/// followed by a conversion and not "read the text".
/// </summary>
public class WeaponScriptKeyValuesTests
{
    private static WeaponScript Parse(string body) =>
        WeaponScriptParser.Parse(
            KeyValuesText.Parse("WeaponData\n{\n" + body + "\n}\n").Children[0], "weapon_test");

    [Fact]
    public void AnIntegerValuedKeyReadsBackAsThatInteger()
    {
        Assert.Equal(18, Parse("\"clip_size\" \"18\"").MaxClip1);
    }

    [Fact]
    public void ANegativeIntegerReadsBackNegative()
    {
        Assert.Equal(-1, Parse("\"clip_size\" \"-1\"").MaxClip1);
    }

    [Fact]
    public void AFloatValuedKeyTruncatesTowardZero()
    {
        // KeyValues types "8.9" as TYPE_FLOAT and GetInt returns (int)m_flValue,
        // which truncates rather than rounds.
        Assert.Equal(8, Parse("\"clip_size\" \"8.9\"").MaxClip1);
        Assert.Equal(-8, Parse("\"clip_size\" \"-8.9\"").MaxClip1);
    }

    [Fact]
    public void ANonNumericKeyReadsAsZeroThroughAtoi()
    {
        // TYPE_STRING goes through atoi, which answers 0 when there are no
        // leading digits -- NOT the default the caller passed.
        Assert.Equal(0, Parse("\"clip_size\" \"abc\"").MaxClip1);
    }

    [Fact]
    public void ANumericPrefixReadsAsItsPrefixThroughAtoi()
    {
        // "12abc" is TYPE_STRING because it does not parse wholly as a number,
        // and atoi then reads the 12. int.TryParse would have refused it.
        Assert.Equal(12, Parse("\"clip_size\" \"12abc\"").MaxClip1);
    }

    [Fact]
    public void AnEmptyValueIsAStringAndReadsAsZero()
    {
        Assert.Equal(0, Parse("\"clip_size\" \"\"").MaxClip1);
    }

    [Fact]
    public void AnIntegerValuedStringKeyRendersBackThroughPercentD()
    {
        // GetString on a TYPE_INT re-renders with "%d", so leading zeroes go.
        Assert.Equal("7", Parse("\"anim_prefix\" \"007\"").AnimationPrefix);
    }

    [Fact]
    public void ATextValueIsReturnedAsWritten()
    {
        Assert.Equal("pistol", Parse("\"anim_prefix\" \"pistol\"").AnimationPrefix);
    }
}
