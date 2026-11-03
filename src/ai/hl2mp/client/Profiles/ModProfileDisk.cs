using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Client.Profiles;

/// <summary>
/// The profiles on the mod's own disk. The bytes go through the engine's
/// file system on the <c>MOD</c> path (<see cref="IFwdBaseFileSystem"/>:
/// <c>ReadAll</c>, <c>Open("wb")</c>, <c>Write</c>, <c>Close</c>, the
/// user's 2026-10-19 file-handle ruling). Its table on the client has no
/// <c>CreateDirHierarchy</c> and no <c>FindFirst</c>, and an engine
/// <c>Open</c> for writing makes no directory, so the directory is made and
/// listed under the mod's game dir (<see cref="IModInfo.GameDir"/>, the
/// <c>-game</c> directory the MOD path names), as the UI host reads its
/// theme file there (plan_hl2mp_ai §12.4, Phase 5c).
/// </summary>
[SideService(Side.Client, As = [typeof(IProfileDisk)])]
public sealed class ModProfileDisk(IFwdBaseFileSystem files, IModInfo mod) : IProfileDisk
{
    public const string PathId = "MOD";

    private string FullDirectory => Path.Combine(mod.GameDir, BotProfileStore.Directory);

    public IReadOnlyList<string> Names()
        => System.IO.Directory.Exists(FullDirectory)
            ? [.. System.IO.Directory.GetFiles(FullDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()]
            : [];

    public byte[]? Read(string path) => files.FileExists(path, PathId) ? files.ReadAll(path, PathId) : null;

    public string? Write(string path, byte[] bytes)
    {
        try
        {
            System.IO.Directory.CreateDirectory(FullDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"cannot make {FullDirectory}: {e.Message}";
        }

        nint handle = files.Open(path, "wb", PathId);
        if (handle == 0)
            return $"cannot open {path} for writing";
        try
        {
            int written = files.Write(bytes, handle);
            return written == bytes.Length ? null : $"wrote {written} of {bytes.Length} bytes to {path}";
        }
        finally
        {
            files.Close(handle);
        }
    }
}
