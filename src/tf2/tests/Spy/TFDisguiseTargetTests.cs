extern alias client;

using System.Reflection;

using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using TF2.Tests.Weapons;

using Xunit;

using ClientTFPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCond = client::TF2.Shared.TFCond;
using ClientState = client::TF2.Shared.Player.TFPlayerNetState;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;

namespace TF2.Tests.Spy;

/// <summary>
/// The disguise's target and fake health on the carrier
/// (<c>m_hDisguiseTarget</c>, <c>m_iDisguiseHealth</c>,
/// tf_player_shared.cpp:581-582), the server's search for a target
/// (<c>TeamFortress_GetDisguiseTarget</c>, tf_player.cpp:8601-8656) and the
/// health they fake (CompleteDisguise, tf_player_shared.cpp:8321-8334).
/// </summary>
public sealed class TFDisguiseTargetTests
{
    /// <summary>
    /// Stock sends the target and the health and predicts neither
    /// (tf_player_shared.cpp:581-582, against the prediction block at
    /// :447-473): the server's word, news to the client — CritMult's rule.
    /// </summary>
    [Fact]
    public void The_disguise_target_and_health_ride_sent_not_predicted()
    {
        Assert.Null(typeof(TFPlayerNetState).GetProperty("DisguiseTargetIndex")
                        ?.GetCustomAttribute<PredictedAttribute>());
        Assert.Null(typeof(TFPlayerNetState).GetProperty("DisguiseHealth")
                        ?.GetCustomAttribute<PredictedAttribute>());
    }

    /// <summary>The completion names the blue heavy and borrows his health
    /// (tf_player.cpp:8618-8635, tf_player_shared.cpp:8323).</summary>
    [Fact]
    public void The_completion_draws_a_target_on_the_disguise_team_and_their_health()
    {
        var rig = new SpyRig();

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);

