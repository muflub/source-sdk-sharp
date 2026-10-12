//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;

using SourceSharp.Abi;

namespace SourceSharp.Shared.Soundscapes;

/// <summary>
/// Every soundscape the manifest names, in the order that gives each one its
/// INDEX — the number the server and the client refer to a soundscape by.
///
/// <para>
/// <b>Why one class rather than one per side.</b> The index is a SHARED number
/// and nothing on the wire carries the name: a <c>Soundscape</c> user message
/// says "play number 31". Stock builds that number twice, once in
/// <c>game/server/soundscape_system.cpp</c> and once in
/// <c>game/client/c_soundscape.cpp</c>, by two loops written to be in step —
/// and the shim reproduced both. Any disagreement between them is SILENT: the
/// wrong ambience plays, or none does, and nothing reports an error. A file
/// skipped on one side, a section with no subkeys counted on one side, the
/// map's own file added twice on one side; each is one wrong number and a
/// quiet map.
/// </para>
///
/// <para>
/// So there is ONE traversal, here, and both sides ask it. The agreement stops
/// being a property somebody maintains and becomes a property of there being
/// one thing. <c>sourcesharp/game/server/shell_soundscape.cpp</c>'s header
/// argued the opposite — "reimplementing the traversal in C# would mean
/// reimplementing the client's too, in step" — and it was right about the risk
/// and wrong about the remedy: the remedy is to reimplement NEITHER and share
/// ONE.
/// </para>
///
/// <para>
/// <b>The traversal is stock's, exactly, and the order is load-bearing.</b>
/// <c>scripts/soundscapes_manifest.txt</c>'s sub-keys in file order, the ones
/// named <c>file</c> only; within each named file, the top-level sections in
/// file order, counting only those that have at least one subkey; then the
/// map's own <c>scripts/soundscapes_&lt;map&gt;.txt</c> appended, but only if
/// the manifest did not already list it. Every one of those clauses is a
/// number that moves if it is dropped.
/// </para>
///
/// <para>
/// <b>A file that does not load is SKIPPED, not fatal</b>, which is stock's
/// behaviour and matters here. RE-MEASURED on 2026-09-20 rather than carried
/// over: the shipped manifest names TWELVE files and
/// <c>game/mod_sharp/scripts/</c> holds THREE of them —
/// <c>soundscapes_streetwar.txt</c>, <c>soundscapes_labs.txt</c> and
/// <c>soundscapes_strike.txt</c>. The other nine are mounted out of HL2's VPKs
/// or are absent, and a mod that has neither simply has fewer soundscapes.
/// Those three yield an index of 46. Native gets the skip from
/// <c>LoadFromFile</c> returning false; this gets it from a null read and from
/// catching a parse failure. A parse failure is LOGGED, because "the file is
/// missing" and "the file is broken" are different problems with the same
/// symptom.
/// </para>
///
/// <para>
/// <b>BOTH SIDES USE IT.</b> The server's index has been this since
/// plan_server_novalve P2.4. The client's moved with its playback, as this
/// paragraph used to require: in sound cluster 17 (lane c2-sims) the client
/// soundscape system became the managed <c>ClientSoundscapes</c>, which plays
/// from this library's <see cref="KeyValuesText"/> trees. The native
/// <c>CUtlVector</c> in <c>shell_soundscape_client.cpp</c> went with that
/// file, so there is one parse and one index on each side.
/// </para>
/// </summary>
public sealed class SoundscapeLibrary
{
    /// <summary>The manifest, which is the only path built in rather than read.</summary>
    public const string ManifestPath = "scripts/soundscapes_manifest.txt";

    private readonly List<Soundscape> _all = new();

    /// <summary>
    /// Every soundscape, in index order. <c>_all[i].Index == i</c>.
    /// </summary>
    public IReadOnlyList<Soundscape> All => _all;

    /// <summary>How many were loaded. Stock's <c>m_soundscapeCount</c>.</summary>
    public int Count => _all.Count;

