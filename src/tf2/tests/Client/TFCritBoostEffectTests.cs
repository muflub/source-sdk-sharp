extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientEffects = client::SourceSharp.Abi.RecordingEngine;
using Crit = client::TF2.Client.Player.TFCritBoostEffect;
using ClientTFPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCarriers = client::TF2.Client.Player.TFClientCarriers;
using PlayerResources = client::TF2.Client.Player.TFClientPlayerResources;
using ClientDriver = client::TF2.Client.Player.TFClientConditionDriver;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Mask = client::TF2.Shared.Conditions.TFCondMask;
using Cond = client::TF2.Shared.TFCond;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The crit boost's glow, stock's <c>UpdateCritBoostEffect</c>
/// (tf_player_shared.cpp:7501-7613) through the condition driver: the
/// display gates, the name (the disguise's team for the hidden enemy), and
/// the clear.
/// </summary>
public sealed class TFCritBoostEffectTests
{
    private static readonly EntityHandle Player = new(0x0002_0002u);

    [Fact]
    public void A_critted_red_player_glows_red()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Equal("critgun_weaponmodel_red", rig.LastStartName());
    }

    [Fact]
    public void A_critted_blue_player_glows_blu()
    {
        var rig = Make(team: TFTeam.Blue);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Equal("critgun_weaponmodel_blu", rig.LastStartName());
    }

    [Fact]
    public void The_demo_charges_crit_reaches_the_arm_too()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.CritboostedDemoCharge);

        Assert.Equal("critgun_weaponmodel_red", rig.LastStartName());
    }

    [Fact]
    public void A_disguised_nonlocals_glow_wears_nothing()
    {
        var rig = Make(team: TFTeam.Red, localTeam: TFTeam.Blue, disguiseTeam: TFTeam.Blue,
                      conds: Mask.Of([Cond.Critboosted, Cond.Disguised]));

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Null(rig.LastStartName());
    }

    [Fact]
    public void The_local_players_disguised_glow_wears_the_real_team()
    {
        var rig = Make(team: TFTeam.Red, localTeam: TFTeam.Blue, disguiseTeam: TFTeam.Blue,
                      conds: Mask.Of([Cond.Critboosted, Cond.Disguised]),
                      localIndex: 2);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Equal("critgun_weaponmodel_red", rig.LastStartName());
    }

    [Fact]
    public void A_blipped_enemy_wears_nothing()
    {
        var rig = Make(team: TFTeam.Red, localTeam: TFTeam.Blue,
                      conds: Mask.Of([Cond.Critboosted, Cond.StealthedBlink]));

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Null(rig.LastStartName());
    }

    [Fact]
    public void A_blinking_teammate_still_glows()
    {
        var rig = Make(team: TFTeam.Red, localTeam: TFTeam.Red,
                      conds: Mask.Of([Cond.Critboosted, Cond.StealthedBlink]));

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.Equal("critgun_weaponmodel_red", rig.LastStartName());
    }

    [Fact]
    public void A_condition_outside_the_reevaluation_starts_nothing()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Burning);
        rig.Arm.OnRemoved(Player, Cond.Radiusheal);

        Assert.Null(rig.LastStartName());
    }

    [Fact]
    public void The_glow_is_cleared_when_the_last_crit_leaves_under_stealth()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);
        Assert.Equal(1, rig.StartCount());

        // Stealth arrives on its own decode: stock's stealthed add is no
        // OnAddCritBoost, so the glow waits for the next crit change.
        ClientWords.SetConditions(rig.Carrier.State, Mask.Of(Cond.Stealthed));
        rig.Arm.OnRemoved(Player, Cond.Critboosted);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopExtra");
        Assert.Equal(Player, stop.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_player_with_no_carrier_is_skipped()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartExtra", true);
        engine.Returns("Hud.LocalPlayerIndex", 4);
        var envelopes = new EnvelopeSpy();
        var arm = new Crit(engine, engine, new ClientCarriers(), new PlayerResources(), engine, envelopes);

        arm.OnAdded(Player, Cond.Critboosted);

        Assert.Equal(0, engine.Calls.Count(c => c.Member.StartsWith("Effects.ParticleSystem")));
    }

    [Fact]
    public void The_boosts_hum_is_a_patch_on_the_player_at_full_volume()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        var create = Assert.Single(rig.Engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
        Assert.Equal(Player, create.ArgAs<EntityHandle>(0));
        Assert.Equal("Weapon_General.CritPower", create.ArgAs<string>(2));
        Assert.Equal((77, 1f, 100f), rig.Envelopes.Played.Single());
    }

    [Fact]
    public void The_hum_is_created_once_per_boost()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);
        rig.Arm.OnAdded(Player, Cond.CritboostedOnKill);

        Assert.Equal(1, rig.Engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate"));
    }

    [Fact]
    public void The_hum_ends_with_the_boost()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        // The boost leaves on its own decode: the words move first.
        ClientWords.SetConditions(rig.Carrier.State, default);
        rig.Arm.OnRemoved(Player, Cond.Critboosted);

        Assert.Equal([77], rig.Envelopes.Destroyed);
        var free = Assert.Single(rig.Engine.Calls, c => c.Member == "Sound.SoundPatchFree");
        Assert.Equal(77, free.Args[0]);
    }

    [Fact]
    public void A_hidden_boost_hums_nothing()
    {
        var rig = Make(team: TFTeam.Red, conds: Mask.Of([Cond.Critboosted, Cond.StealthedBlink]),
                       localTeam: TFTeam.Blue);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Sound."));
        Assert.Empty(rig.Envelopes.Created);
    }

    /// <summary>A carrier for player 2, its team in the scoreboard; the local player is entity 4 unless said.</summary>
    private static Rig Make(TFTeam team, TFTeam localTeam = TFTeam.Red,
                           TFTeam disguiseTeam = TFTeam.Red, Mask? conds = null, int localIndex = 4)
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartExtra", true);
        engine.Returns("Hud.LocalPlayerIndex", localIndex);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[2] = (byte)team;
        resource.State.Team[4] = (byte)localTeam;

        var log = new client::SourceSharp.Abi.RecordingLog();
        var classes = new client::TF2.Shared.TFClassTable(log, new ClientFiles(TFClassTableTests.Files.Shipped()));
        var carrier = new ClientTFPlayerData(engine, new ClientCarriers(),
                                             new OneShell(), resources, engine, classes,
                                             new ClientDriver(new NoHooks()), log);
        carrier.State.BindLocal();
        carrier.State.OwnerIndex = 2;
        carrier.State.DisguiseTeam = (int)disguiseTeam;
        ClientWords.SetConditions(carrier.State, conds ?? Mask.Of(Cond.Critboosted));

        var carriers = new ClientCarriers();
        carriers.Track(carrier);

        var envelopes = new EnvelopeSpy();
        engine.Returns("Sound.SoundPatchCreate", 77);

        return new Rig(engine, carrier,
                       new Crit(engine, engine, carriers, resources, engine, envelopes), envelopes);
    }

    private sealed record Rig(ClientEffects Engine, ClientTFPlayerData Carrier, Crit Arm, EnvelopeSpy Envelopes)
    {
        public string? LastStartName()
        {
            var starts = Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemStartExtra").ToList();

            return starts.Count == 0 ? null : starts[^1].ArgAs<string>(1);
        }

        public int StartCount() => Engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStartExtra");
    }

    /// <summary>The client's envelope controller, patch handles remembered.</summary>
    private sealed class EnvelopeSpy : client::SourceSharp.Client.Interop.IClientSoundController
    {
        public readonly List<int> Created = [], Destroyed = [];
        public readonly List<(int Patch, float Volume, float Pitch)> Played = [];

        public void Create(int patch) => Created.Add(patch);

        public void Destroy(int patch) => Destroyed.Add(patch);

        public void Play(int patch, float volume, float pitch, float startTime) => Played.Add((patch, volume, pitch));

        public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => 0f;

        public void ChangePitch(int patch, float target, float deltaTime) { }

        public void ChangeVolume(int patch, float target, float deltaTime) { }

        public float GetPitch(int patch) => 0f;

        public float GetVolume(int patch) => 0f;

        public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) { }

        public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) { }

        public void CommandClear(int patch) { }

        public void SystemReset() { }

        public void Shutdown(int patch) { }
    }

    /// <summary>One player shell at entity index 2, for the carrier's lookups.</summary>
    private sealed class OneShell : client::SourceSharp.Client.IClientStandalones
    {
        private readonly client::SourceSharp.Client.Standalone.ClientSsPlayer _player =
            new(new client::SourceSharp.Abi.RecordingEngine());

        public OneShell()
        {
            _player.BindIndex(2);
            _player.Handle = new EntityHandle(0x0002_0002u);
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [_player];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _player.Index == entityIndex ? _player : null;
    }

    /// <summary>The shipped class scripts, as the client's file seam sees them.</summary>
    private sealed class ClientFiles(TFClassTableTests.Files files) : client::SourceSharp.Abi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    /// <summary>An empty condition chain.</summary>
    private sealed class NoHooks : client::SourceSharp.Abi.IHooks<Hook>
    {
        public IReadOnlyList<Hook> All => [];

        public void Each(string what, Action<Hook> call) { }
    }
}
