using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SourceSharp.Shared.Soundscapes;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The one soundscape traversal, and every clause of it that MOVES A NUMBER.
///
/// <para>
/// The index is a shared wire value — a <c>Soundscape</c> user message says
/// "play number 31" and carries no name — so each rule below is tested for the
/// index it produces rather than for the set of names it finds. A traversal
/// that finds the right soundscapes in the wrong order is the failure this
/// class exists to prevent, and a test that only checked membership would pass
/// on it.
/// </para>
///
/// <para>
/// The last group reads the REAL shipped scripts, and the group after that
/// reads both NATIVE traversals' source text. A managed traversal checked only
/// against fixtures its own author wrote is a stand-in that drifts silently.
/// </para>
/// </summary>
public class SoundscapeLibraryTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>A filesystem of literal strings — the engine's ReadGameFile shape.</summary>
    private static Func<string, string?> Files(params (string path, string text)[] entries)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, text) in entries)
            table[path] = text;

        return path => table.TryGetValue(path, out string? text) ? text : null;
    }

    private static string Manifest(params string[] paths)
    {
        var body = string.Join("\n", paths.Select(p => $"    \"file\" \"{p}\""));
        return "\"soundscapes_manifest\"\n{\n" + body + "\n}\n";
    }

    /// <summary>A soundscape file of sections, each with one subkey.</summary>
    private static string File_(params string[] names)
        => string.Join("\n", names.Select(n => $"\"{n}\"\n{{\n    \"dsp\" \"1\"\n}}"));

    // ------------------------------------------------------------- the order

    [Fact]
    public void TheIndexIsTheManifestOrderThenFileOrder()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt", "scripts/b.txt")),
            ("scripts/a.txt", File_("a.one", "a.two")),
            ("scripts/b.txt", File_("b.one"))));

        Assert.Equal(new[] { "a.one", "a.two", "b.one" },
                     library.All.Select(s => s.Name).ToArray());
        Assert.Equal(0, library.IndexOf("a.one"));
        Assert.Equal(1, library.IndexOf("a.two"));
        Assert.Equal(2, library.IndexOf("b.one"));
    }

    [Fact]
    public void EveryEntrysIndexIsItsOwnPositionInAll()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("one", "two", "three"))));

        for (int i = 0; i < library.Count; i++)
            Assert.Equal(i, library.All[i].Index);
    }

    [Fact]
    public void AMissingFileIsSkippedAndDoesNotShiftTheIndexOfWhatFollows()
    {
        // The shipped manifest names twelve files and nine of them are absent,
        // so this is the normal case rather than an edge one.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt",
             Manifest("scripts/gone.txt", "scripts/a.txt")),
            ("scripts/a.txt", File_("a.one"))));

        Assert.Equal(1, library.Count);
        Assert.Equal(0, library.IndexOf("a.one"));
    }

    [Fact]
    public void ASectionWithNoSubkeysIsNotCounted()
    {
        // Native tests GetFirstSubKey() on both sides. Dropping the test here
        // would shift every later index by one.
        //
        // THIS FACT IS THE ONLY THING THAT CHECKS THIS CLAUSE, and that was
        // measured rather than assumed. Deleting the clause and running
        // cs_soundscape_index_check on a live join still reported
        // "255 agree, 0 differ" -- because every top-level section in all
        // twelve shipped files happens to have subkeys, so the clause is a
        // no-op ON THAT DATA. A live run cannot fail on it; this can.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", "\"stray\" \"value\"\n" + File_("real.one"))));

        Assert.Equal(1, library.Count);
        Assert.Equal(0, library.IndexOf("real.one"));
    }

    [Fact]
    public void AManifestEntryThatIsNotKeyedFileIsSkipped()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt",
             "\"m\"\n{\n  \"notafile\" \"scripts/a.txt\"\n  \"file\" \"scripts/b.txt\"\n}\n"),
            ("scripts/a.txt", File_("a.one")),
            ("scripts/b.txt", File_("b.one"))));

        Assert.Equal(1, library.Count);
        Assert.Equal(0, library.IndexOf("b.one"));
    }

    [Fact]
    public void OnlyTheManifestsFirstRootIsWalked()
    {
        // The manifest is read into ONE KeyValues and its CHILDREN walked, so a
        // second top-level block is not read -- unlike a soundscape file, every
        // root of which is walked. The two rules differ on both native sides.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt",
             "\"first\"\n{\n  \"file\" \"scripts/a.txt\"\n}\n"
           + "\"second\"\n{\n  \"file\" \"scripts/b.txt\"\n}\n"),
            ("scripts/a.txt", File_("a.one")),
            ("scripts/b.txt", File_("b.one"))));

        Assert.Equal(1, library.Count);
        Assert.Equal(-1, library.IndexOf("b.one"));
    }

    [Fact]
    public void EveryRootOfASoundscapeFileIsWalked()
    {
        // The other half of the rule above: AddSoundScapeFile walks
        // GetNextKey(), so a file with several roots contributes all of them.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("one") + "\n" + File_("two"))));

        Assert.Equal(2, library.Count);
    }

    // ---------------------------------------------------------- the map file

    [Fact]
    public void TheMapsOwnFileIsAppendedAfterEverythingTheManifestNamed()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("a.one")),
            ("scripts/soundscapes_mymap.txt", File_("map.one"))),
            mapName: "mymap");

        Assert.Equal(new[] { "a.one", "map.one" }, library.All.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void TheMapsOwnFileIsNotLoadedTwiceWhenTheManifestAlreadyNamedIt()
    {
        // Loading it twice would duplicate its sections and shift nothing
        // before them but everything after -- and there is nothing after, which
        // is exactly why this is easy to get wrong and invisible.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/soundscapes_mymap.txt")),
            ("scripts/soundscapes_mymap.txt", File_("map.one"))),
            mapName: "mymap");

        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void TheDuplicateTestOnTheMapFileIsCaseInsensitive()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("SCRIPTS/SoundScapes_MyMap.TXT")),
            ("scripts/soundscapes_mymap.txt", File_("map.one"))),
            mapName: "mymap");

        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void NoMapNameMeansNoMapFile()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("a.one")),
            ("scripts/soundscapes_mymap.txt", File_("map.one"))));

        Assert.Equal(1, library.Count);
    }

    // ------------------------------------------------------------- the lookup

    [Fact]
    public void LookupIsCaseInsensitiveAsTheScriptsNeed()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("Streetwar.Util_Drips"))));

        Assert.Equal(0, library.IndexOf("streetwar.util_drips"));
    }

    [Fact]
    public void AnUnknownNameIsMinusOneRatherThanZero()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("a.one"))));

        Assert.Equal(-1, library.IndexOf("nothing.like.this"));
        Assert.Equal(-1, library.IndexOf(""));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheLastDefinitionWhichIsWhatPlays()
    {
        // The CLIENT's FindSoundscapeByName counts down from the end, so the
        // later definition is the one the player hears. The server's CUtlDict
        // answered whichever node its tree reached first.
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt", "scripts/b.txt")),
            ("scripts/a.txt", File_("same")),
            ("scripts/b.txt", File_("same"))));

        Assert.Equal(2, library.Count);
        Assert.Equal(1, library.IndexOf("same"));
    }

    [Fact]
    public void AnIndexOutsideTheTableIsNullRatherThanAnEntry()
    {
        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/a.txt")),
            ("scripts/a.txt", File_("a.one"))));

        Assert.Null(library.ByIndex(-1));
        Assert.Null(library.ByIndex(1));
        Assert.Equal("a.one", library.ByIndex(0)!.Name);
    }

    // ------------------------------------------------------- failure is quiet

    [Fact]
    public void AMissingManifestIsAnEmptyLibraryAndAWarningRatherThanAThrow()
    {
        var warnings = new List<string>();

        var library = SoundscapeLibrary.Load(Files(), onWarning: warnings.Add);

        Assert.Equal(0, library.Count);
        Assert.Single(warnings);
    }

    [Fact]
    public void AMalformedSoundscapeFileIsSkippedAndSaidOutLoud()
    {
        // "The file is missing" and "the file is broken" have the same symptom
        // and different causes, so only one of them is silent.
        var warnings = new List<string>();

        var library = SoundscapeLibrary.Load(Files(
            ("scripts/soundscapes_manifest.txt", Manifest("scripts/bad.txt", "scripts/a.txt")),
            ("scripts/bad.txt", "\"unclosed\"\n{\n  \"dsp\" \"1\"\n"),
            ("scripts/a.txt", File_("a.one"))), onWarning: warnings.Add);

        Assert.Equal(1, library.Count);
        Assert.Equal(0, library.IndexOf("a.one"));
        Assert.Single(warnings);
    }

    // ------------------------------------------- the REAL scripts, ON DISK
    //
    // THESE ARE THE ON-DISK SUBSET AND NOT THE GAME'S INDEX, and the difference
    // is large enough to mislead: the disk under game/mod_sharp/scripts/ holds
    // three of the manifest's twelve files and yields 46 soundscapes, while a
    // RUNNING game reads all twelve -- the other nine are mounted out of HL2's
    // VPKs, which have no path on disk -- and its index is 255.
    //
    // Both numbers were measured on 2026-09-20: 46 by this fixture, 255 by
    // cs_soundscape_index_check on a live dm_lockdown join, which reported
    // "255 managed, 255 agree, 0 differ, 0 unknown to native".
    //
    // So these facts pin the traversal against real script text, which is worth
    // doing, and they are NOT evidence about the index the wire carries. Only
    // the live check is that, which is why the live check exists.

    private static Func<string, string?> ShippedFiles()
    {
        string root = Path.Combine(RepoLayout.SdkTree, "game", "mod_sharp");

        return path =>
        {
            string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        };
    }

    [Fact]
    public void TheShippedManifestNamesTwelveFilesAndThreeOfThemArePresent()
    {
        // The control for the two facts below: if the scripts stopped being
        // readable, "46 soundscapes" would become "0" and an assertion on a
        // name would fail for the wrong reason.
        string manifest = Read("game/mod_sharp/scripts/soundscapes_manifest.txt");

        Assert.Equal(12, manifest.Split("\"file\"").Length - 1);

        string dir = Path.Combine(RepoLayout.SdkTree, "game", "mod_sharp", "scripts");

        Assert.True(File.Exists(Path.Combine(dir, "soundscapes_streetwar.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "soundscapes_labs.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "soundscapes_strike.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "soundscapes_canals.txt")));
    }

    [Fact]
    public void TheThreeOnDiskScriptsYieldFortySixSoundscapes()
    {
        // 46 is the ON-DISK subset. The live game's index is 255; see the note
        // above this group. A reader who takes 46 for the wire index will
        // mis-read every number downstream of it.
        var library = SoundscapeLibrary.Load(ShippedFiles());

        Assert.Equal(46, library.Count);
    }

    [Fact]
    public void TheFirstAndLastShippedSoundscapesAreWhereTheTraversalPutsThem()
    {
        // Named rather than just counted: a traversal that found 46 of the
        // wrong things, or the right things in the wrong order, passes a count.
        var library = SoundscapeLibrary.Load(ShippedFiles());

        Assert.Equal("overwatch_mp", library.All[0].Name);
        Assert.Equal("scripts/soundscapes_streetwar.txt", library.All[0].SourceFile);
        Assert.Equal("labs_mp", library.All[39].Name);
        Assert.Equal("scripts/soundscapes_labs.txt", library.All[39].SourceFile);
        Assert.Equal("scripts/soundscapes_strike.txt", library.All[45].SourceFile);
    }

    [Fact]
    public void EveryShippedSoundscapeCarriesTheScriptThatSaysWhatItPlays()
    {
        var library = SoundscapeLibrary.Load(ShippedFiles());

        Assert.All(library.All, s => Assert.NotEmpty(s.Script.Children));
    }

    // ------------- both sides number soundscapes with this library, pinned
    //
    // plan_server_novalve.md P2.4 switched the SERVER to this library and
    // deleted its native registry (held to it on dm_lockdown first, 255
    // soundscapes, index for index). Sound cluster 17 (lane c2-sims) switched
    // the CLIENT: its soundscape system is the managed ClientSoundscapes, the
    // driver loads this library at the level's start, and
    // shell_soundscape_client.cpp, which numbered them natively, is deleted.
    // The agreement is one fact about one library now.

    private static string ServerService()
        => Read("src/sourcesharp/managed/SourceSharp.Server/Entities/Soundscape/Soundscapes.cs");

    private static string ClientDriver()
        => Read("src/sourcesharp/managed/SourceSharp.Client/Soundscapes/ClientSoundscapeDriver.cs");

    [Fact]
    public void BothSidesAreReallyRead()
    {
        // The control for the facts below.
        Assert.Contains("SoundscapeLibrary", ServerService());
        Assert.Contains("class ClientSoundscapeDriver", ClientDriver());
    }

    [Fact]
    public void BothSidesLoadTheLibraryAndNeitherHasANativeRegistry()
    {
        // If either side grows a native registry again, or stops loading the
        // library, it should be a red test here rather than a quiet map.
        Assert.Contains("_library = SoundscapeLibrary.Load(", ServerService());
        Assert.Contains("SoundscapeLibrary.Load(files.ReadGameFile", ClientDriver());
        Assert.False(File.Exists(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server",
                                              "shell_soundscape.cpp")),
                     "the server's native soundscape registry is back");
        Assert.False(File.Exists(Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "client",
                                              "shell_soundscape_client.cpp")),
                     "the client's native soundscape registry is back");
    }
}
