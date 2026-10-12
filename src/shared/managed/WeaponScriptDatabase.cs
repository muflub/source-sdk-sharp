//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The weapon script database — the managed half of
/// <c>game/shared/weapon_parse.cpp</c>'s <c>m_WeaponInfoDatabase</c>,
/// <c>PrecacheFileWeaponInfoDatabase</c> and the three lookups over it.
///
/// <para>
/// <b>What the C++ side actually did in this build, measured.</b> Before this
/// port, <c>PrecacheFileWeaponInfoDatabase</c> had exactly ONE caller on the
/// client link line — the port's own <c>world_adapter_client.cpp:702</c>, in
/// <c>W_Precache()</c> — and its RESULT had none: <c>nm</c> over all 287 client
/// objects finds no consumer of <c>LookupWeaponInfoSlot</c>,
/// <c>GetFileWeaponInfoFromHandle</c>, <c>GetInvalidWeaponInfoHandle</c>,
/// <c>GetWeaponSoundFromString</c>, <c>gWR</c> or <c>WeaponsResource::</c>
/// outside the three files themselves. The HUD callers stock has
/// (<c>weapon_selection.cpp</c>, <c>history_resource.cpp</c>,
/// <c>hud_ammo.cpp</c>, <c>c_baseplayer.cpp</c>) are not compiled into this
/// project.
/// </para>
///
/// <para>
/// <b>And the manifest it walks is not present.</b> There is no
/// <c>scripts/weapon_manifest.txt</c> on any search path this mod mounts — not
/// in <c>game/mod_sharp/scripts</c> and not in the HL2MP content VPK, whose
/// only "manifest" entry is <c>soundscapes_manifest</c>. So stock's precache
/// loaded the manifest, failed, and returned having parsed nothing. That is the
/// honest statement of what this port replaces, and it is why the gate for this
/// lane is a test against the real script files rather than a claim that a live
/// run proves anything: <b>the capability was reachable and empty</b>.
/// </para>
///
/// <para>
/// The database is still ported in full rather than dropped, because the
/// scripts themselves ARE shipped — thirteen of them in
/// <c>game/mod_sharp/scripts</c> — and a mod that adds a manifest, or managed
/// code that asks for a weapon by name, must get stock's answers.
/// </para>
/// </summary>
public sealed class WeaponScriptDatabase
{
    /// <summary>
    /// The handle a lookup that found nothing answers — stock's
    /// <c>GetInvalidWeaponInfoHandle()</c>, i.e. <c>CUtlDict</c>'s
    /// <c>InvalidIndex()</c>, which for its <c>unsigned short</c> index is
    /// <c>(unsigned short)-1</c>.
    ///
    /// <para>
    /// <b>It is NOT 0 and NOT -1 as an int.</b> Zero is a VALID handle — the
    /// first weapon inserted — and this port has already been bitten once by
    /// treating a default-constructed handle as an absence.
    /// </para>
    /// </summary>
    public const int InvalidHandle = 0xFFFF;

    /// <summary>Where the manifest lives, and what stock asks for.</summary>
    public const string ManifestPath = "scripts/weapon_manifest.txt";

    /// <summary>
    /// How this database reads a file off the engine's <c>GAME</c> search
    /// paths, or null for "there is no reader".
    ///
    /// <para>
    /// <b>A DELEGATE RATHER THAN <c>IEngineFiles</c>, and the side audit is why.</b>
    /// <c>IEngineFiles.ReadGameFile</c> is backed by ABI slots
    /// <c>FilesReadGameFile</c> and <c>FilesGameFileSize</c>, and both are
    /// filled only by <c>game/server/shell_files.cpp</c> — they are
    /// SERVER-ONLY. <c>SourceSharp.Shared</c> may lean only on slots BOTH sides
    /// fill, and <c>SourceSharp.Client</c> may not lean on a server slot at
    /// all, so a database that read its own files could live on neither.
    /// <c>SideAuditTests.SharedAssemblyLeansOnlyOnSlotsBothSidesFill</c> caught
    /// exactly that in the first version of this file.
    /// </para>
    ///
    /// <para>
    /// So reading is the CALLER's job and this type is engine-free. A side that
    /// has a reader passes one; a side that does not passes null and gets an
    /// empty database rather than a broken one.
    /// </para>
    /// </summary>
    private readonly Func<string, string?>? _read;

    private readonly List<WeaponScript> _scripts = new();

