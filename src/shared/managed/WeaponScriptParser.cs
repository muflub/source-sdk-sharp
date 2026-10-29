//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// Reads one weapon script into a <see cref="WeaponScript"/> — the managed
/// <c>FileWeaponInfo_t::Parse</c>.
///
/// <para>
/// <b>This is a PORT, not a reinterpretation.</b> Every default, every
/// fallback and the order the named item flags are applied in are stock's, and
/// the tests pin each one against the real <c>scripts/weapon_*.txt</c> files
/// this mod ships. Where stock's behaviour is surprising it is reproduced and
/// commented rather than corrected, because a weapon script written for stock
/// has to keep meaning what it meant.
/// </para>
/// </summary>
public static class WeaponScriptParser
{
    /// <summary>
    /// Parses the <c>WeaponData</c> body of a script.
    /// </summary>
    /// <param name="data">
    /// The block whose children are the weapon's keys — stock is handed the
    /// root of the loaded file, whose own name is <c>WeaponDatafile</c> and
    /// whose keys are the ones read below.
    /// </param>
    /// <param name="weaponName">
    /// The name the database was asked for. Stock copies THIS into
    /// <c>szClassName</c> rather than reading a key, so a script whose file
    /// name and contents disagree is named by its file.
    /// </param>
    /// <param name="useConsoleBuckets">
    /// Stock's <c>hud_fastswitch.GetInt() == 2</c> on the client and
    /// <c>IsX360()</c> on the server: when true the <c>bucket_360</c> and
    /// <c>bucket_position_360</c> keys override the normal ones.
    ///
    /// <para>
    /// It is a PARAMETER rather than a convar read inside the parse because
    /// §6's ConVar row says no ConVar's semantics is declared natively and, by
    /// the same argument, a pure parse should not reach for global state. The
    /// caller supplies the policy; this decides nothing.
    /// </para>
    /// </param>
    public static WeaponScript Parse(KeyValuesText data, string weaponName, bool useConsoleBuckets = false)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (weaponName is null) throw new ArgumentNullException(nameof(weaponName));

        int slot = GetInt(data, "bucket", 0);
        int position = GetInt(data, "bucket_position", 0);

        // Stock reads the 360 keys THROUGH the values it already has, so a
        // script with bucket_360 but no bucket_position_360 keeps its normal
        // position rather than falling back to zero.
        if (useConsoleBuckets)
        {
            slot = GetInt(data, "bucket_360", slot);
            position = GetInt(data, "bucket_position_360", position);
        }

        int maxClip1 = GetInt(data, "clip_size", WeaponScript.NoClip);
        int maxClip2 = GetInt(data, "clip2_size", WeaponScript.NoClip);

