//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System;

namespace SourceSharp.Shared;

/// <summary>
/// The sound categories a weapon script's <c>SoundData</c> block may name.
///
/// <para>
/// <b>This is <c>WeaponSound_t</c> from <c>game/shared/weapon_parse.h</c>, and
/// the numbering is load-bearing</b>: stock stores the sounds in an array
/// indexed by this enum and looks a name up by walking
/// <c>pWeaponSoundCategories</c> in the same order
/// (<c>GetWeaponSoundFromString</c>). A renumbering here silently re-points
/// every weapon's sounds.
/// </para>
///
/// <para>
/// <b>Not to be confused with <c>SourceSharp.Abi.WeaponSound</c></b>, which is
/// a THREE-value managed abstraction (<c>Single = 0</c>, <c>Empty = 1</c>,
/// <c>Reload = 2</c>) used by <c>IEngineWeapons.PlaySound</c>. The two types
/// share a concept and share no values — <c>Empty</c> is 0 here and 1 there —
/// so they are deliberately different types rather than one reused. Converting
/// between them is a mapping, never a cast.
/// </para>
///
/// <para>
/// <c>WPN_DOUBLE</c> is spelled <see cref="Double"/> here. Stock's name carries
/// a <c>WPN_</c> prefix only because <c>windows.h</c> defines <c>DOUBLE</c>;
/// that collision does not exist in C#, and the SCRIPT key is
/// <c>"double_shot"</c> either way.
/// </para>
/// </summary>
public enum WeaponScriptSound
{
    Empty = 0,
    Single = 1,
    SingleNpc = 2,
    Double = 3,
    DoubleNpc = 4,
    Burst = 5,
    Reload = 6,
    ReloadNpc = 7,
    MeleeMiss = 8,
    MeleeHit = 9,
    MeleeHitWorld = 10,
    Special1 = 11,
    Special2 = 12,
    Special3 = 13,
    Taunt = 14,
    Deploy = 15,
}

/// <summary>
/// The script keys behind <see cref="WeaponScriptSound"/> — stock's
/// <c>pWeaponSoundCategories</c>, whose own comment says it "needs to match the
/// WeaponSound_t enum in weapon_parse.h".
/// </summary>
public static class WeaponScriptSounds
{
    /// <summary>How many categories there are — stock's
    /// <c>NUM_SHOOT_SOUND_TYPES</c>.</summary>
    public const int Count = 16;

    /// <summary>
    /// The category names as written in a weapon script, indexed by
    /// <see cref="WeaponScriptSound"/>.
    /// </summary>
    public static readonly ImmutableArray<string> Categories =
    [
        "empty",
        "single_shot",
        "single_shot_npc",
        "double_shot",
        "double_shot_npc",
        "burst",
        "reload",
        "reload_npc",
        "melee_miss",
        "melee_hit",
        "melee_hit_world",
        "special1",
        "special2",
        "special3",
        "taunt",
        "deploy",
    ];