    // Stock's CUtlDict defaults to k_eDictCompareTypeCaseInsensitive, so
    // LookupWeaponInfoSlot( "WEAPON_PISTOL" ) finds a script inserted as
    // "weapon_pistol". Reproduced.
    private readonly Dictionary<string, int> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the console (X360) bucket keys win, i.e. stock's
    /// <c>hud_fastswitch.GetInt() == 2</c>.
    ///
    /// <para>
    /// <b>This is the ConVar read that kept <c>hud_fastswitch</c> declared in
    /// C++.</b> <c>weapon_parse.cpp:359</c> held the only <c>extern ConVar
    /// hud_fastswitch;</c> on the client link line, and
    /// <c>shell_hud.cpp</c>'s native declaration existed solely to satisfy it.
    /// With the parse managed, the value arrives here as a property and the
    /// native declaration had no referencer left — lane 4c-render measured that
    /// (<c>weapon_parse.cpp</c> is no longer compiled at all; the symbol read
    /// one definer and zero importers on both link lines) and moved the
    /// declaration to <c>SourceSharp.Client/ConVars.cs</c>.
    /// </para>
    /// </summary>
    public bool UseConsoleBuckets { get; set; }

    /// <param name="readGameFile">
    /// Reads a path off the engine's <c>GAME</c> search paths, answering null
    /// when it is on none of them. Null means "this side has no reader", which
    /// is the CLIENT's situation today — see <see cref="_read"/>.
    /// </param>
    /// <param name="log">Where a script that cannot be read is said (statics to zero).</param>
    public WeaponScriptDatabase(ILog log, Func<string, string?>? readGameFile = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _read = readGameFile;
    }

    private readonly ILog _log;

    /// <summary>Whether this database can read anything at all.</summary>
    public bool HasReader => _read is not null;

    /// <summary>How many scripts have been read.</summary>
    public int Count => _scripts.Count;

    /// <summary>
    /// Stock's <c>LookupWeaponInfoSlot</c>: the handle for a name already read,
    /// or <see cref="InvalidHandle"/>.
    /// </summary>
    public int LookupSlot(string name)
    {
        if (name is null)
            return InvalidHandle;

        return _byName.TryGetValue(name, out int handle) ? handle : InvalidHandle;
    }

    /// <summary>
    /// Stock's <c>GetFileWeaponInfoFromHandle</c>. An out-of-range or invalid
    /// handle answers <see cref="Null"/> rather than null — stock returns a
    /// shared, default-constructed <c>gNullWeaponInfo</c> so that no caller
    /// has to null-check, and a port that answered null instead would turn
    /// stock's benign miss into a crash.
    /// </summary>
    public WeaponScript FromHandle(int handle)
    {
        if (handle < 0 || handle >= _scripts.Count)
            return Null;

        return _scripts[handle];
    }

    /// <summary>
    /// Stock's <c>gNullWeaponInfo</c>: a default-constructed script that every
    /// failed lookup answers with.
    ///
    /// <para>
    /// Its field values are <c>FileWeaponInfo_t</c>'s CONSTRUCTOR defaults,
    /// which are NOT the parser's defaults — the constructor zeroes the clip
    /// sizes where a parsed script defaults them to <see
    /// cref="WeaponScript.NoClip"/>, and it sets <c>bAutoSwitchTo</c> false
    /// where the parser defaults it true. Both are reproduced exactly.
    /// </para>
    /// </summary>
    public static WeaponScript Null { get; } = new WeaponScript
    {
        // FileWeaponInfo_t::FileWeaponInfo_t(), field for field.
        ClassName = "",
        PrintName = "",
        ViewModel = "",
        WorldModel = "",
        AnimationPrefix = "",
        Slot = 0,
        Position = 0,
        MaxClip1 = 0,
        MaxClip2 = 0,
        DefaultClip1 = 0,
        DefaultClip2 = 0,
        Weight = 0,
        RumbleEffect = -1,
        AutoSwitchTo = false,
        AutoSwitchFrom = false,
        Flags = WeaponItemFlags.None,
        Ammo1 = "",
        Ammo2 = "",
        MeleeWeapon = false,
        BuiltRightHanded = true,
        AllowFlipping = true,
        ShowUsageHint = false,
    };