    /// <summary>
    /// Reads the manifest and everything it names.
    /// </summary>
    /// <param name="readGameFile">
    /// The engine filesystem, as <c>IEngineFiles.ReadGameFile</c> — not
    /// <c>File.ReadAllText</c>. Eight of the eleven manifest entries live
    /// inside a mounted VPK and have no path on disk.
    /// </param>
    /// <param name="mapName">
    /// The current map, WITHOUT a directory or an extension, or null in the
    /// menu. Stock appends <c>scripts/soundscapes_&lt;map&gt;.txt</c> when the
    /// manifest has not already named it.
    /// </param>
    /// <param name="onWarning">
    /// Where a bogus manifest entry or an unparseable file is reported. Stock
    /// calls <c>Warning()</c>; a caller that does not want console output (a
    /// test) passes its own.
    /// </param>
    public static SoundscapeLibrary Load(
        Func<string, string?> readGameFile,
        string? mapName = null,
        Action<string>? onWarning = null)
    {
        if (readGameFile is null)
            throw new ArgumentNullException(nameof(readGameFile));

        var library = new SoundscapeLibrary();
        var warn = onWarning ?? (_ => { });

        string? manifestText = readGameFile(ManifestPath);
        if (manifestText is null)
        {
            // Stock calls Error() here, which halts the game. This does not:
            // an empty library answers -1 for every name, which is what an
            // env_soundscape with an unknown name already gets, and the caller
            // can say so once rather than the process dying inside a parse.
            warn($"soundscapes: manifest '{ManifestPath}' did not load; no soundscapes registered");
            return library;
        }

        KeyValuesText manifest;
        try
        {
            manifest = KeyValuesText.Parse(manifestText);
        }
        catch (KeyValuesTextException ex)
        {
            warn($"soundscapes: manifest '{ManifestPath}' is malformed ({ex.Message})");
            return library;
        }

        // The map's own file, which the manifest may or may not already name.
        string? mapFile = string.IsNullOrEmpty(mapName)
            ? null
            : $"scripts/soundscapes_{mapName}.txt";

        // THE FIRST ROOT ONLY, and this is not the same rule the soundscape
        // FILES get. Stock reads the manifest with LoadKeyValues into one
        // KeyValues and then walks GetFirstSubKey()/GetNextKey() over ITS
        // CHILDREN — so a second top-level block in the manifest is not read.
        // AddFile below walks every root of a soundscape file, because
        // AddSoundScapeFile does exactly that with `pKeys = pKeys->GetNextKey()`.
        // The two are different on both native sides and the difference is a
        // shifted index, so it is reproduced rather than tidied.
        if (manifest.Children.Count > 0)
        {
            foreach (var sub in manifest.Children[0].Children)
            {
                if (!string.Equals(sub.Name, "file", StringComparison.OrdinalIgnoreCase))
                {
                    warn($"soundscapes: manifest '{ManifestPath}' with bogus file type "
                       + $"'{sub.Name}', expecting 'file'");
                    continue;
                }

                library.AddFile(sub.Value, readGameFile, warn);

                // Case-INSENSITIVE, because stock's test is FStrEq, which is
                // Q_stricmp == 0. A manifest that spells the map's file with a
                // different case would otherwise load it twice and shift every
                // index after it.
                if (mapFile is not null
                    && string.Equals(sub.Value, mapFile, StringComparison.OrdinalIgnoreCase))
                {
                    mapFile = null;
                }
            }
        }

        if (mapFile is not null)
            library.AddFile(mapFile, readGameFile, warn);

        return library;
    }

    /// <summary>
    /// One file's top-level sections, appended in file order.
    ///
    /// <para>
    /// THE SUBKEY TEST IS NOT A TIDINESS CHECK. A top-level section with no
    /// children is not counted, on both native sides, so skipping the test
    /// here would shift every later index by one. It is what makes a stray
    /// <c>"name" "value"</c> line at the top of a soundscape file harmless.
    /// </para>
    /// </summary>
    private void AddFile(string path, Func<string, string?> readGameFile, Action<string> warn)
    {
        string? text = readGameFile(path);
        if (text is null)
            return;   // stock: LoadFromFile answered false. Not an error.

        KeyValuesText document;
        try
        {
            document = KeyValuesText.Parse(text);
        }
        catch (KeyValuesTextException ex)
        {
            warn($"soundscapes: '{path}' is malformed and was skipped ({ex.Message})");
            return;
        }

        foreach (var section in document.Children)
        {
            if (section.Children.Count == 0)
                continue;

            _all.Add(new Soundscape(_all.Count, section.Name, path, section));
        }
    }

    /// <summary>
    /// The index for a name, or -1 — stock's answer for a miss, which every
    /// caller reads as "no such soundscape".
    ///
    /// <para>
    /// <b>THE LAST MATCH WINS, and that is the CLIENT's rule rather than an
    /// arbitrary pick.</b> The client's FindSoundscapeByName counts DOWN from
    /// the end, so a name defined twice resolves to the later definition —
    /// which is what actually plays. The server's CUtlDict answered whichever
    /// node its tree found first, which for a duplicate name is unspecified,
    /// so the two native sides could disagree about a duplicate and nothing
    /// would say so. Taking the client's rule makes the answer the one the
    /// player hears.
    /// </para>
    /// </summary>
    public int IndexOf(string name)
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        for (int i = _all.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_all[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>The soundscape at an index, or null — stock's IsValidIndex.</summary>
    public Soundscape? ByIndex(int index)
        => index >= 0 && index < _all.Count ? _all[index] : null;

    /// <summary>The name at an index, or null.</summary>
    public string? NameByIndex(int index) => ByIndex(index)?.Name;
}

/// <summary>
/// One registered soundscape: its index, its name, the file it came from, and
/// the script block that says what it plays.
/// </summary>
/// <param name="Index">Its position in <see cref="SoundscapeLibrary.All"/>.</param>
/// <param name="Name">The top-level section name, as written.</param>
/// <param name="SourceFile">The manifest entry it was read from, for reports.</param>
/// <param name="Script">The parsed block — playlooping, playrandom, dsp and the rest.</param>
public sealed record Soundscape(int Index, string Name, string SourceFile, KeyValuesText Script);