    /// <summary>
    /// Stock's <c>GetWeaponSoundFromString</c>: the category index for a script
    /// key, or <c>-1</c> for a key that names none.
    ///
    /// <para>
    /// The comparison is case-INsensitive because stock's is
    /// (<c>Q_stricmp</c>), and a null argument answers -1 rather than throwing
    /// because stock's <c>Q_stricmp</c> against a null is a crash this port has
    /// no reason to reproduce.
    /// </para>
    /// </summary>
    public static int FromString(string? name)
    {
        if (name is null)
            return -1;

        for (int i = 0; i < Categories.Length; i++)
        {
            if (string.Equals(Categories[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}

/// <summary>
/// Stock's <c>ITEM_FLAG_*</c> bits from <c>game/shared/shareddefs.h</c>, which a
/// weapon script sets either as a raw <c>item_flags</c> number or by naming one
/// flag at a time.
/// </summary>
[Flags]
public enum WeaponItemFlags
{
    None = 0,
    SelectOnEmpty = 1 << 0,
    NoAutoReload = 1 << 1,
    NoAutoSwitchEmpty = 1 << 2,
    LimitInWorld = 1 << 3,
    Exhaustible = 1 << 4,
    DoHitLocationDamage = 1 << 5,
    NoAmmoPickups = 1 << 6,
    NoItemPickup = 1 << 7,
}

/// <summary>
/// The named-flag table — stock's <c>g_ItemFlags</c>, in its order.
///
/// <para>
/// The ORDER is not decorative: stock walks this array and applies each flag
/// whose key is present, so a script that both clears and sets overlapping bits
/// resolves in array order.
/// </para>
/// </summary>
public static class WeaponItemFlagNames
{
    /// <summary>The script key for each flag, paired with its bit.</summary>
    public static readonly ImmutableArray<(string Name, WeaponItemFlags Value)> All =
    [
        ("ITEM_FLAG_SELECTONEMPTY", WeaponItemFlags.SelectOnEmpty),
        ("ITEM_FLAG_NOAUTORELOAD", WeaponItemFlags.NoAutoReload),
        ("ITEM_FLAG_NOAUTOSWITCHEMPTY", WeaponItemFlags.NoAutoSwitchEmpty),
        ("ITEM_FLAG_LIMITINWORLD", WeaponItemFlags.LimitInWorld),
        ("ITEM_FLAG_EXHAUSTIBLE", WeaponItemFlags.Exhaustible),
        ("ITEM_FLAG_DOHITLOCATIONDMG", WeaponItemFlags.DoHitLocationDamage),
        ("ITEM_FLAG_NOAMMOPICKUPS", WeaponItemFlags.NoAmmoPickups),
        ("ITEM_FLAG_NOITEMPICKUP", WeaponItemFlags.NoItemPickup),
    ];
}

/// <summary>
/// One weapon's script data — the managed <c>FileWeaponInfo_t</c>.
///
/// <para>
/// <b>Why this is managed.</b> <c>plan_entity.md</c> §6 rules the weapon script
/// database out of the native set: clip sizes, slots, buckets and sounds are
/// what the game does, and <c>scripts/weapon_*.txt</c> is not an engine data
/// file — nothing outside the game DLLs reads it. That is the whole distance
/// between this and the old <c>shell_propdata_system.cpp</c>, which §6 kept
/// because <c>scripts/propdata.txt</c> is read through the engine's filesystem
/// at level init and keyed by surface properties the physics engine owns. That
/// file has since left both sides (the server's in P5, the client's in C9).
/// </para>
///
/// <para>
/// <b>What is NOT here, and why.</b> <c>FileWeaponInfo_t</c> also carries nine
/// <c>CHudTexture *</c> icon pointers, <c>iSpriteCount</c> and
/// <c>bLoadedHudElements</c>. Those are not script data: they are the HUD
/// renderer's own objects, filled in afterwards by
/// <c>WeaponsResource::LoadWeaponSprites</c> from the script's
/// <c>TextureData</c> block. They stay on the native side of the HUD and are
/// deliberately absent here rather than mirrored as nulls.
/// </para>
/// </summary>
public sealed class WeaponScript
{
    /// <summary>Stock's <c>WEAPON_NOCLIP</c> — "this weapon has no clip".</summary>
    public const int NoClip = -1;

    /// <summary>Stock's <c>MAX_WEAPON_SLOTS</c>: HUD item selection slots.</summary>
    public const int MaxSlots = 6;

    /// <summary>Stock's <c>MAX_WEAPON_POSITIONS</c>: items within a slot.</summary>
    public const int MaxPositions = 20;

    /// <summary>Stock's <c>MAX_WEAPON_STRING</c>, the buffer these fields are
    /// copied into — so the longest string that survives is one less.</summary>
    public const int MaxStringBuffer = 80;

    /// <summary>Stock's <c>MAX_WEAPON_PREFIX</c>.</summary>
    public const int MaxPrefixBuffer = 16;

    /// <summary>Stock's <c>MAX_WEAPON_AMMO_NAME</c>.</summary>
    public const int MaxAmmoNameBuffer = 32;

    /// <summary>Stock's <c>WEAPON_PRINTNAME_MISSING</c>, the printname a script
    /// that names none is given.</summary>
    public const string PrintNameMissing = "!!! Missing printname on weapon";

    /// <summary>The script's own name, e.g. <c>weapon_pistol</c>. Stock copies
    /// the name it was ASKED for, not a key from the file.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Localisation token shown in the HUD.</summary>
    public string PrintName { get; init; } = "";

    /// <summary>The <c>viewmodel</c> key — first person.</summary>
    public string ViewModel { get; init; } = "";

    /// <summary>The <c>playermodel</c> key — the weapon as others see it.</summary>
    public string WorldModel { get; init; } = "";

    /// <summary>The <c>anim_prefix</c> key.</summary>
    public string AnimationPrefix { get; init; } = "";

    /// <summary>The <c>bucket</c> key: which HUD selection slot.</summary>
    public int Slot { get; init; }

    /// <summary>The <c>bucket_position</c> key: position within the slot.</summary>
    public int Position { get; init; }

    /// <summary>The <c>clip_size</c> key, or <see cref="NoClip"/>.</summary>
    public int MaxClip1 { get; init; }

    /// <summary>The <c>clip2_size</c> key, or <see cref="NoClip"/>.</summary>
    public int MaxClip2 { get; init; }

    /// <summary>The <c>default_clip</c> key; defaults to <see cref="MaxClip1"/>.</summary>
    public int DefaultClip1 { get; init; }

    /// <summary>The <c>default_clip2</c> key; defaults to <see cref="MaxClip2"/>.</summary>
    public int DefaultClip2 { get; init; }

    /// <summary>The <c>weight</c> key: autoselection importance.</summary>
    public int Weight { get; init; }

    /// <summary>The <c>rumble</c> key; <c>-1</c> when the script names none.</summary>
    public int RumbleEffect { get; init; }

    /// <summary>The <c>autoswitchto</c> key; defaults TRUE.</summary>
    public bool AutoSwitchTo { get; init; }

    /// <summary>The <c>autoswitchfrom</c> key; defaults TRUE.</summary>
    public bool AutoSwitchFrom { get; init; }

    /// <summary>The resolved <c>item_flags</c> plus every named flag applied.</summary>
    public WeaponItemFlags Flags { get; init; }

    /// <summary>The <c>primary_ammo</c> key, with stock's <c>"None"</c>
    /// spelled as the EMPTY string.</summary>
    public string Ammo1 { get; init; } = "";

    /// <summary>The <c>secondary_ammo</c> key, same convention.</summary>
    public string Ammo2 { get; init; } = "";

    /// <summary>The <c>MeleeWeapon</c> key; defaults false.</summary>
    public bool MeleeWeapon { get; init; }

    /// <summary>The <c>BuiltRightHanded</c> key; defaults TRUE.</summary>
    public bool BuiltRightHanded { get; init; }

    /// <summary>The <c>AllowFlipping</c> key; defaults TRUE.</summary>
    public bool AllowFlipping { get; init; }

    /// <summary>The <c>showusagehint</c> key; defaults false.</summary>
    public bool ShowUsageHint { get; init; }

    /// <summary>
    /// The <c>SoundData</c> block, indexed by <see cref="WeaponScriptSound"/>.
    /// Always <see cref="WeaponScriptSounds.Count"/> long; an entry a script
    /// does not name is the EMPTY string, which is stock's zeroed buffer.
    /// </summary>
    public IReadOnlyList<string> Sounds { get; init; } = EmptySounds();

    /// <summary>A fresh, fully-empty sound table.</summary>
    public static string[] EmptySounds()
    {
        var sounds = new string[WeaponScriptSounds.Count];

        for (int i = 0; i < sounds.Length; i++)
            sounds[i] = "";

        return sounds;
    }

    /// <summary>The sound for one category, or the empty string.</summary>
    public string Sound(WeaponScriptSound category)
    {
        int index = (int)category;

        return index >= 0 && index < Sounds.Count ? Sounds[index] : "";
    }
}
