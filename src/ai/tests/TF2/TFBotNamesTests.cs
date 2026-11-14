using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>TF2's bot names (P6 step 1): the tf2 mod's own pools file, read through the file seam by its path.</summary>
public sealed class TFBotNamesTests
{
    public class Files : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IEngineFiles.ReadGameFile))
                return null;
            string asked = Path.Combine(RepoLayout.Repo("game", "mod_tf2"), (string)args![0]!);
            return File.Exists(asked) ? File.ReadAllText(asked) : null;
        }
    }

    [Fact]
    public void TheTF2ModsNamesFileGivesThePools()
    {
        var names = new TFGameFileBotNames(DispatchProxy.Create<IEngineFiles, Files>());
        Assert.True(names.Pools is { } pools && pools.First.Length > 10, names.Refusal);
    }
}