        return new WeaponScript
        {
            // Q_strncpy truncation is reproduced on every field stock copies
            // into a fixed buffer. It is not a detail: a printname longer than
            // 79 characters IS cut short in stock, and a managed database that
            // answered the whole string would disagree with the C++ one on
            // exactly the inputs a test is least likely to try.
            ClassName = Truncate(weaponName, WeaponScript.MaxStringBuffer),
            PrintName = Truncate(GetString(data, "printname", WeaponScript.PrintNameMissing),
                                 WeaponScript.MaxStringBuffer),
            ViewModel = Truncate(GetString(data, "viewmodel"), WeaponScript.MaxStringBuffer),
            WorldModel = Truncate(GetString(data, "playermodel"), WeaponScript.MaxStringBuffer),
            AnimationPrefix = Truncate(GetString(data, "anim_prefix"), WeaponScript.MaxPrefixBuffer),

            Slot = slot,
            Position = position,

            MaxClip1 = maxClip1,
            MaxClip2 = maxClip2,

            // The DEFAULT clip defaults to the MAX clip, so a script that names
            // clip_size and not default_clip spawns the weapon full.
            DefaultClip1 = GetInt(data, "default_clip", maxClip1),
            DefaultClip2 = GetInt(data, "default_clip2", maxClip2),

            Weight = GetInt(data, "weight", 0),
            RumbleEffect = GetInt(data, "rumble", -1),

            Flags = ParseFlags(data),

            ShowUsageHint = GetInt(data, "showusagehint", 0) != 0,
            PlayerDamage = GetInt(data, "damage", 0),
            AutoSwitchTo = GetInt(data, "autoswitchto", 1) != 0,
            AutoSwitchFrom = GetInt(data, "autoswitchfrom", 1) != 0,
            BuiltRightHanded = GetInt(data, "BuiltRightHanded", 1) != 0,
            AllowFlipping = GetInt(data, "AllowFlipping", 1) != 0,
            MeleeWeapon = GetInt(data, "MeleeWeapon", 0) != 0,

            Ammo1 = ParseAmmo(data, "primary_ammo"),
            Ammo2 = ParseAmmo(data, "secondary_ammo"),

            Sounds = ParseSounds(data),
        };
    }

    /// <summary>
    /// <c>item_flags</c>, then every named flag the script mentions.
    ///
    /// <para>
    /// Stock's default when <c>item_flags</c> is absent is
    /// <c>ITEM_FLAG_LIMITINWORLD</c> and NOT zero, which its own comment calls
    /// the "LAME old way". A named key set to 0 CLEARS its bit and set to 1
    /// SETS it; any other value — including a key that is not a number —
    /// leaves the bit alone, because stock's sentinel for "absent" is -1 and
    /// only 0 and 1 are acted on.
    /// </para>
    /// </summary>
    private static WeaponItemFlags ParseFlags(KeyValuesText data)
    {
        var flags = (WeaponItemFlags)GetInt(data, "item_flags", (int)WeaponItemFlags.LimitInWorld);

        foreach (var (name, value) in WeaponItemFlagNames.All)
        {
            int set = GetInt(data, name, -1);

            if (set == 0)
                flags &= ~value;
            else if (set == 1)
                flags |= value;
        }

        return flags;
    }

    /// <summary>
    /// An ammo key, with stock's <c>"None"</c> convention.
    ///
    /// <para>
    /// The default when the key is ABSENT is the literal string <c>"None"</c>,
    /// which then compares equal to the sentinel and becomes empty — so an
    /// absent key and an explicit <c>"None"</c> answer the same thing. The
    /// comparison is stock's <c>strcmp</c> and so is case-SENSITIVE: a script
    /// saying <c>"none"</c> gets an ammo type literally named <c>none</c>.
    /// Reproduced rather than fixed.
    /// </para>
    /// </summary>
    private static string ParseAmmo(KeyValuesText data, string key)
    {
        string ammo = GetString(data, key, "None");

        return string.Equals(ammo, "None", StringComparison.Ordinal)
            ? ""
            : Truncate(ammo, WeaponScript.MaxAmmoNameBuffer);
    }

    /// <summary>
    /// The <c>SoundData</c> block. A missing block leaves every category empty,
    /// and a category whose value is the empty string is left empty too —
    /// stock tests <c>soundname &amp;&amp; soundname[0]</c> before copying.
    /// </summary>
    private static string[] ParseSounds(KeyValuesText data)
    {
        var sounds = WeaponScript.EmptySounds();
        var block = data.Find("SoundData");

        if (block is null)
            return sounds;

        for (int i = 0; i < WeaponScriptSounds.Count; i++)
        {
            string name = GetString(block, WeaponScriptSounds.Categories[i]);

            if (name.Length != 0)
                sounds[i] = Truncate(name, WeaponScript.MaxStringBuffer);
        }

        return sounds;
    }

    /// <summary>
    /// <c>Q_strncpy( dest, src, N )</c>: at most <c>N - 1</c> characters
    /// survive, because the last byte is the terminator.
    /// </summary>
    private static string Truncate(string value, int bufferSize)
    {
        int max = bufferSize - 1;

        return value.Length <= max ? value : value.Substring(0, max);
    }

    // ------------------------------------------------------------------
    // Stock KeyValues accessor semantics.
    //
    // These are not "read the text": KeyValues TYPES a value when it loads it
    // (KeyValues.cpp:2537-2571) and GetInt/GetString then CONVERT from that
    // type (:1290, :1400). The difference is visible — GetString on a value
    // written "8" returns "8" via %d, and on one written "8.5" returns
    // "8.500000" via %f — so a reader that just handed back the source text
    // would agree with stock on most weapon scripts and disagree on some.
    // ------------------------------------------------------------------

    /// <summary>How stock would type a value loaded from a text buffer.</summary>
    private enum KvType
    {
        String,
        Int,
        Float,
        UInt64,
    }

    /// <summary>
    /// Stock's type detection, in stock's order: empty is a string; an 18-char
    /// <c>0x</c>-prefixed token is a uint64; a token that parses WHOLLY as a
    /// float and not wholly as an int is a float; one that parses wholly as an
    /// int is an int; anything else is a string.
    /// </summary>
    private static KvType TypeOf(string value)
    {
        if (value.Length == 0)
            return KvType.String;

        if (value.Length == 18 && value[0] == '0' && value[1] == 'x')
            return KvType.UInt64;

        // Stock compares the end pointers of strtol and strtod against the end
        // of the string: "wholly consumed" is the test, and strtol winning ties
        // is why "8" is an int and "8.5" a float.
        bool wholeInt = int.TryParse(value, NumberStyles.AllowLeadingSign,
                                     CultureInfo.InvariantCulture, out _);

        if (wholeInt)
            return KvType.Int;

        bool wholeFloat = float.TryParse(value, NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out _);

        return wholeFloat ? KvType.Float : KvType.String;
    }

    /// <summary>
    /// Stock's <c>KeyValues::GetInt( name, default )</c>: the default only when
    /// the key is ABSENT, and a conversion from the stored type otherwise.
    /// </summary>
    internal static int GetInt(KeyValuesText block, string key, int defaultValue = 0)
    {
        var found = block.Find(key);

        if (found is null || found.IsBlock)
            return defaultValue;

        string value = found.Value;

        switch (TypeOf(value))
        {
            case KvType.Int:
                return int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

            case KvType.Float:
                // (int) of a float truncates TOWARD ZERO in C++, which is what
                // a C# cast does too.
                return (int)float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

            case KvType.UInt64:
                // Stock asserts and returns 0 -- it will not narrow a 64-bit
                // value into an int. No weapon script has one; reproduced so
                // that if one ever does, both sides answer the same 0.
                return 0;

            default:
                // TYPE_STRING goes through atoi, which is a PREFIX parse: it
                // reads what leading digits there are and answers 0 when there
                // are none. "12abc" is 12 and "abc" is 0 -- neither of which
                // int.TryParse would give.
                return Atoi(value);
        }
    }

    /// <summary>
    /// Stock's <c>KeyValues::GetString( name, default )</c>: the default only
    /// when the key is ABSENT, and the stored type rendered back to text
    /// otherwise.
    /// </summary>
    internal static string GetString(KeyValuesText block, string key, string defaultValue = "")
    {
        var found = block.Find(key);

        if (found is null || found.IsBlock)
            return defaultValue;

        string value = found.Value;

        switch (TypeOf(value))
        {
            case KvType.Int:
                // "%d" of the parsed int, so a value written "007" comes back
                // as "7".
                return int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                          .ToString(CultureInfo.InvariantCulture);

            case KvType.Float:
                // "%f" is SIX decimal places, always.
                return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
                            .ToString("F6", CultureInfo.InvariantCulture);

            default:
                return value;
        }
    }

    /// <summary>
    /// C's <c>atoi</c>: optional leading whitespace, an optional sign, then as
    /// many digits as there are. No digits is 0, and trailing rubbish is
    /// ignored rather than refused.
    /// </summary>
    private static int Atoi(string value)
    {
        int i = 0;

        while (i < value.Length && char.IsWhiteSpace(value[i]))
            i++;

        bool negative = false;

        if (i < value.Length && (value[i] == '+' || value[i] == '-'))
        {
            negative = value[i] == '-';
            i++;
        }

        long result = 0;

        while (i < value.Length && value[i] >= '0' && value[i] <= '9')
        {
            result = result * 10 + (value[i] - '0');
            i++;

            // atoi's behaviour on overflow is undefined; clamping keeps this
            // total rather than throwing on a script nobody should write.
            if (result > int.MaxValue)
                return negative ? int.MinValue : int.MaxValue;
        }

        return (int)(negative ? -result : result);
    }
}
