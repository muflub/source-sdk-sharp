//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The TF weapon script database, one per side: stock's
/// <c>ReadWeaponDataFromFileForSlot</c> (<c>shared/weapon_parse.cpp</c>) as
/// <c>CTFWeaponBase</c> asks it with <c>GetEncryptionKey()</c>
/// (<c>basecombatweapon_shared.cpp:258</c>): <c>scripts/&lt;weapon name&gt;</c>,
/// the <c>.txt</c> first, else the <c>.ctx</c> under
/// <see cref="TFEncryption.Key"/>, on the GAME path (stage1 Corrections 1, 8),
/// parsed by <see cref="TFWeaponScriptParser"/>.
///
/// <para>
/// Each name is read once and held, a miss included: a missing or malformed
/// script is logged once and answers null (stock leaves the weapon on the
/// empty <c>gNullWeaponInfo</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWeaponScripts
{
    /// <summary>Where the weapon scripts live, before the weapon name.</summary>
    public const string ScriptDirectory = "scripts/";

    private readonly ILog _log;
    private readonly IEngineFiles _files;
    private readonly Dictionary<string, TFWeaponScript?> _byName = new(StringComparer.OrdinalIgnoreCase);
    private EncryptedKeyValues? _reader;

    public TFWeaponScripts(ILog log, IEngineFiles files)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>The script a weapon reads, without its extension: the weapon's class name, lower-cased as the pack spells it.</summary>
    public static string ScriptName(string weaponName) => ScriptDirectory + weaponName.ToLowerInvariant();

    /// <summary>A weapon's script, read on first ask; null when it is missing or malformed.</summary>
    public TFWeaponScript? Get(string weaponName)
    {
        ArgumentException.ThrowIfNullOrEmpty(weaponName);

        if (!_byName.TryGetValue(weaponName, out var script))
        {
            script = Read(weaponName);
            _byName[weaponName] = script;
        }

        return script;
    }

    private TFWeaponScript? Read(string weaponName)
    {
        _reader ??= new EncryptedKeyValues(_files, Encoding.ASCII.GetBytes(TFEncryption.Key));
        string name = ScriptName(weaponName);

        KeyValuesText document;
        try
        {
            document = _reader.Read(name);
        }
        catch (FileNotFoundException e)
        {
            _log.Warning($"[TF2/Weapons] {weaponName}: {e.Message}; the weapon has no script\n");
            return null;
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[TF2/Weapons] {name} is malformed: {e.Message}; the weapon has no script\n");
            return null;
        }

        // ReadEncryptedKVFile loads the document's first key (WeaponData).
        if (document.Children.Count == 0 || !document.Children[0].IsBlock)
        {
            _log.Warning($"[TF2/Weapons] {name} holds no key block; the weapon has no script\n");
            return null;
        }

        return TFWeaponScriptParser.Parse(document.Children[0], weaponName.ToLowerInvariant());
    }
}
