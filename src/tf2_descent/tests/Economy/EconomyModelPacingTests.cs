using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy.Model;
using Xunit;

namespace TF2.Descent.Tests.Economy;

/// <summary>
/// RU14's solo pacing gate (economy_model.json's <c>pacing</c>, the model's band; the design is
/// silent): a solo hero of A.6's first three builds descends 1 to 15 in at most 1.5 times the
/// hours of the party of the four builds on the same seed, dying at most twice a depth.
/// Seeds 1 and 2. The planted fact puts a level's life back at its old 2 and sees it fail.
/// </summary>
public sealed class EconomyModelPacingTests
{
    private static readonly EconomyModel Model = new(DescentContent.Shipped());
    private static readonly ModelGates Gates = new(Model);
    private static readonly string[] Party = ["artillery", "gunman", "trolldier", "sergeant"];
    private static readonly string[] Solo = ["artillery", "gunman", "trolldier"];
    private static readonly ulong[] Seeds = [1, 2];

    private static readonly Lazy<IReadOnlyDictionary<ulong, RunReport>> PartyRuns =
        new(() => Seeds.ToDictionary(s => s, s => Model.Run(Party, s)));

    private static readonly Lazy<IReadOnlyList<RunReport>> SoloRuns =
        new(() => [.. Solo.SelectMany(b => Seeds.Select(s => Model.Run([b], s)))]);

    private static string Say(RunReport r) => $"{r.Builds[0]} seed {r.Seed}: {r.Last.Seconds / 3600:F2} h, {r.Last.Deaths} deaths";

    [Fact]
    public void A_solo_descent_takes_at_most_half_again_the_partys_hours()
    {
        Assert.Equal(Solo.Length * Seeds.Length, SoloRuns.Value.Count);
        Assert.All(SoloRuns.Value, r => Assert.True(Gates.SoloHoursHold(r, PartyRuns.Value[r.Seed]),
            $"{Say(r)} against the party's {PartyRuns.Value[r.Seed].Last.Seconds / 3600:F2} h"));
    }

    [Fact]
    public void A_solo_descent_dies_at_most_twice_a_depth()
    {
        Assert.All(SoloRuns.Value, r => Assert.Equal(15, r.Depths.Count));
        Assert.All(SoloRuns.Value, r => Assert.True(Gates.SoloDeathsHold(r), Say(r)));
    }

    [Fact]
    public void Solo_pacing_fails_when_a_level_gives_its_old_2_life()
    {
        var planted = new EconomyModel(PlantedContent.With("classes:[soldier].perLevel.0.amount=2"));
        var run = planted.Run(["trolldier"], 1);

        Assert.False(new ModelGates(planted).SoloDeathsHold(run), Say(run));
    }
}
