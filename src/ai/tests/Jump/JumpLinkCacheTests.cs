using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A level's blast links found once off the game's thread and cached beside its graph (lane
/// G5c-2a): a file reads back what was written, a file of another revision, graph or inputs is
/// refused, a second load reads the file where the first ran the pass, and a cancelled pass
/// writes nothing.
/// </summary>
public sealed class JumpLinkCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "g5c2-links-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A yard with a ledge across it, 30 % of a jump's rise up today: one pair of levels, a link.</summary>
    private static readonly Lazy<(VoxelColumns Columns, NavGraph Graph)> Yard = new(() =>
    {
        float height = 16f * MathF.Round(0.3f * Envelope().Rise / 16f);
        VoxelColumns columns = LedgeField(height);
        return (columns, GraphOf(columns));
    });

    private static readonly NavLinkCosts Costs = new() { RunSpeed = RunSpeed };

    private static JumpLink Link(int from, int to, float? walk) => new(from, to, new JumpRecipe(new Vector(1f, 2f, 3f), new Vector(4f, 5f, 6.5f), 160f, 35f, 180f, -3, true),
                                                                      JumpFamily.Forward, 0.95f, 1.785f, 1.25f, 28.7f, walk);

    private string FilePath => Path.Combine(_dir, "map.navgraph." + new JumpLinkCache().Suffix);

    private JumpLinkWork Work(NavGraph graph, VoxelColumns columns, JumpLinkCache cache, string key = "key", JumpSearchSettings? search = null)
    {
        JumpPilotLimits limits = Limits();
        JumpLauncher launcher = JumpLauncher.Rocket(RocketDamage, RocketRefire);
        return new JumpLinkWork(graph, columns, Sims().Create(columns, launcher), Sims().Create(JumpEnvelope.OpenAir(), launcher), limits, search ?? new JumpSearchSettings(),
                                JumpPlacesSettings.Of(TFNavProfile.ModNumbers), [], Costs, cache, FilePath, key, ThreadCpuClockMicros());
    }

    private static Func<double>? ThreadCpuClockMicros() => SourceSharp.Ai.Server.Senses.ThreadCpuClock.Micros();

    [Fact]
    public void AFileReadsBackTheLinksAndTheCostsItWasWrittenWith()
    {
        NavGraph graph = Yard.Value.Graph;
        var file = new JumpLinkFile([Link(1, 2, 3.25f), Link(5, 9, null)], 7, 1234, 456.5);
        var cache = new JumpLinkCache();
        JumpLinkFile? back = cache.Decode(cache.Encode(graph, "k", file), graph, "k");
        Assert.NotNull(back);
        Assert.Equal(file.Links, back.Links);
        Assert.Equal((7, 1234, 456.5), (back.Candidates, back.Flights, back.PassMs));
    }

    [Fact]
    public void AFileOfAnotherRevisionGraphOrInputsIsRefused()
    {
        NavGraph graph = Yard.Value.Graph;
        var file = new JumpLinkFile([Link(1, 2, 3.25f)], 1, 10, 1d);
        byte[] bytes = new JumpLinkCache().Encode(graph, "k", file);
        Assert.NotNull(new JumpLinkCache().Decode(bytes, graph, "k"));
        Assert.Null(new JumpLinkCache(JumpLinkCache.Revision + 1).Decode(bytes, graph, "k"));
        Assert.Null(new JumpLinkCache().Decode(bytes, graph, "j"));
        Assert.Null(new JumpLinkCache().Decode(bytes, graph, "other"));
        Assert.Null(new JumpLinkCache().Decode(bytes, GraphOf(LedgeField(48f)), "k"));
        Assert.Null(new JumpLinkCache().Decode(bytes.AsSpan(0, bytes.Length - 1), graph, "k"));
        Assert.Null(new JumpLinkCache().Decode([.. bytes, 0], graph, "k"));
        // And the file's name says its revision, so another one never even opens it.
        Assert.NotEqual(new JumpLinkCache().Suffix, new JumpLinkCache(JumpLinkCache.Revision + 1).Suffix);
    }

    [Fact]
    public void TheInputsKeyFollowsEveryNumberTheLinksComeFrom()
    {
        JumpLauncher launcher = JumpLauncher.Rocket(RocketDamage, RocketRefire);
        JumpPlacesSettings places = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        string key = JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings(), places, [1, 2]);
        Assert.Equal(key, JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings(), places, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(JumpLauncher.Rocket(RocketDamage + 1f, RocketRefire), Limits(), new JumpSearchSettings(), places, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(launcher, Limits(turn: 180f), new JumpSearchSettings(), places, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings { Keep = 0.7f }, places, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings { Up = new JumpSearchSettings().Up with { PitchStep = 5f } }, places, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings(), places with { PerPair = 2 }, [1, 2]));
        Assert.NotEqual(key, JumpLinkCache.InputsKey(launcher, Limits(), new JumpSearchSettings(), places, [1, 3]));
    }

    [Fact]
    public void TheSecondLoadReadsTheFileWhereTheFirstRanThePass()
    {
        (VoxelColumns columns, NavGraph graph) = Yard.Value;
        JumpLinkOutcome cold = Work(graph, columns, new JumpLinkCache()).Run(CancellationToken.None);
        Assert.False(cold.FromCache);
        Assert.True(cold.Flights > 0 && cold.Set.Links.Count > 0, $"the control: the pass flew and found: {cold.Flights} flights, {cold.Set.Links.Count} links");
        Assert.True(File.Exists(FilePath));

        JumpLinkOutcome warm = Work(graph, columns, new JumpLinkCache()).Run(CancellationToken.None);
        Assert.True(warm.FromCache);
        Assert.Equal(cold.Set.Jumps, warm.Set.Jumps);
        Assert.Equal(cold.Set.Links.Hash, warm.Set.Links.Hash);
        // The pass's own numbers come with the file; reading it flew nothing more.
        Assert.Equal((cold.Candidates, cold.Flights), (warm.Candidates, warm.Flights));

        // The negative controls: another revision, other inputs and another graph each run the pass again.
        string written = Convert.ToHexString(File.ReadAllBytes(FilePath));
        Assert.False(Work(graph, columns, new JumpLinkCache(JumpLinkCache.Revision + 1)).Run(CancellationToken.None).FromCache);
        Assert.False(Work(graph, columns, new JumpLinkCache(), key: "kez").Run(CancellationToken.None).FromCache);
        VoxelColumns lower = LedgeField(columns.Spans(columns.Width - 1, 0)[0].Bottom - 16f);
        Assert.False(Work(GraphOf(lower), lower, new JumpLinkCache()).Run(CancellationToken.None).FromCache);
        Assert.NotEqual(written, Convert.ToHexString(File.ReadAllBytes(FilePath)));
    }

    [Fact]
    public void ACancelledPassWritesNoFile()
    {
        (VoxelColumns columns, NavGraph graph) = Yard.Value;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Work(graph, columns, new JumpLinkCache()).Run(cancel.Token));
        Assert.False(File.Exists(FilePath));
        // The control: uncancelled, the same work writes it.
        Work(graph, columns, new JumpLinkCache()).Run(CancellationToken.None);
        Assert.True(File.Exists(FilePath));
    }
}
