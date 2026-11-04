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
/// display gates, the name (the disguise's team for the hidden enemy), the
/// bearer — the local player's viewmodel, another player's carried gun —
/// the frame's retarget, the active weapon's <c>no_crit_boost</c>, and the
/// clear.
/// </summary>
public sealed class TFCritBoostEffectTests
{
    private static readonly EntityHandle Player = new(0x0002_0002u);

    private static readonly EntityHandle Attachment = new(0x0006_0001u);

    private static readonly EntityHandle CarriedGun = new(0x0007_0007u);

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
    public void The_local_glow_wears_the_viewmodel_bearer()
    {
        var rig = Make(team: TFTeam.Red, localIndex: 2);
        rig.Bearer.Handle = Attachment;

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        var start = rig.LastStart();
        Assert.Equal(Attachment, start?.ArgAs<EntityHandle>(0));
        Assert.Equal(Crit.GlowSlot, start?.Args[2]);
    }

    [Fact]
    public void The_local_glow_without_an_attachment_wears_the_carried_gun()
    {
        var rig = Make(team: TFTeam.Red, localIndex: 2);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        // No attachment networked: the bearer falls to the carried gun, the
        // same fall the flame's bearer makes.
        Assert.Equal(CarriedGun, rig.LastStart()?.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void Another_players_glow_wears_their_carried_gun()
    {
        var rig = Make(team: TFTeam.Red);

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        var start = Assert.Single(rig.Started());
        Assert.Equal(CarriedGun, start.ArgAs<EntityHandle>(0));
        Assert.Equal(Crit.GlowSlot, start.Args[2]);
    }

    [Fact]
    public void A_replaced_bearer_moves_the_glow()
    {
        var rig = Make(team: TFTeam.Red, localIndex: 2);
        rig.Bearer.Handle = Attachment;

        rig.Arm.OnAdded(Player, Cond.Critboosted);
        Assert.Equal(Attachment, rig.LastStart()?.ArgAs<EntityHandle>(0));

        // The attachment is gone; the next frame's bearer is the gun.
        rig.Bearer.Handle = null;
        rig.Arm.ClientThink(0f);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopSlot");
        Assert.Equal(Attachment, stop.ArgAs<EntityHandle>(0));
        Assert.Equal(CarriedGun, rig.LastStart()?.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_gun_with_no_crit_boost_wears_nothing()
    {
        var container = new client::TF2.Shared.Econ.AttributeContainer(null, client::TF2.Shared.Econ.AttributeProviderType.Weapon);
        container.Set(client::TF2.Shared.Econ.AttributeValue.OfFloat(NoCritBoost(), 1f));
        var rig = Make(team: TFTeam.Red);
        rig.Owners.Item = container;

        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member.StartsWith("Effects.ParticleSystem"));
        Assert.Empty(rig.Envelopes.Created);
    }

    [Fact]
    public void A_gun_without_the_attribute_read_boosts_plainly()
    {
        var rig = Make(team: TFTeam.Red);

        // No item behind the active weapon: stock's unread zero, boostable.
        rig.Arm.OnAdded(Player, Cond.Critboosted);

        Assert.NotNull(rig.LastStart());
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
        Assert.Single(rig.Started());

        // Stealth arrives on its own decode: stock's stealthed add is no
        // OnAddCritBoost, so the glow waits for the next crit change.
        ClientWords.SetConditions(rig.Carrier.State, Mask.Of(Cond.Stealthed));
        rig.Arm.OnRemoved(Player, Cond.Critboosted);

        var stop = Assert.Single(rig.Engine.Calls, c => c.Member == "Effects.ParticleSystemStopSlot");
        Assert.Equal(CarriedGun, stop.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_player_with_no_carrier_is_skipped()
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", true);
        engine.Returns("Hud.LocalPlayerIndex", 4);
        var envelopes = new EnvelopeSpy();
        var arm = new Crit(engine, engine, new Shells(), new ClientCarriers(), new PlayerResources(),
                           new BearerFake(), new OwnersFake(), client::SourceSharp.Shared.WeaponCatalog.Stock(),
                           new client::TF2.Shared.Econ.AttributeHooks(), engine, envelopes);

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

    /// <summary>The schema's <c>no_crit_boost</c>, defindex 288 additive.</summary>
    private static client::TF2.Shared.Econ.AttributeDefinition NoCritBoost()
        => new(288, "no crit boost", "no_crit_boost", client::TF2.Shared.Econ.AttributeDescriptionFormat.Additive,
               client::TF2.Shared.Econ.AttributeEffectType.Negative, false, false,
               client::TF2.Shared.Econ.AttributeValueType.Default, client::TF2.Shared.Econ.MergedKeys.Block("x"));


    /// <summary>A carrier for player 2, its team in the scoreboard; the local player is entity 4 unless said.</summary>
    private static Rig Make(TFTeam team, TFTeam localTeam = TFTeam.Red,
                           TFTeam disguiseTeam = TFTeam.Red, Mask? conds = null, int localIndex = 4)
    {
        var engine = new ClientEffects();
        engine.Returns("Effects.ParticleSystemStartSlot", true);
        engine.Returns("Hud.LocalPlayerIndex", localIndex);

        var resources = new PlayerResources();
        var resource = new client::TF2.Client.Player.ClientTFPlayerResource(engine, resources);
        resource.State.BindLocal();
        resource.OnSpawn();
        resource.State.Team[2] = (byte)team;
        resource.State.Team[4] = (byte)localTeam;

        var log = new client::SourceSharp.Abi.RecordingLog();
        var classes = new client::TF2.Shared.TFClassTable(log, new ClientFiles(TFClassTableTests.Files.Shipped()));

        var shells = new Shells();
        var player = new client::SourceSharp.Client.Standalone.ClientSsPlayer(engine);
        player.BindIndex(2);
        player.Handle = Player;
        shells.Entities.Add(player);

        var gun = new client::SourceSharp.Client.Standalone.ClientSsWeapon(engine);
        gun.BindIndex(7);
        gun.Handle = CarriedGun;
        gun.State.BindLocal();
        gun.State.OwnerIndex = 2;
        shells.Entities.Add(gun);

        var carrier = new ClientTFPlayerData(engine, new ClientCarriers(),
                                             shells, resources, engine, classes,
                                             new ClientDriver(new NoHooks()), log);
        carrier.State.BindLocal();
        carrier.State.OwnerIndex = 2;
        carrier.State.DisguiseTeam = (int)disguiseTeam;
        ClientWords.SetConditions(carrier.State, conds ?? Mask.Of(Cond.Critboosted));

        var carriers = new ClientCarriers();
        carriers.Track(carrier);

        var envelopes = new EnvelopeSpy();
        engine.Returns("Sound.SoundPatchCreate", 77);

        var owners = new OwnersFake();
        var bearer = new BearerFake();

        return new Rig(engine, carrier,
                       new Crit(engine, engine, shells, carriers, resources, bearer, owners,
                                client::SourceSharp.Shared.WeaponCatalog.Stock(),
                                new client::TF2.Shared.Econ.AttributeHooks(), engine, envelopes),
                       envelopes, bearer, owners);
    }

    private sealed record Rig(ClientEffects Engine, ClientTFPlayerData Carrier, Crit Arm, EnvelopeSpy Envelopes,
                              BearerFake Bearer, OwnersFake Owners)
    {
        public client::SourceSharp.Abi.EngineCall? LastStart()
        {
            var all = Started();

            return all.Count > 0 ? all[^1] : null;
        }

        public string? LastStartName() => LastStart()?.ArgAs<string>(1);

        public List<client::SourceSharp.Abi.EngineCall> Started()
            => Engine.Calls.Where(c => c.Member == "Effects.ParticleSystemStartSlot").ToList();
    }

    /// <summary>The local player's viewmodel bearer, whose answer a fact aims.</summary>
    private sealed class BearerFake : client::SourceSharp.Client.WeaponEffects.IWeaponViewModelBearer
    {
        public EntityHandle? Handle;

        public EntityHandle? ViewModelBearer() => Handle;
    }

    /// <summary>The weapon owners' seam, whose item a fact aims.</summary>
    private sealed class OwnersFake : client::TF2.Shared.ITFWeaponOwners
    {
        public client::TF2.Shared.Econ.IAttributeProvider? Item { get; set; }

        public client::TF2.Shared.Econ.IAttributeProvider? Active { get; set; }

        public client::TF2.Shared.TFClass ClassOf(int playerIndex) => client::TF2.Shared.TFClass.Undefined;

        public client::TF2.Shared.Conditions.TFConditions? ConditionsOf(int playerIndex) => null;

        public float CritMult(int playerIndex) => 1f;

        public bool WinsCrits(int playerIndex) => false;

        public client::TF2.Shared.Econ.IAttributeProvider? ItemOf(int playerIndex, string weaponName) => Item;

        public client::TF2.Shared.Econ.IAttributeProvider? ActiveItemOf(int playerIndex) => Active;

        public client::TF2.Shared.Econ.IAttributeProvider? ManagerOf(int playerIndex) => null;

        public bool OffGround(int playerIndex) => false;

        public bool EyesUnderwater(int playerIndex) => false;

        public bool DuckingOf(int playerIndex) => false;
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

    /// <summary>One player shell at entity index 2 and its carried gun at 7.</summary>
    private sealed class Shells : client::SourceSharp.Client.IClientStandalones
    {
        public readonly List<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Entities = [];

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => Entities;

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
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
