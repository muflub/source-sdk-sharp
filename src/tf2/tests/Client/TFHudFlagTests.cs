extern alias client;

using SourceSharp.Abi;
using Xunit;

using ClientEntities = client::TF2.Client.Entities;
using ClientPlayer = client::TF2.Client.Player;
using ClientShared = client::TF2.Shared;
using ClientUi = client::TF2.Client.Ui;

// The shared interop compiles per side; the adapter's convar is the client's copy.
using ClientAbi = client::SourceSharp.Abi;

using Engine = client::SourceSharp.Abi.RecordingEngine;
using ClientSources = client::SourceSharp.Client;
using ClientStandalone = client::SourceSharp.Client.Standalone;
using View = client::SourceSharp.Client.ViewReadout;

namespace TF2.Tests.Client;

/// <summary>
/// The flag HUD's adapter (tf_hud_flagstatus.cpp): the captures swap on
/// tf_flag_caps_per_round, the compass bearing's arithmetic and the
/// stolen flag's carrier resolution, the carried-by-local read and the
/// capture-zone arrow's local team.
/// </summary>
public sealed class TFHudFlagTests
{
    // The raw TF team numbers (tf_shareddefs.h): TF_TEAM_RED 2, TF_TEAM_BLUE 3.
    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>ETFFlagStatus: stolen.</summary>
    private const int Stolen = 1;

    /// <summary>The local player index the rig's engine answers.</summary>
    private const int Local = 5;

    /// <summary>The teams' scores and captures, as the scoreboard holds them.</summary>
    private sealed class TeamsFake : ClientEntities.ITFTeamSource
    {
        public int RedScore;
        public int RedCaptures;

        public int Score(int team) => team == Red ? RedScore : 0;

        public int RoundsWon(int team) => 0;

        public int FlagCaptures(int team) => team == Red ? RedCaptures : 0;

        public int Role(int team) => 0;

        public int PlayerCount(int team) => 0;
    }

    /// <summary>A convar backend that stores what it is told, so a SetValue round-trips.</summary>
    private sealed class Backend : ClientAbi.IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;

        public string Get(int id) => _values[id];

        public string? ValueOf(string name)
            => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    /// <summary>The client's entities, the viewer player placed by index.</summary>
    private sealed class Entities : ClientSources.IClientStandalones
    {
        public readonly Dictionary<int, ClientStandalone.ClientStandaloneEntity> ByIndex = [];

        public IReadOnlyList<ClientStandalone.ClientStandaloneEntity> Live() => ByIndex.Values.ToArray();

        public ClientStandalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => ByIndex.TryGetValue(entityIndex, out ClientStandalone.ClientStandaloneEntity? entity) ? entity : null;
    }

    private sealed class FlagRig
    {
        public readonly Engine Engine = Engine.Default();
        public readonly View View = new();
        public readonly ObjectiveRig Rig = new();
        public readonly TeamsFake Teams = new();
        public readonly ClientPlayer.TFClientPlayerResources Resources = new();
        public readonly ClientShared.TFFlagCapsPerRoundConVar Caps;
        public readonly Entities EntitiesBox = new();
        public readonly ClientUi.TFHudFlag Hud;

        public FlagRig()
        {
            Engine.Returns("Hud.LocalPlayerIndex", Local);

            var convars = new ClientAbi.ConVarRegistry(new Backend());
            convars.RegisterTypes([typeof(ClientShared.TFFlagCapsPerRoundConVar)]);
            Caps = convars.Get<ClientShared.TFFlagCapsPerRoundConVar>()!;

            var player = new ClientPlayer.ClientTFPlayerResource(Engine, Resources);
            player.State.BindLocal();
            player.State.Team[Local] = Blue;
            Resources.Live = player;

            Hud = new ClientUi.TFHudFlag(Rig.Source, Teams, Caps, Engine, Engine, Engine, View, Resources, EntitiesBox);
        }

        /// <summary>The local player's entity, spectating <paramref name="target"/> in the named mode.</summary>
        public void Spectate(uint target, int mode = ClientAbi.GameMovement.ObsModeInEye)
        {
            var viewer = new ClientStandalone.ClientSsPlayer(Engine);
            viewer.State.BindLocal();
            viewer.State.ObserverMode = mode;
            viewer.State.ObserverTarget = target;
            EntitiesBox.ByIndex[Local] = viewer;
        }

        public void Look(float yaw)
        {
            View.PublishEye(new Vector(0f, 0f, 70f));
            View.PublishAngles(0f, yaw);
        }

        public void SetLocalTeam(int team) => Resources.Live!.State.Team[Local] = (byte)team;
    }

