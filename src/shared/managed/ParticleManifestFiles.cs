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
/// Which particle config files the manifests name: particle_parse.cpp's
/// GetParticleManifest and the map half of ParseParticleEffectsMap, read as
/// text in C#. Both sides ask it: the server's <c>ParticleManifest</c> reads
/// the files' system names itself; the client hands the list to native, whose
/// CParticleSystemMgr loads each file (plan_client_novalve C9: KeyValues'
/// text parsing moves to managed). An entry keeps stock's '!' always-precache
/// marker; the reader of the list decides what it means.
/// </summary>
public sealed class ParticleManifestFiles(ILog log, Func<string, string, byte[]?> readOnPath)
{
    public const string GameManifest = "particles/particles_manifest.txt";
    public const int MaxMapEntries = 64;

    /// <summary>
    /// GetParticleManifest: every "file" entry of the game's manifest, in
    /// order. A missing manifest is said and answers none.
    /// </summary>
    public List<string> Game()
    {
        var files = new List<string>();
        KeyValuesText? manifest = Load(GameManifest, "GAME");
        if (manifest == null)
        {
            log.Warning($"PARTICLE SYSTEM: Unable to load manifest file '{GameManifest}'\n");
            return files;
        }

        foreach (KeyValuesText sub in TopLevelKeys(manifest))
        {
            if (!string.Equals(sub.Name, "file", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning($"CParticleMgr::Init:  Manifest '{GameManifest}' with bogus file type '{sub.Name}', expecting 'file'\n");
                continue;
            }
            files.Add(sub.Value);
        }
        return files;
    }

    /// <summary>
    /// The map's manifest: particles.txt inside the BSP, then the map's own
    /// manifest inside the BSP, then on the game path; the first found. Its
    /// "file" entries under particles/ (marker or not), at most
    /// <see cref="MaxMapEntries"/>. No manifest answers <c>null</c>, without a
    /// word: most maps have none.
    /// </summary>
    public List<string>? Map(string mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return null;

        string mapManifest = $"maps/{mapName}_particles.txt";
        KeyValuesText? manifest = Load("particles.txt", "BSP")
                               ?? Load(mapManifest, "BSP")
                               ?? Load(mapManifest, "GAME");
        if (manifest == null)
            return null;

        var files = new List<string>();
        foreach (KeyValuesText sub in TopLevelKeys(manifest))
        {
            if (files.Count >= MaxMapEntries)
            {
                log.Warning($"CParticleMgr::LevelInit:  Map particles manifest '{mapManifest}' specifies more than {MaxMapEntries} entries.\n");
                break;
            }

            if (!string.Equals(sub.Name, "file", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning($"CParticleMgr::LevelInit:  Manifest '{mapManifest}' with bogus file type '{sub.Name}', expecting 'file'\n");
                continue;
            }

            // Custom particles must live in particles/, marker or not.
            string path = sub.Value;
            string dir = DirectoryOf(path.StartsWith('!') ? path.Substring(1) : path);
            if (!string.Equals(dir, "particles", StringComparison.OrdinalIgnoreCase))
            {
                log.Warning($"CParticleMgr::LevelInit:  Manifest '{mapManifest}' contains a particle file '{path}' that's not under the particles directory. Custom particles must be placed in the particles directory.\n");
                continue;
            }
            files.Add(path);
        }
        return files;
    }

    private KeyValuesText? Load(string path, string pathId)
    {
        byte[]? bytes = readOnPath(path, pathId);
        if (bytes == null)
            return null;
        try
        {
            return KeyValuesText.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (KeyValuesTextException e)
        {
            log.Warning($"'{path}' ({pathId}) is not a KeyValues document: {e.Message}\n");
            return null;
        }
    }

    // A manifest is one named block ("particles_manifest" { ... }); stock's
    // loader reads the FIRST root's subkeys, so that is what is walked here.
    private static IEnumerable<KeyValuesText> TopLevelKeys(KeyValuesText document)
        => document.Children.Count == 0 ? Array.Empty<KeyValuesText>() : document.Children[0].Children;

    // V_StripFilename: the text before the last separator, "" when there is none.
    internal static string DirectoryOf(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return slash <= 0 ? "" : path.Substring(0, slash);
    }
}
