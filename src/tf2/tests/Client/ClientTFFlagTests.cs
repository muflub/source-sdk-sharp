extern alias client;

using SourceSharp.Abi;

using Xunit;

// The client's half, under the alias: the shared source compiles into both
// sides, so the mirror and its netstate exist twice. DataUpdateType itself
// lives in the shared Abi assembly, so it is named unaliased.
using CAbi = client::SourceSharp.Abi;
using ClientFlag = client::TF2.Client.Entities.ClientTFFlag;
using GlowRegistry = client::SourceSharp.Client.Render.GlowObjectManager;

namespace TF2.Tests.Client;

/// <summary>
/// The flag mirror's paper trail (entity_capture_flag.cpp:2591-2645, the
/// client's <c>ManageTrailEffects</c>): the system runs while the wire says
/// stolen and the switch includes the paper, and stops otherwise.
/// </summary>
public sealed class ClientTFFlagTests
{
    private static (CAbi.RecordingEngine Engine, ClientFlag Flag) Mirror(byte status = 1, byte effects = 1, string paper = "")
    {
        var engine = new CAbi.RecordingEngine();
        engine.Returns("Effects.ParticleSystemStart", true);
        engine.Returns("Effects.ParticleSystemFollowPoints", true);
        var flag = new ClientFlag(engine, engine, new GlowRegistry(_ => false, _ => null, _ => 7));
        flag.State.BindLocal();
        flag.State.FlagStatus = status;
        flag.State.UseTrailEffect = effects;
        flag.State.PaperEffect = paper;
        var blob = flag.State.Snapshot();
        flag.OnDataChanged(blob, DataUpdateType.Created);
        return (engine, flag);
    }

    [Fact]
    public void A_stolen_flag_starts_the_paper_trail()
    {
        var (engine, _) = Mirror();

        var start = engine.Last("Effects.ParticleSystemStart");
        Assert.NotNull(start);
        Assert.Equal("player_intel_papertrail", start!.Value.ArgAs<string>(1));
        // Point 0 is the one a trail rides — PATTACH_ABSORIGIN_FOLLOW.
        Assert.Equal(0b1, engine.Last("Effects.ParticleSystemFollowPoints")!.Value.ArgAs<int>(1));
    }

    [Fact]
    public void The_map_s_flag_paper_names_itself()
    {
        var (engine, _) = Mirror(paper: "my_own_paper");

        Assert.Equal("my_own_paper", engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_switch_without_the_paper_starts_nothing()
    {
        var none = Mirror(effects: 0);
        Assert.Equal(0, none.Engine.CountOf("Effects.ParticleSystemStart"));

        var colorOnly = Mirror(effects: 3);
        Assert.Equal(0, colorOnly.Engine.CountOf("Effects.ParticleSystemStart"));
    }

    [Fact]
    public void A_flag_that_is_not_stolen_anymore_stops_the_paper()
    {
        var (engine, flag) = Mirror();

        flag.State.FlagStatus = 0;   // home again
        var blob = flag.State.Snapshot();
        flag.OnDataChanged(blob, DataUpdateType.DataTableChanged);

        Assert.Equal(1, engine.CountOf("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void A_removed_mirror_stops_the_paper()
    {
        var (engine, flag) = Mirror();

        flag.OnRemoved();

        Assert.Equal(1, engine.CountOf("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void A_mirror_removed_without_the_paper_stops_nothing()
    {
        var (engine, flag) = Mirror(effects: 3);

        flag.OnRemoved();

        Assert.Equal(0, engine.CountOf("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void The_glow_half_registers_the_flag_once_and_keeps_the_slot_across_changes()
    {
        var engine = new CAbi.RecordingEngine();
        engine.Returns("Effects.ParticleSystemStart", true);
        var glow = new GlowRegistry(_ => false, _ => null, _ => 7);
        var flag = new ClientFlag(engine, engine, glow);
        flag.State.BindLocal();
        flag.State.GlowEnabled = 1;
        flag.State.Team = 2;      // TF_TEAM_RED
        flag.State.FlagStatus = 0;   // home: no paper to start
        var blob = flag.State.Snapshot();
        flag.OnDataChanged(blob, DataUpdateType.Created);

        Assert.True(glow.HasGlowEffect(flag.Handle));
        Assert.Equal(1, glow.Count);

        // A later change re-colours and re-shows through the same slot; it does
        // not register again (stock's UpdateGlowEffect creates once).
        flag.State.PaperEffect = "other_paper";
        flag.OnDataChanged(flag.State.Snapshot(), DataUpdateType.DataTableChanged);
        Assert.Equal(1, glow.Count);
        Assert.True(glow.IsRenderingWhenOccluded(0));
    }

    [Fact]
    public void The_team_glow_table_holds_stock_s_three_rows()
    {
        // TFTeamPlayRules::GetTeamGlowColor (tf_gamerules.cpp:17565-17584).
        Assert.Equal(0.74f, ClientFlag.GetTeamGlowColor(2).X);
        Assert.Equal(0.23f, ClientFlag.GetTeamGlowColor(2).Y);
        Assert.Equal(0.23f, ClientFlag.GetTeamGlowColor(2).Z);
        Assert.Equal(0.49f, ClientFlag.GetTeamGlowColor(3).X);
        Assert.Equal(0.66f, ClientFlag.GetTeamGlowColor(3).Y);
        Assert.Equal(0.77f, ClientFlag.GetTeamGlowColor(3).Z);
        Assert.Equal(0.76f, ClientFlag.GetTeamGlowColor(0).X);
        Assert.Equal(0.76f, ClientFlag.GetTeamGlowColor(1).Y);
        Assert.Equal(0.76f, ClientFlag.GetTeamGlowColor(9).Z);
    }
}