    [Fact]
    public void The_compass_reads_the_flag_against_the_view_yaw()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagOrigin[Blue * 3] = 100f;
            s.FlagOrigin[Blue * 3 + 2] = 20f;   // the height flattens away
        });
        rig.Look(0f);

        Assert.Equal(0f, rig.Hud.Bearing(Blue), 3);   // dead ahead

        rig.Rig.Arrive(s => s.FlagOrigin[Blue * 3 + 1] = 100f);   // to the left
        Assert.Equal(-45f, rig.Hud.Bearing(Blue), 3);

        rig.Rig.Arrive(s => s.FlagOrigin[Blue * 3 + 1] = -100f);  // to the right
        Assert.Equal(45f, rig.Hud.Bearing(Blue), 3);
    }

    [Fact]
    public void With_no_view_published_the_bearing_holds_at_zero()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagOrigin[Blue * 3] = 100f;
            s.FlagOrigin[Blue * 3 + 1] = -100f;
        });

        // before: no view; after (the fact above): 45 degrees.
        Assert.Equal(0f, rig.Hud.Bearing(Blue), 3);
    }

    [Fact]
    public void A_stolen_flag_points_at_its_carrier()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = Stolen;
            s.FlagCarrier[Blue] = 7;
            s.FlagOrigin[Blue * 3] = 100f;   // the spawn point, stale while carried
        });
        rig.Look(0f);

        rig.Engine.Returns("Effects.ClientEntityOrigin", true);
        rig.Engine.Returns("Effects.ClientEntityOrigin.Origin", new Vector(0f, -100f, 0f));

        // The networked origin alone would read 0; the carrier is to the right.
        Assert.Equal(90f, rig.Hud.Bearing(Blue), 3);
    }

    [Fact]
    public void The_carried_read_names_the_local_carrier()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Red] = true;
            s.FlagStatus[Red] = Stolen;
            s.FlagCarrier[Red] = 9;
        });

        Assert.False(rig.Hud.CarriedByLocal(Red));   // someone else carries it

        rig.Rig.Arrive(s => s.FlagCarrier[Red] = Local);

        Assert.True(rig.Hud.CarriedByLocal(Red));
    }

    [Fact]
    public void The_captures_read_the_flag_caps_while_a_target_is_set()
    {
        var rig = new FlagRig();
        rig.Teams.RedCaptures = 2;
        rig.Teams.RedScore = 40;

        Assert.Equal(3, rig.Hud.CapsPerRound);   // the declaration's default
        Assert.Equal(2, rig.Hud.Captures(Red));

        // The target dropped to 0: the dialog variable falls back to the score.
        rig.Caps.SetValue(0);

        Assert.Equal(0, rig.Hud.CapsPerRound);
        Assert.Equal(40, rig.Hud.Captures(Red));
    }

    [Fact]
    public void The_zone_arrow_points_at_the_local_teams_zone()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.ZoneOrigin[Red * 3] = 100f;          // RED's zone dead ahead
            s.ZoneOrigin[Blue * 3 + 1] = -100f;    // BLUE's to the right
        });
        rig.Look(0f);

        // The local player is BLUE: their zone sits to the right.
        Assert.Equal(90f, rig.Hud.ZoneBearing, 3);

        rig.SetLocalTeam(Red);

        Assert.Equal(0f, rig.Hud.ZoneBearing, 3);
    }

    [Fact]
    public void The_flag_row_reads_the_teams_flags()
    {
        var rig = new FlagRig();

        // Before the map's table arrives: no team fields a flag.
        Assert.False(rig.Hud.Present);
        Assert.False(rig.Hud.Has(Red));
        Assert.Equal(0, rig.Hud.Status(Red));
        Assert.Equal(0f, rig.Hud.ResetSeconds(Red));

        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Red] = true;
            s.HasFlag[Blue] = true;
            s.FlagStatus[Red] = 1;    // ETFFlagStatus_Stolen
            s.FlagStatus[Blue] = 2;   // ETFFlagStatus_Dropped
            s.FlagResetTime[Blue] = 42f;
        });

        // After: RED's flag is being carried, BLU's lies dropped.
        Assert.True(rig.Hud.Present);
        Assert.True(rig.Hud.Has(Red));
        Assert.True(rig.Hud.Has(Blue));
        Assert.Equal(1, rig.Hud.Status(Red));
        Assert.Equal(2, rig.Hud.Status(Blue));
    }

    [Fact]
    public void The_dropped_flag_counts_against_the_frame_clock()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = 2;   // dropped
            s.FlagResetTime[Blue] = 42f;
        }, now: 40f);

        rig.Engine.Returns("Clock.Now", 40f);
        Assert.Equal(2f, rig.Hud.ResetSeconds(Blue), 3);
        rig.Engine.Returns("Clock.Now", 50f);
        Assert.Equal(0f, rig.Hud.ResetSeconds(Blue), 3);
    }

    [Fact]
    public void The_in_eye_target_carrying_the_flag_is_the_no_arrow_disc()
    {
        var rig = new FlagRig();
        rig.Spectate(7);   // in-eye of entity 7
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = Stolen;
            s.FlagCarrier[Blue] = 7;
            s.HasFlag[Red] = true;   // RED's stays home: no carrier to watch
        });

        Assert.True(rig.Hud.SpectatingCarrier(Blue));
        Assert.False(rig.Hud.SpectatingCarrier(Red));
    }

    [Fact]
    public void An_in_eye_target_without_the_flag_keeps_the_arrow()
    {
        var rig = new FlagRig();
        rig.Spectate(9);   // a bystander, not the carrier
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = Stolen;
            s.FlagCarrier[Blue] = 7;
        });

        Assert.False(rig.Hud.SpectatingCarrier(Blue));
    }

    [Fact]
    public void A_chase_spectator_keeps_the_arrow()
    {
        var rig = new FlagRig();
        rig.Spectate(7, ClientAbi.GameMovement.ObsModeChase);
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = Stolen;
            s.FlagCarrier[Blue] = 7;
        });

        Assert.False(rig.Hud.SpectatingCarrier(Blue));
    }

    [Fact]
    public void A_playing_player_keeps_the_arrow()
    {
        var rig = new FlagRig();
        rig.Rig.Arrive(s =>
        {
            s.HasFlag[Blue] = true;
            s.FlagStatus[Blue] = Stolen;
            s.FlagCarrier[Blue] = 7;
        });

        Assert.False(rig.Hud.SpectatingCarrier(Blue));   // no observer mode at all
    }
}
