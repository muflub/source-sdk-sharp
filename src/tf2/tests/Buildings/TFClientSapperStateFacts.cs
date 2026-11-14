extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientPlayer = client::SourceSharp.Client.Standalone.ClientSsPlayer;
using ClientDirectory = client::SourceSharp.Client.Standalone.ClientEntityDirectory;
using ClientStandalones = client::SourceSharp.Client.IClientStandalones;
using ClientStandalone = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ClientSentry = client::TF2.Client.Buildings.ClientTFSentryGun;
using ClientSapper = client::TF2.Client.Buildings.TFClientSapperState;
using ClientResources = client::TF2.Client.Player.TFClientPlayerResources;
using ClientResource = client::TF2.Client.Player.ClientTFPlayerResource;

namespace TF2.Tests.Buildings;

/// <summary>
/// The replay's half of the wrench's building hit
/// (<see cref="ClientSapper"/>): stock's <c>CTFWrench::Smack</c> traces on BOTH
/// sides (tf_weapon_wrench.cpp:145-199), so the client's replay of the swing
/// asks the struck building's team itself — the friendly one sets the skip bit,
/// an enemy one does not (the <c>OnFriendlyBuildingHit</c> test, :180, reads
/// the team the server's <c>StartPlacement</c> published).
/// </summary>
public sealed class TFClientSapperStateFacts
{
    private const int Owner = 1;
    private const int BuildingIndex = 4;
    private const int BlueTeam = 2;
    private const int RedTeam = 3;
    private const float Swing = 70f;   // SmackRange, tf_weapon_wrench.cpp:165
    private static readonly EntityHandle OwnerHandle = new(0x0001_0000u | Owner);
    private static readonly EntityHandle BuildingHandle = new(0x0001_0000u | BuildingIndex);
    private static readonly Vector Eye = new(0f, 0f, 64f);
    private static readonly Vector AimHit = new(70f, 0f, 64f);

    /// <summary>
    /// The swing's line stopping on a building of the owner's own team is the
    /// friendly hit: the replay's answer is <c>Useful</c>, the one word that
    /// sets the skip bit both sides agree on.
    /// </summary>
    [Fact]
    public void The_replay_calls_a_struck_building_of_the_owners_team_useful()
    {
        var rig = new Rig(team: BlueTeam);

        Assert.Equal(client::TF2.Shared.WrenchSmackResult.Useful, rig.Sapper.WrenchSmack(Owner, Swing));
    }

    /// <summary>
    /// The same swing on a building of the OTHER team is no friendly hit at
    /// all (<c>OnFriendlyBuildingHit</c>'s team test): the replay answers
    /// <c>None</c> and the plain melee runs.
    /// </summary>
    [Fact]
    public void A_struck_building_of_another_team_is_no_friendly_hit()
    {
        var rig = new Rig(team: RedTeam);

        Assert.Equal(client::TF2.Shared.WrenchSmackResult.None, rig.Sapper.WrenchSmack(Owner, Swing));
    }

    /// <summary>
    /// A swing whose thin line and thin entity clip stop on nothing is still
    /// the building's when the ±18 hull (stock's sweep, tf_weapon_wrench.cpp:
    /// 170-186) names it: the replay answers <c>Useful</c> from the hull's
    /// blocker, so the skip bit rides the swing's own command on both sides.
    /// Without the hull arm the replay answers <c>None</c> a command behind
    /// the server's own word — the boot's <c>BehaviourMode=256|8448</c> pair.
    /// </summary>
    [Fact]
    public void The_hull_sweep_names_the_building_when_the_thin_line_stops_on_nothing()
    {
        var rig = new Rig(team: BlueTeam, thinLineStops: false);

        Assert.Equal(client::TF2.Shared.WrenchSmackResult.Useful, rig.Sapper.WrenchSmack(Owner, Swing));
    }

    /// <summary>The arsenal's pair, as the client's registry files it: both the
    /// owner and the building findable by their engine indices.</summary>
    private sealed class Pair : ClientStandalones
    {
        public readonly ClientDirectory Directory = new();

        public IReadOnlyList<ClientStandalone> Live()
            => throw new InvalidOperationException("the replay finds its pair by index");

        public ClientStandalone? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);
        public IEnumerable<ClientStandalone> OfClass(string? className) => Directory.OfClass(className);
        public IEnumerable<ClientStandalone> OfName(string? name) => Directory.OfName(name);
    }

    /// <summary>The replay's side: an owner facing +X, its line stopping on the building.</summary>
    private sealed class Rig
    {
        public readonly ClientEngine Engine = new();
        public readonly ClientSapper Sapper;

        public Rig(byte team, bool thinLineStops = true)
        {
            // Every aim line stops on entity 4 — the swing's reach answered
            // the same by the world's line and the entity clip, as the
            // building is solid to the replay's trace. With the thin passes
            // held back, the ±18 hull still names the building (stock's
            // sweep order, tf_weapon_wrench.cpp:170-186).
            Engine.Returns("WorldQuery.ClientTraceLine", thinLineStops ? Hit : Miss);
            Engine.Returns("WorldQuery.ClientClipRayToEntities", thinLineStops ? Hit : Miss);
            Engine.Returns("WorldQuery.ClientTraceHullEntities", Hull);
            Engine.Returns("Entities.GetAngles", new Vector(0f, 90f, 0f));   // the command's yaw

            var owner = new ClientPlayer(Engine) { Handle = OwnerHandle };
            owner.BindEngine(Engine, Engine);   // the registry's bind, as the replay sees it
            owner.State.BindLocal();
            owner.State.Origin = Eye;

            var building = new ClientSentry(Engine) { Handle = BuildingHandle };
            building.State.BindLocal();
            building.State.Team = team;   // the word the server's StartPlacement publishes

            var pair = new Pair();
            pair.Directory.Bind(owner.Handle, Owner, owner);
            pair.Directory.Bind(building.Handle, BuildingIndex, building);

            var resources = new ClientResources();
            var resource = new ClientResource(Engine, resources);
            resource.State.BindLocal();
            resource.State.Team[Owner] = BlueTeam;   // the engineer's own team, off the scoreboard
            resources.Live = resource;

            Sapper = new ClientSapper(Engine, pair, resources);
        }

        private static ClientTraceResult Hit =>
            new(true, 0.5f, AimHit, Vector.Zero, 0, BuildingIndex, 0, 0);

        private static ClientTraceResult Miss =>
            new(false, 1f, Vector.Zero, Vector.Zero, 0, 0, 0, 0);

        // The swept box blocked on the building: stock's hull pass names it when
        // the thin line stops on nothing.
        private static ClientBoxTrace Hull =>
            new(true, 1f, Vector.Zero, BuildingIndex, Vector.Zero);
    }
}
