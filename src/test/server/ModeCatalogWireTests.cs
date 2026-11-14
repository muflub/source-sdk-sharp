using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The catalog's way to the clients (plan_bots_teams_tf2 D.2): its text
/// round-trips every field, a hostile label cannot add a line, the playing
/// mode's id is published to <c>sv_mode_active</c> when it changes, and each
/// player coming in gets the catalog with this map's support.
/// </summary>
public class ModeCatalogWireTests
{
    private sealed class Level : ILevelInfo
    {
        public string? Map => "dm_lockdown";
        public bool IsReady => true;
    }

    private static readonly SettingSpec Bots = new("ai_bot_count", SettingKind.Int, [], "4", SettingApplies.NextMap, Votable: false, AdminOnly: true, Min: 0f, Max: 16.5f);

    private static List<CatalogMode> Sample() =>
    [
        new(CoreModeSpecs.Deathmatch(), new MapSupport(true, "")),
        new(CoreModeSpecs.TeamDeathmatch() with { Settings = [.. CoreModeSpecs.TeamDeathmatch().Settings, Bots] }, new MapSupport(true, "")),
        new(new ModeSpec("ctf", "#TF2_Gametype_Ctf", true, [], ["item_teamflag", "item_flag2"]), new MapSupport(false, "dm_lockdown has no item_teamflag")),
    ];

    [Fact]
    public void TheCatalogRoundTripsEveryField()
    {
        var sent = Sample();

        var read = ModeCatalogText.Read(ModeCatalogText.Write(sent));

        Assert.Equal(sent.Count, read.Count);
        for (int i = 0; i < sent.Count; i++)
        {
            Assert.Equal((sent[i].Spec.Id, sent[i].Spec.Title, sent[i].Spec.Teamplay, sent[i].OnThisMap),
                         (read[i].Spec.Id, read[i].Spec.Title, read[i].Spec.Teamplay, read[i].OnThisMap));
            Assert.Equal(sent[i].Spec.RequiresAnyOf, read[i].Spec.RequiresAnyOf);
            Assert.Equal(sent[i].Spec.Settings.Count, read[i].Spec.Settings.Count);
            for (int s = 0; s < sent[i].Spec.Settings.Count; s++)
            {
                var (a, b) = (sent[i].Spec.Settings[s], read[i].Spec.Settings[s]);
                Assert.Equal((a.Key, a.Kind, a.Default, a.Applies, a.Votable, a.AdminOnly, a.Min, a.Max),
                             (b.Key, b.Kind, b.Default, b.Applies, b.Votable, b.AdminOnly, b.Min, b.Max));
                Assert.Equal(a.Choices, b.Choices);
            }
        }
    }

    [Fact]
    public void ALabelCannotAddALineOrAField()
    {
        var hostile = new ModeSpec("evil\nM\tx", "Title\twith\ttabs", false,
                                   [CoreModeSpecs.Choice("mp_fraglimit", "0", ("0\u001f1", "No\nlimit"))], []);

        var read = ModeCatalogText.Read(ModeCatalogText.Write([new CatalogMode(hostile, new MapSupport(true, "a\nb"))]));

        var mode = Assert.Single(read);
        Assert.Equal(("evilMx", "Titlewithtabs", "ab"), (mode.Spec.Id, mode.Spec.Title, mode.OnThisMap.Reason));
        Assert.Equal(new SettingChoice("01", "Nolimit"), Assert.Single(mode.Spec.Settings[0].Choices));
    }

    [Fact]
    public void ALineItCannotReadIsSkipped()
    {
        string text = ModeCatalogText.Write(Sample()) + "S\tbroken\n" + "X\tnothing\n";

        Assert.Equal(3, ModeCatalogText.Read(text).Count);
        Assert.Empty(ModeCatalogText.Read("S\torphan\t0\t0\t0\t1\t0\t0\t0\t\n"));
    }

    private sealed class Rig
    {
        public readonly TeamDeathmatchTests.Rig Game = new();
        public readonly SvModeActiveConVar Active;
        public readonly ModeCatalogPublisher Publisher;

        public Rig()
        {
            // A registry that holds what is written, as the engine's would.
            Active = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>())).Declare<SvModeActiveConVar>();
            var catalog = new GameModeCatalog(Game.Modes, new BspEntityClasses(new GameModeCatalogTests.Bsps()), new Level());
            Publisher = new ModeCatalogPublisher(Game.Modes, catalog, Active, Game.Host);
        }
    }

    [Fact]
    public void ThePlayingModesIdIsPublishedWhenItChanges()
    {
        var rig = new Rig();
        Assert.Equal("", rig.Active.Value);

        rig.Publisher.Publish();
        Assert.Equal("null", rig.Active.Value);

        rig.Game.Tdm();
        rig.Publisher.Publish();
        Assert.Equal("tdm", rig.Active.Value);
    }

    [Fact]
    public void APlayerComingInGetsTheCatalogInPieces()
    {
        var rig = new Rig();

        rig.Publisher.OnClientActive(3);

        var sent = rig.Game.Host.AllOf("UserMessages.Send").ToList();
        Assert.NotEmpty(sent);
        Assert.All(sent, s => Assert.Equal(("SourceSharp:ModeCatalogPiece", 3), ((string)s.Args[0]!, (int)s.Args[2]!)));
    }

    [Fact]
    public void TheEntriesSayWhetherThisMapCanPlayEachMode()
    {
        var rig = new Rig();
        var catalog = new GameModeCatalog(rig.Game.Modes, new BspEntityClasses(new GameModeCatalogTests.Bsps()), new Level());

        var entries = ModeCatalogPublisher.Entries(catalog);

        Assert.Equal(["null", "tdm"], entries.Select(e => e.Spec.Id));
        Assert.All(entries, e => Assert.True(e.OnThisMap.Supported));
    }
}
