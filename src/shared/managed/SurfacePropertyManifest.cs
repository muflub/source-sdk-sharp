//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// scripts/surfaceproperties_manifest.txt, read in C# — plan_server_novalve.md
/// P2.4, out of physics_shared.cpp's <c>PhysParseSurfaceData</c>, which read
/// the manifest with tier1's KeyValues from <c>CPhysicsHook::Init</c>.
///
/// <para>
/// <b>Only the manifest moved.</b> Each file it names is still parsed by
/// VPhysics itself (<c>IPhysicsSurfaceProps::ParseSurfaceData</c>), exactly as
/// stock handed it over: the surface table is VPhysics' own and the engine and
/// the client read it there.
/// </para>
///
/// <para>
/// <b>Once per process, before any map</b>, as stock's DLL-init parse was: the
/// server module calls <see cref="Load"/> at startup, and the client's
/// PhysParseSurfaceData forwarder asks for it from its DLL init
/// (plan_physics_bones_native_zero 2.6) into the same VPhysics table.
/// </para>
///
/// <para>
/// <b>A missing file is said, not fatal.</b> Stock called <c>Error()</c> for a
/// manifest or a named file that did not load. This warns and parses what it
/// can, which leaves VPhysics' default surface for anything not parsed.
/// </para>
/// </summary>
public sealed class SurfacePropertyManifest
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public const string ManifestPath = "scripts/surfaceproperties_manifest.txt";

    private readonly IEngineFiles _files;

    public SurfacePropertyManifest(ILog log, IEngineFiles files) 

    {

        _log = log;

        _files = files;

    }

    /// <summary>
    /// The files the manifest names, in order: the first root's children
    /// called <c>file</c> (case-insensitively), each other key warned about —
    /// stock's loop over <c>GetFirstSubKey()</c>.
    /// </summary>
    public static List<string> FilesNamed(string manifestText, Action<string> warn)
    {
        var files = new List<string>();
        KeyValuesText document = KeyValuesText.Parse(manifestText);

        if (document.Children.Count == 0)
            return files;

        foreach (var sub in document.Children[0].Children)
        {
            if (string.Equals(sub.Name, "file", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(sub.Value);
                continue;
            }

            warn($"surfaceprops::Init:  Manifest '{ManifestPath}' with bogus file type '{sub.Name}', expecting 'file'");
        }

        return files;
    }

    /// <summary>Reads the manifest and hands VPhysics each file. Answers how many files it parsed.</summary>
    public int Load(IVtablePhysicsSurfaceProps props)
    {
        string? manifest = _files.ReadGameFile(ManifestPath);

        if (manifest is null)
        {
            _log.Warning($"[SourceSharp] Unable to load manifest file '{ManifestPath}'; no surface properties parsed\n");
            return 0;
        }

        List<string> files;
        try
        {
            files = FilesNamed(manifest, w => _log.Warning(w + "\n"));
        }
        catch (KeyValuesTextException e)
        {
            _log.Warning($"[SourceSharp] manifest file '{ManifestPath}' is malformed: {e.Message}\n");
            return 0;
        }

        int parsed = 0;

        foreach (string file in files)
        {
            string? text = _files.ReadGameFile(file);

            if (text is null)
            {
                _log.Warning($"[SourceSharp] Unable to load surface prop file '{file}' " +
                            $"(referenced by manifest file '{ManifestPath}')\n");
                continue;
            }

            props.ParseSurfaceData(file, text);
            parsed++;
        }

        if (System.Environment.GetEnvironmentVariable("SOURCESHARP_NOVALVE_SNAPSHOT") != null)
        {
            int count = props.SurfacePropCount();
            for (int i = 0; i < count; i++)
                _log.Msg($"[novalve-snapshot:surfaceprop] {i} {props.PropName(i)}\n");
            _log.Msg($"[novalve-snapshot:surfaceprop-count] {count}\n");
        }

        return parsed;
    }
}
