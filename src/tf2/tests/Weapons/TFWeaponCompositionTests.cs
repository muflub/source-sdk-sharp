extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Ctf;
using TF2.Server.Weapons;
using TF2.Shared;

using Xunit;

using CAbi = client::SourceSharp.Abi;
using CClient = client::SourceSharp.Client;
using CShared = client::SourceSharp.Shared;
using CTF = client::TF2.Shared;

namespace TF2.Tests.Weapons;

/// <summary>
/// The TF weapon services as each side's scan registers them (the
/// <c>[SideService]</c> marks and the hook registrar), composed over the
/// shipped scripts: the rows, the behaviours, the ammo, the carry rule and
/// the hook chain all resolve, on the server and on the client alike.
/// </summary>
public sealed class TFWeaponCompositionTests
{
    /// <summary>The connected clients as the minimal server container needs them: nobody is
    /// on, so the loadout reads no per-client state and the medigun's autoheal fails closed.</summary>
    private sealed class Nobody : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => 0;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private sealed class ClientFiles(TFClassTableTests.Files files) : CAbi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }

    private sealed class ClientStandalones : CClient.IClientStandalones
    {
        public IReadOnlyList<CClient.Standalone.ClientStandaloneEntity> Live() => [];
        public CClient.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static ServiceContainer Server()
    {
        var c = new ServiceContainer();
        c.AddInstance<ILog>(new RecordingLog());
        c.AddInstance<IEngineFiles>(WeaponFixtures.ShippedWithClasses());
        c.AddInstance(new TFWeaponCriticalsConVar());
        c.AddInstance(new TFWeaponCriticalsMeleeConVar());
        c.AddInstance(new TFWeaponCriticalsBucketCapConVar());
        c.AddInstance(new TFWeaponCriticalsBucketBottomConVar());
        c.AddInstance(new TFWeaponCriticalsBucketDefaultConVar());
        // The carrier's owners (2.3) answer the weapons over the engine's players.
        var engine = TestServices.Fake();
        c.AddInstance<IEnginePlayers>(engine);
        // The knife hook asks the spy slice (2.12); its deps ride beside the
        // weapon services so the chain composes here as it does in the game.
        c.AddInstance<IEngineEntities>(engine);
        c.AddInstance<IEngineWorldQuery>(engine);
        // The spy slice's disguise body (2.12) wears the class model through the render seam.
        c.AddInstance<IEngineEntityRender>(engine);
        // The owners' ground answer reads the player entities through the directory.
        c.AddInstance<IEntityDirectory>(new TF2.Tests.Combat.NoEntities());
        c.AddInstance<IEngineDamage>(engine);
        // ITFPlayerData and ITFPlayerConditions come from TFPlayerCarriers and
        // TFPlayerConditions, the scan's own registrations.
        c.AddInstance(new SourceSharp.Server.DamageHitGroups());
        c.AddInstance<SourceSharp.Abi.IUniformRandom>(new TF2.Tests.Combat.BandRandom());
        c.AddInstance<SourceSharp.Abi.IGameClock>(engine);
        c.AddInstance<SourceSharp.Abi.IEngineEnvironment>(engine);
        c.AddInstance(new TF2.Shared.TFSpyCloakConsumeRateConVar());
        c.AddInstance(new TF2.Shared.TFSpyCloakRegenRateConVar());
        c.AddInstance(new TF2.Shared.TFSpyInvisTimeConVar());
        c.AddInstance(new TF2.Shared.TFSpyInvisUnstealthTimeConVar());
        c.AddInstance(new TF2.Shared.TFSpyCloakNoAttackTimeConVar());
        // The loadout's melee-only sudden death arm (2.3) reads its convars.
        c.AddInstance(new TF2.Shared.MpStalemateMeleeOnlyConVar());
        c.AddInstance(new TF2.Shared.TFStalemateChangeClassTimeConVar());
        // The projectile effects (2.8) find their spawned entities through the
        // registry; the composition here answers none.
        c.AddInstance<IEntityDirectory>(new TF2.Tests.Combat.NoEntities());
        // The medigun's owner (2.9): the healing behind the beam answers over the player
        // services, whose leaves (game events, the loadout's mod-info and connected-clients
        // lines, and the healing's three convars) join the container the same way.
        c.AddInstance<IEngineGameEvents>(engine);
        c.AddInstance<IModInfo>(new ModInfo(_ => null));
        c.AddInstance<IConnectedClients>(new Nobody());
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(TFWeaponBehaviour).Assembly);
        c.AddInstance(convars);
        c.AddInstance(new TFMaxHealthBoostConVar());
        c.AddInstance(new TFBoostDrainTimeConVar());
        c.AddInstance(new TFInvulnTimeConVar());
        // The airblast's arm (2.13): the cray gate and the old push's lift;
        // the stun stack rides the scan's own TFPlayerCarriers. The medigun's
        // build (2.13) reads its rate the same way.
        c.AddInstance(new TFAirBlastCrayConVar());
        c.AddInstance(new TFFlameThrowerBurstZvelocityConVar());
        c.AddInstance(new TFPushbackScaleConVar());
        c.AddInstance(new TFPushbackScaleVerticalConVar());
        c.AddInstance(new WeaponMedigunChargeRateConVar());
        // The airblast's underwater refusal (2.11) reads the networking state.
        c.AddInstance<IEngineNetworking>(engine);
        // The cloak-consuming disguise (2.12) emits the use-deny vo through the sound area.
        c.AddInstance<IEngineSound>(engine);
        // The minigun's ring of fire (2.3) precaches its particle and temps it out.
        c.AddInstance<IEnginePrecache>(engine);
        c.AddInstance<SourceSharp.Shared.ITempEnts>(new RecordingEffectDispatch());
        // The PDA's item switch hands the owner back through the core's
        // arsenal; the game's ServerModule registers it, so it joins the
        // container over the same engine fake (the registry spawns nothing
        // and the directory answers none).
        c.AddInstance(new SourceSharp.Server.Arsenal(new RecordingLog(), engine, engine, engine, engine,
                                                      engine, engine, engine, engine, new TF2.Tests.Combat.NoEntities(),
                                                      engine, new SourceSharp.Server.DamageHitGroups()));
        SideServices.Register(c, Side.Server, typeof(TFWeaponBehaviour).Assembly.GetTypes());
        // The healing's CTF seam answers only the flag rules; the scan's real
        // CtfRules sits on the round machine, which TeamRoundCompositionTests
        // composes — here the seam takes the fake the medigun's facts hold,
        // registered after the scan so it stands over the scan's claim.
        c.AddInstance<ICtfRules>(new TF2.Tests.Medigun.TFHealingCtf());
        // The engineer's lift (2.10) rides the skill's optional arm, and the
        // scan's round state wants the team roster, which no weapon fact
        // composes — the seam takes the fake, registered after the scan so it
        // stands over the scan's claim.
        c.AddInstance<TF2.Server.Rules.ITeamRoundState>(new TF2.Tests.Entities.FakeRoundState());
        // The buildings' purse, arms and spot refusals (2.10/2.11) read the
        // core's Arsenal and the level's trigger seams, which no weapon fact
        // composes — the seams take the fakes, registered after the scan so
        // they stand over the scan's claim.
        c.AddInstance<TF2.Server.Buildings.ITFAmmoPurse>(new TF2.Tests.Buildings.FakePurse());
        c.AddInstance<TF2.Server.Buildings.ITFBuilderArms>(new TF2.Tests.Buildings.FakeBuilderArms());
        c.AddInstance<TF2.Server.Entities.Rooms.ITFNoBuildAreas>(new TF2.Tests.Buildings.FakeNoBuildAreas());
        c.AddInstance<TF2.Server.Entities.Rooms.ITFRespawnRooms>(new TF2.Tests.Buildings.FakeRespawnRooms());
        return c;
    }

    [Fact]
    public void The_server_composes_the_tf_rows_behaviours_ammo_and_carry_rule()
    {
        var c = Server();

        var types = Assert.Single(c.ResolveAll<IWeaponTypeSource>().OfType<TFWeaponTypes>());
        Assert.Equal(32, types.Rows().Count());
        // The carrier's clip rows are keyed by this catalog's index: a wider
        // catalogue than the row count would silently drop weapons' clips.
        Assert.True(TF2.Shared.Player.TFPlayerNetState.ClipRows >= types.Rows().Count());
        var behaviours = Assert.Single(c.ResolveAll<IWeaponBehaviourSource>().OfType<TFWeaponBehaviours>());
        Assert.Equal(Enum.GetValues<TFWeaponKind>().Length, behaviours.Behaviours().Count());
        Assert.Single(c.ResolveAll<IAmmoTypeSource>().OfType<TFAmmoTypes>());
        Assert.Single(c.ResolveAll<IAmmoCarryRule>().OfType<TFAmmoCarryRule>());
        Assert.IsType<TFCritConVars>(c.Resolve(typeof(ITFCritSettings)));
        Assert.IsType<TF2.Server.Player.TFServerWeaponOwners>(((TFWeaponParts)c.Resolve(typeof(TFWeaponParts))!).Owners);
        // The PDA's trigger hands the owner back through the scan's real
        // switch over the core's arsenal: a seam with no owner on the side
        // would fail here, not on the trigger (3.5's first boot).
        Assert.IsType<TF2.Server.Weapons.TFPDAItemSwitch>(
            c.Resolve(typeof(TF2.Server.Weapons.ITFItemSwitchEffects)));
    }

    [Fact]
    public void The_server_composes_the_anim_layer_over_its_weapon_read()
    {
        // The layer's every parameter is a side service: a seam with no owner on the
        // side fails here, not 950 times a second in a live game (3.5's first boot).
        var c = Server();

        Assert.IsType<TF2.Server.Player.TFServerAnimWeapons>(c.Resolve(typeof(TF2.Shared.Anim.ITFAnimWeapons)));
        Assert.NotNull(c.Resolve(typeof(TF2.Shared.Anim.TFAnimLayer)));
    }

    [Fact]
    public void The_servers_chain_holds_tf2s_hooks_in_order()
    {
        var chain = (TFWeaponHookChain)Server().Resolve(typeof(TFWeaponHookChain))!;

        Assert.Collection(chain.All,
            h => Assert.IsType<TFAttributeWeaponHook>(h),
            h => Assert.IsType<TFCritBoostHook>(h),
            h => Assert.IsType<TFKingReloadHook>(h),
            h => Assert.IsType<TFHealedReloadHook>(h),
            h => Assert.IsType<TFKnifeHook>(h),
            h => Assert.IsType<TFWrenchHook>(h));
    }

    /// <summary>The client's weapon container: the scan over the client
    /// assembly — which carries the shared weapons and the client's own arms
    /// together — over the engine doubles the predictions read.</summary>
    private static CAbi.ServiceContainer Client()
    {
        var c = new CAbi.ServiceContainer();
        var engine = PredictionDeps.Engine();
        c.AddInstance<ILog>(new RecordingLog());
        c.AddInstance<CAbi.IEngineFiles>(new ClientFiles(WeaponFixtures.ShippedWithClasses()));
        c.AddInstance(new CTF.TFWeaponCriticalsConVar());
        c.AddInstance(new CTF.TFWeaponCriticalsMeleeConVar());
        c.AddInstance(new CTF.TFWeaponCriticalsBucketCapConVar());
        c.AddInstance(new CTF.TFWeaponCriticalsBucketBottomConVar());
        c.AddInstance(new CTF.TFWeaponCriticalsBucketDefaultConVar());
        // The carrier's owners (2.3) answer the weapons over the prediction, which judges by the catalogue.
        c.AddInstance(CAbi.NetStateCatalog.Of(typeof(CTF.TFWeaponBehaviour).Assembly, new RecordingLog()));
        // The underwater refusal (2.11) reads the standalones; the composition answers none.
        c.AddInstance<CClient.IClientStandalones>(new ClientStandalones());
        // The prediction's clock and cloak convars, as the client's composition holds them.
        c.AddInstance<CAbi.IEngineEnvironment>(engine);
        // The medigun's replayed pick asks the world and the effects; the same
        // engine double answers, as the client's areas do.
        c.AddInstance<CAbi.IEngineWorldQuery>(engine);
        c.AddInstance<CAbi.IEngineEffects>(engine);
        var cloakVars = PredictionDeps.SpyConVars();
        c.AddInstance(cloakVars.Get<CTF.TFSpyCloakConsumeRateConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyCloakRegenRateConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyInvisTimeConVar>()!);
        c.AddInstance(cloakVars.Get<CTF.TFSpyInvisUnstealthTimeConVar>()!);
        CAbi.SideServices.Register(c, Side.Client, typeof(CTF.TFWeaponBehaviour).Assembly.GetTypes());
        return c;
    }

    [Fact]
    public void The_client_composes_the_same_rows_and_behaviours()
    {
        var c = Client();

        var types = Assert.Single(c.ResolveAll<CShared.IWeaponTypeSource>().OfType<CTF.TFWeaponTypes>());
        Assert.Equal(Server().ResolveAll<IWeaponTypeSource>().OfType<TFWeaponTypes>().Single().Names(), types.Names());
        Assert.Single(c.ResolveAll<CShared.IWeaponBehaviourSource>().OfType<CTF.TFWeaponBehaviours>());
        var chain = (CTF.TFWeaponHookChain)c.Resolve(typeof(CTF.TFWeaponHookChain))!;
        Assert.Equal(5, chain.All.Count);
        Assert.IsType<CTF.TFKingReloadHook>(chain.All[2]);
        Assert.IsType<CTF.TFKnifeHook>(chain.All[3]);
        Assert.IsType<CTF.TFWrenchHook>(chain.All[4]);
        Assert.IsType<client::TF2.Client.Player.TFClientWeaponOwners>(((CTF.TFWeaponParts)c.Resolve(typeof(CTF.TFWeaponParts))!).Owners);
    }

    /// <summary>
    /// The medigun's replayed pick rides an optional constructor parameter of
    /// the parts: without the contract registration a <c>[SideService]</c>
    /// without an <c>As</c> answers null and the client's replay never runs
    /// the pick — seen live as <c>HealTargetIndex</c> mispredictions at the
    /// beam's first lock.
    /// </summary>
    [Fact]
    public void The_client_replays_the_mediguns_pick_under_its_contract()
    {
        var parts = (CTF.TFWeaponParts)Client().Resolve(typeof(CTF.TFWeaponParts))!;

        Assert.IsType<client::TF2.Client.Weapons.TFClientMedigunPrediction>(parts.MedigunPrediction);
    }

    /// <summary>
    /// The slot binds ask their forecast through <c>IWeaponSlotForecast</c>,
    /// an optional constructor parameter: without the contract registration a
    /// <c>[SideService]</c> without an <c>As</c> answers null and every slot
    /// bind falls back to the server line no client mirrors.
    /// </summary>
    [Fact]
    public void The_client_registers_the_slot_forecast_under_its_contract()
    {
        var c = new CAbi.ServiceContainer();
        var log = new CAbi.RecordingLog();
        var files = new ClientFiles(WeaponFixtures.ShippedWithClasses());
        c.AddInstance<ILog>(log);
        c.AddInstance<CAbi.IEngineFiles>(files);
        c.AddInstance<CAbi.IEngineHud>(new CAbi.RecordingEngine());   // the screen the forecast asks
        c.AddInstance(new CTF.TFWeaponCriticalsConVar());
        CAbi.SideServices.Register(c, Side.Client, typeof(CTF.TFWeaponBehaviour).Assembly.GetTypes());
        c.AddInstance(new CShared.WeaponCatalog(() => c.ResolveAll<CShared.IWeaponTypeSource>(), log));

        Assert.IsType<client::TF2.Client.Weapons.TFWeaponSlotForecast>(
            c.Resolve(typeof(CAbi.IWeaponSlotForecast)));
    }
}
