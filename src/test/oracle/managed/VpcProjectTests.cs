using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <see cref="VpcProject"/>: what a VPC script compiles, from scripts written
/// here, and the HL2MP server project read from the pristine SDK.
/// </summary>
public sealed class VpcProjectTests : IDisposable
{
    private readonly string _src = Directory.CreateTempSubdirectory("vpc-").FullName;

    public void Dispose() => Directory.Delete(_src, recursive: true);

    private string Write(string relative, string text)
    {
        string path = Path.Combine(_src, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string At(string relative) => Path.Combine(_src, relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void AScriptsFilesAreItsOwnDirectorysOrSrcdirs()
    {
        string script = Write("game/server/a.vpc", """
            $Project "A"
            {
                $Folder "Source Files"
                {
                    $File "one.cpp"
                    $File "hl2\two.cpp"
                    $File "$SRCDIR\game\shared\three.cpp"
                }
            }
            """);

        Assert.Equal(new[] { At("game/server/hl2/two.cpp"), At("game/server/one.cpp"), At("game/shared/three.cpp") },
                     VpcProject.Files(script, _src).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AContinuedEntryNamesEveryFileOnItsLines()
    {
        string script = Write("game/server/a.vpc", """
                    $File "one.cpp" \
                          "two.cpp" \
                          "three.cpp"
                    "four.cpp"
            """);

        Assert.Equal(3, VpcProject.Files(script, _src).Count);
        Assert.DoesNotContain(At("game/server/four.cpp"), VpcProject.Files(script, _src));
    }

    [Fact]
    public void AConditionalEntryOrIncludeIsLeftOut()
    {
        Write("game/server/tf.vpc", "$File \"tf_only.cpp\"\n");
        string script = Write("game/server/a.vpc", """
            $Include "$SRCDIR\game\server\tf.vpc" [$TF]
            $File "always.cpp"
            $File "windows.cpp" [$WINDOWS]
            """);

        Assert.Equal(new[] { At("game/server/always.cpp") }, VpcProject.Files(script, _src));
    }

    [Fact]
    public void AnIncludeUnderGameIsFollowedWhateverTheKeywordsCase()
    {
        Write("game/server/base.vpc", "$file \"base.cpp\"\n");
        Write("vpc_scripts/platform.vpc", "$File \"platform.cpp\"\n");
        string script = Write("game/server/a.vpc", """
            $include "$SRCDIR\game\server\base.vpc"
            $Include "$SRCDIR\vpc_scripts\platform.vpc"
            $File "own.cpp"
            """);

        Assert.Equal(new[] { At("game/server/base.cpp"), At("game/server/own.cpp") },
                     VpcProject.Files(script, _src).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The HL2MP server project compiles HL2MP's crossbow and not the
    /// single-player one beside it, and reaches server_base.vpc's files.
    /// </summary>
    [Fact]
    public void TheHl2mpServerProjectIsHl2mpsFiles()
    {
        string game = Path.Combine(ValveSdk.Root, "src", "game");
        IReadOnlyCollection<string> files = VpcProject.Files(Path.Combine(game, "server", "server_hl2mp.vpc"), Path.GetDirectoryName(game)!);

        Assert.Contains(Path.Combine(game, "shared", "hl2mp", "weapon_crossbow.cpp"), files);
        Assert.Contains(Path.Combine(game, "server", "baseanimating.cpp"), files);
        Assert.True(File.Exists(Path.Combine(game, "server", "hl2", "weapon_crossbow.cpp")), "single-player's crossbow is in the tree");
        Assert.DoesNotContain(Path.Combine(game, "server", "hl2", "weapon_crossbow.cpp"), files);
    }
}