    /// <summary>
    /// Stock's <c>PrecacheFileWeaponInfoDatabase</c>: walk
    /// <c>scripts/weapon_manifest.txt</c> and read every script it names.
    ///
    /// <para>
    /// Idempotent in stock's own way — it returns immediately when the database
    /// already holds anything — so a level init may call it blind.
    /// </para>
    /// </summary>
    /// <returns>How many scripts were read by this call.</returns>
    public int PrecacheFromManifest()
    {
        if (_scripts.Count != 0)
            return 0;

        if (_read is null)
        {
            _log.Msg("[SourceSharp/WeaponScripts] no file reader on this side; the " +
                    "weapon script database stays empty. See WeaponScriptDatabase._read.\n");
            return 0;
        }

        string? text = _read(ManifestPath);

        if (text is null)
        {
            // Stock is SILENT here: LoadFromFile returns false and the function
            // falls straight through to deleteThis. That silence is why nobody
            // noticed the manifest is absent from this mod. Said out loud
            // instead -- and at Msg rather than Warning, because on THIS mod it
            // is the expected state and a warning every level load would be
            // noise that trains people to ignore warnings.
            _log.Msg($"[SourceSharp/WeaponScripts] no '{ManifestPath}' on the search " +
                    "path; no weapon script was read. This is the expected state for " +
                    "this mod -- stock's C++ precache did exactly the same thing.\n");
            return 0;
        }

        KeyValuesText manifest;

        try
        {
            manifest = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[SourceSharp/WeaponScripts] '{ManifestPath}' is malformed: {e.Message}\n");
            return 0;
        }

        int read = 0;

        foreach (var root in manifest.Children)
        {
            foreach (var entry in root.Children)
            {
                if (!string.Equals(entry.Name, "file", StringComparison.OrdinalIgnoreCase))
                {
                    // Stock calls Error() here, which is a FATAL exit. A
                    // malformed manifest killing the game is stock's choice and
                    // not one worth porting: this says the same thing and keeps
                    // reading, because the alternative is a mod author losing
                    // the process over a typo in a data file.
                    _log.Warning($"[SourceSharp/WeaponScripts] '{ManifestPath}': expecting " +
                                $"'file', got '{entry.Name}' -- entry skipped\n");
                    continue;
                }

                if (ReadScript(FileBase(entry.Value)) is not null)
                    read++;
            }
        }

        return read;
    }

    /// <summary>
    /// Stock's <c>ReadWeaponDataFromFileForSlot</c>: read
    /// <c>scripts/&lt;name&gt;.txt</c> into a slot, reusing the slot if the
    /// script has already been parsed.
    /// </summary>
    /// <returns>The script, or null when the file could not be read.</returns>
    public WeaponScript? ReadScript(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName))
            return null;

        if (_byName.TryGetValue(weaponName, out int existing))
            return _scripts[existing];

        if (_read is null)
            return null;

        string path = $"scripts/{weaponName}.txt";
        string? text = _read(path);

        if (text is null)
        {
            _log.Warning($"[SourceSharp/WeaponScripts] could not read '{path}'\n");
            return null;
        }

        return AddFromText(weaponName, text, path);
    }

    /// <summary>
    /// Parses one script's TEXT into the database. Public so a test can drive
    /// the whole database without an engine filesystem, and so a caller that
    /// already has the bytes does not have to go back through one.
    /// </summary>
    /// <returns>The script, or null when the text is not a usable script.</returns>
    public WeaponScript? AddFromText(string weaponName, string text, string origin = "")
    {
        if (string.IsNullOrEmpty(weaponName))
            return null;

        if (_byName.TryGetValue(weaponName, out int existing))
            return _scripts[existing];

        string where = origin.Length != 0 ? origin : weaponName;

        KeyValuesText document;

        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[SourceSharp/WeaponScripts] '{where}' is malformed: {e.Message}\n");
            return null;
        }

        // Stock hands FileWeaponInfo_t::Parse the file's ROOT and reads keys
        // straight off it -- the root being named "WeaponData" is incidental to
        // the parse. Walked over every root rather than assuming the first,
        // because a KV1 document may hold several.
        foreach (var root in document.Children)
        {
            var script = WeaponScriptParser.Parse(root, weaponName, UseConsoleBuckets);

            _byName[weaponName] = _scripts.Count;
            _scripts.Add(script);

            return script;
        }

        _log.Warning($"[SourceSharp/WeaponScripts] '{where}' holds no key block\n");
        return null;
    }

    /// <summary>Every script read, in the order they were read.</summary>
    public IReadOnlyList<WeaponScript> All => _scripts;

    /// <summary>
    /// Stock's <c>Q_FileBase</c>: the file name with its directories and its
    /// extension stripped, so a manifest line saying
    /// <c>scripts/weapon_pistol.txt</c> asks for <c>weapon_pistol</c>.
    /// </summary>
    public static string FileBase(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        int start = path.LastIndexOfAny(new[] { '/', '\\' }) + 1;
        int dot = path.LastIndexOf('.');

        int end = dot > start ? dot : path.Length;

        return path.Substring(start, end - start);
    }
}
