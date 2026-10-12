namespace SourceSharp.Abi;

/// <summary>
/// Reading a GAME SCRIPT — a file on the engine's own search paths.
///
/// <para>
/// <b>This area exists because of a measurement, not for symmetry.</b>
/// plan_gamedll_after.md Phase A3 moves <c>CDecalEmitterSystem</c> and half of
/// <c>CPropData</c> into managed code, and both of them begin by parsing a
/// KeyValues file. Part 9a's ownership test puts the PARSE in C# — a name table
/// and a weighted pick have no engine tie — and leaves FINDING the file with
/// the engine, because that is <c>IFileSystem</c>'s and nothing else's.
/// </para>
///
/// <para>
/// <b>And it is load-bearing rather than tidy.</b>
/// <c>scripts/decals_subrect.txt</c> and <c>scripts/propdata.txt</c> are in
/// NEITHER the mod directory NOR any mounted VPK — a sweep of all eight VPK
/// directories finds neither name. They are loose files under the SDK's
/// <c>hl2/scripts</c>, reachable only through the <c>game</c> search paths
/// gameinfo.txt sets up. So a managed <c>File.ReadAllText</c> over a
/// mod-relative path finds nothing, the tables come up empty, and NOTHING SAYS
/// SO: an empty decal table answers −1 to every lookup, exactly as a working
/// one answers for a decal that is genuinely absent.
/// </para>
///
/// <para>
/// <b>Read-only, and deliberately.</b> There is no write member here and no
/// plan for one. The mod's own persistent state has other routes, and a game
/// script is content the game ships rather than something it edits — a write
/// slot would be a way to modify the SDK's shared files from a mod.
/// </para>
/// </summary>
public interface IEngineFiles
{
    /// <summary>
    /// The size in bytes of a file on the engine's <c>GAME</c> search paths, or
    /// −1 when it is on none of them.
    ///
    /// <para>
    /// <b>−1 and 0 are different answers.</b> 0 is a file that exists and is
    /// empty. Collapsed, a missing game script reads as an empty one and the
    /// table it fills comes up empty with nothing said.
    /// </para>
    /// </summary>
    int GameFileSize(string path);

    /// <inheritdoc cref="EngineSide.FilesReadGameFile"/>
    string? ReadGameFile(string path);

    /// <inheritdoc cref="EngineSide.FilesReadGameFileBytes"/>
    byte[]? ReadGameFileBytes(string path);

    /// <inheritdoc cref="EngineSide.FilesReadOnPathBytes"/>
    /// <remarks>A double that only knows the GAME path answers that one.</remarks>
    byte[]? ReadFileOnPath(string path, string pathId)
        => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ReadGameFileBytes(path) : null;
}