        Assert.Equal(SpyRig.Victim.EntryIndex, rig.State.DisguiseTargetIndex);
        Assert.Equal(100, rig.State.DisguiseHealth);
    }

    /// <summary>A dead target answers the disguise class's max
    /// (tf_player_shared.cpp:8324-8328, GetDisguiseMaxHealth :8358-8369) —
    /// the CLASS's row, not the spy's own.</summary>
    [Fact]
    public void A_dead_target_rolls_the_disguise_classs_max_health()
    {
        var rig = new SpyRig { VictimAlive = false };

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);

        Assert.Equal(SpyRig.Victim.EntryIndex, rig.State.DisguiseTargetIndex);
        Assert.Equal(rig.Classes[TFClass.HeavyWeapons].MaxHealth, rig.State.DisguiseHealth);
        Assert.NotEqual(rig.Classes[TFClass.Spy].MaxHealth, rig.State.DisguiseHealth);
    }

    /// <summary>With nobody on the disguise team, a half-to-full roll of the
    /// SPY's own max (tf_player_shared.cpp:8330-8334) and no target.</summary>
    [Fact]
    public void No_target_rolls_half_to_full_of_the_spys_own_max()
    {
        var rig = new SpyRig { VictimTeam = (int)TFTeam.Red };
        rig.Random.Int = 999;    // the clamp pins the roll to the range's top

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);

        Assert.Equal(-1, rig.State.DisguiseTargetIndex);
        Assert.Equal(rig.Classes[TFClass.Spy].MaxHealth, rig.State.DisguiseHealth);
    }

    /// <summary>The re-draw does not pick the last target again
    /// (tf_player.cpp:8611-8621): two blue heavies, and the disguise moves
    /// off the one it had.</summary>
    [Fact]
    public void The_re_disguise_does_not_redraw_the_last_target()
    {
        var rig = new SpyRig();
        rig.AddPlayer(5, TFClass.HeavyWeapons, (int)TFTeam.Blue);

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);
        int first = rig.State.DisguiseTargetIndex;
        Assert.Contains(first, new[] { SpyRig.Victim.EntryIndex, 5 });

        // A second disguise (quick, still blue heavies): the first target is
        // out of pass 1, so the draw lands on the other one.
        Assert.True(rig.Slice.Disguise(103f, TFClass.Soldier, TFTeam.Blue));
        rig.Slice.Tick(103.6f);
        Assert.True(rig.Slice.Disguise(104f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(104.6f);

        Assert.Equal(first == SpyRig.Victim.EntryIndex ? 5 : SpyRig.Victim.EntryIndex,
                     rig.State.DisguiseTargetIndex);
    }

    /// <summary>The server keeps the spy's own model through the disguise
    /// and its end (stock's server re-asserts the spy model and leaves the
    /// body to the client's per-viewer ValidateModelIndex,
    /// c_tf_player.cpp:8988-9040): no SetModel crosses, or the hitboxes
    /// would move and the spy's team-mates would see an enemy.</summary>
    [Fact]
    public void The_server_never_wears_the_disguise_body()
    {
        var rig = new SpyRig();

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        rig.Slice.Tick(102.1f);
        rig.Slice.RemoveDisguise();

        Assert.DoesNotContain(rig.Engine.Calls,
                               c => c.Member == "EntityRender.SetModel" && c.Args[0] is EntityHandle h && h == SpyRig.Spy);
    }

    /// <summary>
    /// Stock's IsEnemyPlayer (c_tf_player.cpp:5384-5412): the other team, and
    /// a spectator or unassigned local is nobody's enemy.
    /// </summary>
    [Fact]
    public void An_enemy_viewer_is_one_of_the_other_team_and_a_spectator_is_nobodys()
    {
        Assert.True(ClientTFPlayerData.IsEnemyViewer((int)TFTeam.Red, (int)TFTeam.Blue));
        Assert.True(ClientTFPlayerData.IsEnemyViewer((int)TFTeam.Blue, (int)TFTeam.Red));
        Assert.False(ClientTFPlayerData.IsEnemyViewer((int)TFTeam.Red, (int)TFTeam.Red));
        Assert.False(ClientTFPlayerData.IsEnemyViewer(0, (int)TFTeam.Red));
        Assert.False(ClientTFPlayerData.IsEnemyViewer((int)TFTeam.Red, 0));
    }

    /// <summary>
    /// The client dresses the body per viewer (c_tf_player.cpp:7802-7805 over
    /// IsEnemyPlayer): an enemy of a disguised carrier sees the disguise
    /// team's base skin; a team-mate (and the carrier) sees the real team's;
    /// the disguise's end undresses.
    /// </summary>
    [Fact]
    public void The_decode_dresses_an_enemys_view_of_the_skin_and_the_end_undresses()
    {
        // Local RED watching a RED spy disguised as BLUE: a team-mate sees RED.
        Assert.Equal(0, ClientTFPlayerData.SkinFor(DisguisedWord(ClientCond.Disguised, (int)TFTeam.Blue), (int)TFTeam.Red, (int)TFTeam.Red));
        // Local BLUE watching the same spy: an enemy sees the disguise's BLUE.
        Assert.Equal(1, ClientTFPlayerData.SkinFor(DisguisedWord(ClientCond.Disguised, (int)TFTeam.Blue), (int)TFTeam.Red, (int)TFTeam.Blue));
        // A spectator sees the real team's.
        Assert.Equal(0, ClientTFPlayerData.SkinFor(DisguisedWord(ClientCond.Disguised, (int)TFTeam.Blue), (int)TFTeam.Red, 0));
        // Disguising (not yet TF_COND_DISGUISED) dresses nobody.
        Assert.Equal(0, ClientTFPlayerData.SkinFor(DisguisedWord(ClientCond.Disguising, (int)TFTeam.Blue), (int)TFTeam.Red, (int)TFTeam.Blue));
        // The end: the real team's skin again.
        Assert.Equal(1, ClientTFPlayerData.SkinFor(NoDisguiseWord(), (int)TFTeam.Blue, (int)TFTeam.Red));
        Assert.Null(ClientTFPlayerData.SkinFor(NoDisguiseWord(), 0, (int)TFTeam.Red));
    }

    /// <summary>
    /// The body follows the same rule (ValidateModelIndex,
    /// c_tf_player.cpp:8999-9003): an enemy of a disguised carrier draws the
    /// disguise class's body; a team-mate draws the spy's own (null, the
    /// override cleared); the disguise's end restores the spy's own for the
    /// enemy too.
    /// </summary>
    [Fact]
    public void The_decode_draws_the_disguise_body_for_an_enemy_and_the_end_restores_the_spy()
    {
        const int red = (int)TFTeam.Red, blue = (int)TFTeam.Blue;

        // The enemy draws the heavy's body; the team-mate sees the spy's own.
        Assert.Equal(client::TF2.Shared.TFClass.HeavyWeapons,
                     ClientTFPlayerData.BodyClassFor(DisguisedWord(ClientCond.Disguised, blue, client::TF2.Shared.TFClass.HeavyWeapons), red, blue));
        Assert.Null(ClientTFPlayerData.BodyClassFor(DisguisedWord(ClientCond.Disguised, blue, client::TF2.Shared.TFClass.HeavyWeapons), red, red));
        // The disguise's end restores the spy's own model for the enemy too.
        Assert.Null(ClientTFPlayerData.BodyClassFor(NoDisguiseWord(), red, blue));
    }

    private static ClientState DisguisedWord(ClientCond cond, int disguiseTeam,
                                             client::TF2.Shared.TFClass disguiseClass = client::TF2.Shared.TFClass.Scout)
    {
        var state = new ClientState();
        state.BindLocal();
        state.DisguiseTeam = disguiseTeam;
        state.DisguiseClass = (int)disguiseClass;
        ClientWords.SetConditions(state, client::TF2.Shared.Conditions.TFCondMask.Of(cond));
        return state;
    }

    private static ClientState NoDisguiseWord()
    {
        var state = new ClientState();
        state.BindLocal();
        return state;
    }

    /// <summary>
    /// An enemy viewer's decode dresses the body and says so: the override
    /// write carries the disguise class's model and the client console line
    /// names it. (The live solo boot never sees this line — a spy viewing
    /// himself is not an enemy, so his decision never moves — which is the
    /// fix: the boot's thirdperson jpeg shows the spy's own body.)
    /// </summary>
    [Fact]
    public void An_enemy_viewers_decode_draws_the_disguise_and_says_so()
    {
        var engine = new client::SourceSharp.Abi.RecordingEngine();
        engine.Returns("Hud.LocalPlayerIndex", 2);   // the local player's slot: BLUE, an enemy of the RED spy

        var resources = new client::TF2.Client.Player.TFClientPlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[2] = (int)TFTeam.Blue;
        resource.State.Team[3] = (int)TFTeam.Red;

        var standalones = new OneShell(WeaponFixtures.ShippedWithClasses());
        var shell = standalones.Player(3);

        var log = new client::SourceSharp.Abi.RecordingLog();
        var classes = new client::TF2.Shared.TFClassTable(log, new ClientFiles(WeaponFixtures.ShippedWithClasses()));

        var data = new ClientTFPlayerData(engine, new client::TF2.Client.Player.TFClientCarriers(),
                                          standalones, resources, engine, classes,
                                          new client::TF2.Client.Player.TFClientConditionDriver(NoHooks()), log);
        data.State.BindLocal();
        data.State.OwnerIndex = 3;
        data.State.DisguiseTeam = (int)TFTeam.Blue;
        data.State.DisguiseClass = (int)client::TF2.Shared.TFClass.HeavyWeapons;
        ClientWords.SetConditions(data.State, client::TF2.Shared.Conditions.TFCondMask.Of(client::TF2.Shared.TFCond.Disguised));

        data.OnDataChanged(default, SourceSharp.Abi.DataUpdateType.Created);

        var write = engine.Calls.Single(c => c.Member == "EntityRender.SetModelOverride");
        Assert.Equal(shell.Handle, write.Args[0]);
        Assert.Equal("models/player/heavy.mdl", write.Args[1]);
        Assert.Contains(log.Lines, l => l.Text.Contains("[TF2] disguise: player 3 drawn as models/player/heavy.mdl"));
    }

    /// <summary>The shipped scripts, as the client's own file seam sees them.</summary>
    private sealed class ClientFiles(TFClassTableTests.Files files) : client::SourceSharp.Abi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>A condition chain with no members: the decode's sync has nothing to tell.</summary>
    private static client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook> NoHooks()
        => new EmptyConditionHooks();

    private sealed class EmptyConditionHooks : client::SourceSharp.Abi.IHooks<client::TF2.Client.Player.ITFClientConditionHook>
    {
        public IReadOnlyList<client::TF2.Client.Player.ITFClientConditionHook> All => [];

        public void Each(string what, Action<client::TF2.Client.Player.ITFClientConditionHook> call) { }
    }

    /// <summary>One player shell at entity index 3, and the shipped class
    /// scripts behind the same file seam.</summary>
    private sealed class OneShell(TFClassTableTests.Files? files = null) : client::SourceSharp.Client.IClientStandalones
    {
        private readonly client::SourceSharp.Client.Standalone.ClientSsPlayer _player =
            new(new client::SourceSharp.Abi.RecordingEngine());

        private readonly TFClassTableTests.Files _files = files ?? new TFClassTableTests.Files();

        public client::SourceSharp.Client.Standalone.ClientSsPlayer Player(int index)
        {
            _player.BindIndex(index);
            _player.Handle = new EntityHandle(0x0002_0003u);
            return _player;
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [_player];
        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _player.Index == entityIndex ? _player : null;
        public int GameFileSize(string path) => _files.GameFileSize(path);
        public string? ReadGameFile(string path) => _files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => _files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => _files.ReadFileOnPath(path, pathId);
    }
}
