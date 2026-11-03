//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// <c>CTFPlayerClassDataMgr</c> (tf_classdata.cpp): the ten class scripts,
/// read once and held for the process, one table per side.
///
/// <para>
/// Stock parses at the game system's <c>Init</c> and never again (each
/// entry's <c>m_bParsed</c>). Here <see cref="Load"/> does that once: the
/// level hooks of each side (<c>TFClassTableLoader</c>) call it as the first
/// level loads, and the indexer calls it if something asks sooner. Each
/// script is <c>scripts/playerclasses/&lt;raw name&gt;</c> read through
/// <see cref="EncryptedKeyValues"/> (stock's <c>ReadEncryptedKVFile</c>: the
/// <c>.txt</c>, else the <c>.ctx</c> under <see cref="TFEncryption.Key"/>) on
/// the GAME path.
/// </para>
///
/// <para>
/// A script that is missing or malformed leaves its class as
/// <see cref="TFClassData.Missing"/> (stock leaves the entry as its
/// constructor made it) and is logged; nothing throws. Each class that loads
/// is logged with its speed, health and ammo maxima.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFClassTable
{
    /// <summary>Where the class scripts live, before the raw class name.</summary>
    public const string ScriptDirectory = "scripts/playerclasses/";

    /// <summary>
    /// The level-hook order of the loaders on both sides. Orders must not tie
    /// across the core and every loaded mod; TF2 is the layer under
    /// tf2_descent, so its hooks take the thousand just below the mods'
    /// range (<c>ModStages.First</c> = 1,000,000, where Descent's start), and
    /// above every core stage (the highest is 2,300).
    /// </summary>
    public const int LoadOrder = 1_000_000 - 1_000 + 10;

    private readonly ILog _log;
    private readonly IEngineFiles _files;
    private TFClassData[]? _classes;

    public TFClassTable(ILog log, IEngineFiles files)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>Whether <see cref="Load"/> has run.</summary>
    public bool IsLoaded => _classes is not null;

    /// <summary>
    /// <c>CTFPlayerClassDataMgr::Get</c>: a class's data, Undefined through
    /// Civilian. Loads the table first if nothing has.
    /// </summary>
    public TFClassData this[TFClass playerClass]
    {
        get
        {
            if (playerClass < TFClass.Undefined || playerClass >= TFClass.CountAll)
                throw new ArgumentOutOfRangeException(nameof(playerClass), playerClass, "TF_CLASS_COUNT_ALL bounds the class table");

            Load();
            return _classes![(int)playerClass];
        }
    }

    /// <summary>The script a class reads, without its extension (<c>s_aPlayerClassFiles</c>).</summary>
    public static string ScriptName(TFClass playerClass) => ScriptDirectory + TFClassNames.Raw(playerClass);

    /// <summary>
    /// <c>CTFPlayerClassDataMgr::Init</c>: reads every class script, once.
    /// Returns how many parsed (10 with the shipped content); a second call
    /// reads nothing and returns the same count.
    /// </summary>
    public int Load()
    {
        if (_classes is null)
        {
            var reader = new EncryptedKeyValues(_files, Encoding.ASCII.GetBytes(TFEncryption.Key));
            var classes = new TFClassData[(int)TFClass.CountAll];
            classes[(int)TFClass.Undefined] = TFClassData.Undefined;

            for (var c = TFClass.Scout; c < TFClass.CountAll; c++)
                classes[(int)c] = Read(reader, c);

            _classes = classes;
            _log.Msg($"[TF2/Classes] {Parsed(classes)} of {(int)TFClass.CountAll - 1} class scripts loaded\n");
        }

        return Parsed(_classes);
    }

    private TFClassData Read(EncryptedKeyValues reader, TFClass playerClass)
    {
        string name = ScriptName(playerClass);
        KeyValuesText document;
        try
        {
            document = reader.Read(name);
        }
        catch (FileNotFoundException e)
        {
            _log.Warning($"[TF2/Classes] {TFClassNames.Raw(playerClass)}: {e.Message}; the class has no data\n");
            return TFClassData.Missing(playerClass);
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[TF2/Classes] {name} is malformed: {e.Message}; the class has no data\n");
            return TFClassData.Missing(playerClass);
        }

        // ReadEncryptedKVFile loads the document's first key; ParseData reads
        // that block's children.
        if (document.Children.Count == 0 || !document.Children[0].IsBlock)
        {
            _log.Warning($"[TF2/Classes] {name} holds no key block; the class has no data\n");
            return TFClassData.Missing(playerClass);
        }

        TFClassData data = TFClassDataParser.Parse(playerClass, document.Children[0], _log, name);
        _log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2/Classes] {TFClassNames.Raw(playerClass)}: speed_max {data.MaxSpeed} health_max {data.MaxHealth} " +
            $"ammo {data.MaxAmmo(TFAmmo.Primary)}/{data.MaxAmmo(TFAmmo.Secondary)}/{data.MaxAmmo(TFAmmo.Metal)}" +
            $"/{data.MaxAmmo(TFAmmo.Grenades1)}/{data.MaxAmmo(TFAmmo.Grenades2)}/{data.MaxAmmo(TFAmmo.Grenades3)}\n"));
        return data;
    }

    private static int Parsed(TFClassData[] classes)
    {
        int parsed = 0;
        foreach (var data in classes)
        {
            if (data.IsParsed)
                parsed++;
        }
        return parsed;
    }
}
