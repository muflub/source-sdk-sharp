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

    /// <summary>The bots as the server container needs them: nobody is a bot, as the
    /// engine fake's own answer; BotInventory's log names through it.</summary>
    private sealed class NoBots : SourceSharp.Server.IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "the composition fake adds no clients";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => false;

        public bool Kick(EntityHandle bot, string reason) => false;

        public IReadOnlyList<SourceSharp.Server.BotPlayer> Bots { get; } = [];
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

    /// <summary>No hands: the composition's owners ask nothing of the local arsenal.</summary>
    private sealed class NoLocalArsenal : CClient.ILocalArsenal
    {
        public bool TryGetActive(out string name, out int clip, out int reserve)
        {
            name = "";
            clip = 0;
            reserve = 0;
            return false;
        }
    }

    internal static ServiceContainer Server()
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
        // The shield charge's bash shakes the screen through the engine seam.
        c.AddInstance<SourceSharp.Server.IScreenEffects>(
            new SourceSharp.Server.ScreenEffects(engine, engine, engine, engine));
        // ITFPlayerData and ITFPlayerConditions come from TFPlayerCarriers and
        // TFPlayerConditions, the scan's own registrations.
        c.AddInstance(new SourceSharp.Server.DamageHitGroups());
        // The melee blast (the Caber's kaboom) scopes its hits through the
        // inflictor stack, as ServerModule registers it for the game.
        c.AddInstance(new SourceSharp.Server.DamageInflictors());
        // ServerModule registers the real lag compensation outside the scan;
        // the shove takes it by constructor.
        c.AddInstance<SourceSharp.Server.ILagCompensation>(new NoRewind());
        c.AddInstance<SourceSharp.Abi.IUniformRandom>(new TF2.Tests.Combat.BandRandom());
        c.AddInstance<IServerSkins>(new RecordingServerSkins());
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
        // registry; the composition here answers none. Their sticky arms (F25)
        // stamp and cap from the sticky's convars, which ServerModule
        // registers over the scan as the game's composition does.
        c.AddInstance<IEntityDirectory>(new TF2.Tests.Combat.NoEntities());
        c.AddInstance(new TF2.Shared.TFGrenadeLauncherLiveTimeConVar());
        c.AddInstance(new TF2.Shared.TFStickyRadiusRampTimeConVar());
        c.AddInstance(new TF2.Shared.TFStickyAirDetRadiusConVar());
        c.AddInstance(new TFPipebombDeflectResetTimeConVar());
        c.AddInstance(new TFGrenadeForceFromBulletConVar());
        c.AddInstance(new TFGrenadeForceFromBuckshotConVar());
        c.AddInstance(new TFGrenadeForceFromBlastConVar());
        c.AddInstance(new TFPipebombForceToMoveConVar());
        c.AddInstance(new TFGrenadeForceSleeptimeConVar());
        // The medigun's owner (2.9): the healing behind the beam answers over the player
        // services, whose leaves (game events, the loadout's mod-info and connected-clients
        // lines, and the healing's three convars) join the container the same way.
        c.AddInstance<IEngineGameEvents>(engine);
        c.AddInstance<IModInfo>(new ModInfo(_ => null));
        c.AddInstance<IConnectedClients>(new Nobody());
        // BotInventory (F19) names the bots through the seam; ServerModule registers
        // it over the engine's fake clients at runtime, so it joins here too.
        c.AddInstance<SourceSharp.Server.IBotPlayers>(new NoBots());
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(TFWeaponBehaviour).Assembly);
        // The declared convars stand in the container as the game's publish
        // does (ServerModule's PublishTo): the movement rules and the CTF
        // settings read theirs as the leaves they are.
        convars.PublishTo(c);
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
        // The feign corpse's dress (the Dead Ringer's arm) pulls the death
        // walk's gib rule, whose tf_playergib joins as the game's own.
        c.AddInstance(new TFPlayerGibConVar());
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
        var arsenal = new SourceSharp.Server.Arsenal(new RecordingLog(), engine, engine, engine, engine,
                                                     engine, engine, engine, engine, new TF2.Tests.Combat.NoEntities(),
                                                     engine, new SourceSharp.Server.DamageHitGroups());
        c.AddInstance(arsenal);
        // The owners' refire bank reads the giver by its seam, as ServerModule
        // registers it (IWeaponGiver over the same instance).
        c.AddInstance<SourceSharp.Server.IWeaponGiver>(arsenal);
        SideServices.Register(c, Side.Server, typeof(TFWeaponBehaviour).Assembly.GetTypes());
        // The disguise's dress (2.12) reads the econ triple; ServerModule
        // registers each over the scan, as the game's composition does.
        c.AddInstance(EconTestFiles.TrimmedSchema());
        c.AddInstance(new TFClassTable(new RecordingLog(), WeaponFixtures.ShippedWithClasses()));
        c.AddInstance(new SourceSharp.Shared.WeaponCatalog(() => c.ResolveAll<SourceSharp.Shared.IWeaponTypeSource>(), null));
        // The speech behind the weapon-fire concepts rides the dispatcher's
        // optional arm; its search and its roster join as the rigs' own fakes,
        // so the chain under the effects composes here as it does in the game.
        c.AddInstance<SourceSharp.Abi.IResponseRules>(new TF2.Tests.Entities.FakeConceptRules());
        c.AddInstance<SourceSharp.Server.ITeams>(new TF2.Tests.Entities.FakeConceptTeams());
        // The healing's CTF seam answers only the flag rules; the scan's real
        // CtfRules stands behind the round-machine seams below, as the game's
        // composition holds it — the composition fact holds the chain.

        // The engineer's lift (2.10) rides the skill's optional arm, and the
        // scan's round state wants the team roster, which no weapon fact
        // composes — the seam takes the fake, registered after the scan so it
        // stands over the scan's claim.
        c.AddInstance<TF2.Server.Rules.ITeamRoundState>(new TF2.Tests.Entities.FakeRoundState());
        // The flag rules' remaining seams, for the composition fact below: the
        // round machine's control half and the mode slot answer nothing here,
        // and the CTF convars stand at their declared defaults.
        c.AddInstance<TF2.Server.Rules.ITeamRoundControl>(new TF2.Tests.Entities.FakeRoundControl());
        c.AddInstance<TF2.Server.Rules.IActiveTeamRoundMode>(new TF2.Tests.Rules.ModeSlot());
        // The buildings' purse, arms and spot refusals (2.10/2.11) read the
        // core's Arsenal and the level's trigger seams, which no weapon fact
        // composes — the seams take the fakes, registered after the scan so
        // they stand over the scan's claim.
        c.AddInstance<TF2.Server.Buildings.ITFAmmoPurse>(new TF2.Tests.Buildings.FakePurse());
        c.AddInstance<TF2.Server.Buildings.ITFBuilderArms>(new TF2.Tests.Buildings.FakeBuilderArms());
        c.AddInstance<TF2.Server.Entities.Rooms.ITFNoBuildAreas>(new TF2.Tests.Buildings.FakeNoBuildAreas());
        c.AddInstance<TF2.Server.Entities.Rooms.ITFRespawnRooms>(new TF2.Tests.Buildings.FakeRespawnRooms());
        // The death drops (the ammo pack's hook, tf_player.cpp:13103) ask the
        // core's player records; ServerModule registers them over the engine's
        // own, so they join here too — the rig's records answer membership
        // alone, the way the drops read them.
        c.AddInstance<SourceSharp.Server.IPlayerDirectory>(new TF2.Tests.Spy.SpyRecords());
        // The death notice is a ServerModule instance registration outside the
        // scan (its AddInstance/Create pair), taken by constructor through the
        // now-required notice parameter; it joins over the same engine fake.
        c.AddInstance(new SourceSharp.Server.DeathNotice(engine, engine, engine,
                                                         new SourceSharp.Server.DamageInflictors()));
        // The corpses pair is the module's own Create-and-hold beside the scan
        // (ServerModule's _corpses); the notice's body and the spy's ragdoll
        // both read it, so it joins over the same engine fake and directory.
        c.AddInstance(new SourceSharp.Server.Corpses(new RecordingLog(), engine, engine,
                                                     new TF2.Tests.Combat.NoEntities()));
        // The round roster's names: ServerModule's own instance beside the
        // scan (the same Create the module runs, after the clients it reads).
        c.AddInstance(c.Create<SourceSharp.Server.PlayerNames>());
        return c;
    }

    [Fact]
    public void The_server_composes_the_tf_rows_behaviours_ammo_and_carry_rule()
    {
        var c = Server();

        var types = Assert.Single(c.ResolveAll<IWeaponTypeSource>().OfType<TFWeaponTypes>());
        Assert.Equal(34, types.Rows().Count());
        var behaviours = Assert.Single(c.ResolveAll<IWeaponBehaviourSource>().OfType<TFWeaponBehaviours>());
        Assert.Equal(Enum.GetValues<TFWeaponKind>().Length, behaviours.Behaviours().Count());
        Assert.Single(c.ResolveAll<IAmmoTypeSource>().OfType<TFAmmoTypes>());
        Assert.Single(c.ResolveAll<IAmmoCarryRule>().OfType<TFAmmoCarryRule>());
        Assert.IsType<TFCritConVars>(c.Resolve(typeof(ITFCritSettings)));
        Assert.IsType<TF2.Server.Player.TFServerWeaponOwners>(((TFWeaponParts)c.Resolve(typeof(TFWeaponParts))!).Owners);
        // The medigun's replay arm is the server's own answer: required, so a
        // side without it fails the composition, not the first heal tick.
        Assert.IsType<TF2.Server.Medigun.TFServerMedigunPrediction>(
            ((TFWeaponParts)c.Resolve(typeof(TFWeaponParts))!).MedigunPrediction);
        // The PDA's trigger hands the owner back through the scan's real
        // switch over the core's arsenal: a seam with no owner on the side
        // would fail here, not on the trigger (3.5's first boot).
        Assert.IsType<TF2.Server.Weapons.TFPDAItemSwitch>(
            c.Resolve(typeof(TF2.Server.Weapons.ITFItemSwitchEffects)));
    }

    [Fact]
    public void The_flag_rules_compose_over_the_scan_without_a_healing_cycle()
    {
        // The healing's flag-rule ask is the leaf seam (IFlagCarriers, over the
        // entity directory): the medigun link the weapon owners carry made every
        // constructor edge back to CtfRules a turn of the cycle CtfRules ->
        // EngineCtfPlayers -> TFServerWeaponOwners -> TFMedigunWeapon ->
        // TFPlayerHealing and back, which refused the mod's whole load at the
        // boot. The scan's real rules stand behind the round machine's seams
        // here, as the game's composition holds them.
        Assert.IsType<CtfRules>(Server().Resolve(typeof(CtfRules)));
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
    public void The_server_scan_holds_the_gesture_rule()
    {
        // The third-person gesture seam's TF answer: the server's chain holds
        // it, so the arsenal's ask names TF events and not the HL2MP tables.
        var c = Server();

        Assert.IsType<TF2.Shared.Anim.TFWeaponGestures>(
            Assert.Single(c.ResolveAll<SourceSharp.Shared.Anim.IWeaponGestureRule>()));
    }

    [Fact]
    public void The_servers_chain_holds_tf2s_hooks_in_order()
    {
        var chain = (TFWeaponHookChain)Server().Resolve(typeof(TFWeaponHookChain))!;

        Assert.Collection(chain.All,
            h => Assert.IsType<TFSwordHook>(h),
            h => Assert.IsType<TFAttributeWeaponHook>(h),
            h => Assert.IsType<TFCritBoostHook>(h),
            h => Assert.IsType<TFKingReloadHook>(h),
            h => Assert.IsType<TFHealedReloadHook>(h),
            h => Assert.IsType<TF2.Server.Weapons.TFRevengeHook>(h),
            h => Assert.IsType<TF2.Server.Weapons.TFKnifeServerHook>(h),
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
        // Both sides read the shipped schema over their own file seam, as the game
        // composes them (the GAME path holds items_game.txt beside the scripts), so
        // the item-carried names join the catalogue on both sides alike.
        var files = WeaponFixtures.ShippedWithClasses();
        files.Bytes[TF2.Shared.Econ.ItemSchema.FilePath] = System.Text.Encoding.UTF8.GetBytes(EconTestFiles.Fixture("items_game.trimmed.txt"));
        c.AddInstance<CAbi.IEngineFiles>(new ClientFiles(files));
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
        c.AddInstance(cloakVars.Declared<CTF.TFSpyCloakConsumeRateConVar>());
        c.AddInstance(cloakVars.Declared<CTF.TFSpyCloakRegenRateConVar>());
        c.AddInstance(cloakVars.Declared<CTF.TFSpyInvisTimeConVar>());
        c.AddInstance(cloakVars.Declared<CTF.TFSpyInvisUnstealthTimeConVar>());
        // The weapons' three convars at their declared defaults, as the game
        // client's publish leaves them (ClientModule's RegisterAll +
        // PublishTo): the parts read them by constructor, so the composition
        // holds them as the leaves they are.
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.WeaponMedigunChargeRateConVar>());
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFUseFixedWeaponspreadsConVar>());
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFFlameThrowerBurstammoConVar>());
        // The prediction's charge and hype meters' rates, as the module's
        // convar publish holds them.
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFDemomanChargeDrainTimeConVar>());
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFDemomanChargeRegenRateConVar>());
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFScoutEnergydrinkConsumeRateConVar>());
        c.AddInstance(DeclaredClientConVars.Fresh<CTF.TFScoutEnergydrinkRegenRateConVar>());
        // The owners' required deps the module itself holds: the catalogue over
        // the sides' rows (ClientModule's own), the local arsenal it registers,
        // and the hud the engine compose binds.
        c.AddInstance(new CShared.WeaponCatalog(() => c.ResolveAll<CShared.IWeaponTypeSource>(), new RecordingLog()));
        c.AddInstance<CClient.ILocalArsenal>(new NoLocalArsenal());
        c.AddInstance<CAbi.IEngineHud>(engine);
        CAbi.SideServices.Register(c, Side.Client, typeof(CTF.TFWeaponBehaviour).Assembly.GetTypes());
        // The item-carried names the client's rows read ride the schema over the
        // same file seam; join it after the scan, as the server helper does, so
        // it stands over the scan's own claim instead of colliding with it.
        c.AddInstance(new CTF.Econ.ItemSchema(new ClientFiles(files), new RecordingLog()));
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
        Assert.Equal(6, chain.All.Count);
        Assert.IsType<CTF.TFSwordHook>(chain.All[0]);
        Assert.IsType<CTF.TFKingReloadHook>(chain.All[3]);
        Assert.IsType<CTF.TFWrenchHook>(chain.All[4]);
        Assert.IsType<client::TF2.Client.Weapons.TFKnifeClientHook>(chain.All[5]);
        Assert.IsType<client::TF2.Client.Player.TFClientWeaponOwners>(((CTF.TFWeaponParts)c.Resolve(typeof(CTF.TFWeaponParts))!).Owners);
        // The client's silent answers for the building and revenge seams: the
        // sides that never see them hold the no-op arms, required like every
        // other part, so the composition names a missing one.
        var clientParts = (CTF.TFWeaponParts)c.Resolve(typeof(CTF.TFWeaponParts))!;
        Assert.IsType<client::TF2.Client.Buildings.TFClientBuilderPlacement>(clientParts.Builder);
        Assert.IsType<client::TF2.Client.Buildings.TFClientSapperState>(clientParts.Sapper);
        Assert.IsType<client::TF2.Client.Buildings.TFClientSentryWrangle>(clientParts.SentryWrangle);
        Assert.IsType<client::TF2.Client.Player.TFClientRevengeCrits>(clientParts.RevengeCrits);
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
        c.AddInstance<CClient.IClientStandalones>(new CClient.Standalone.ClientStandaloneRegistry(c));  // the local player's shell the carried walk reads
        c.AddInstance(new CShared.WeaponScriptDatabase(log, null, files.ReadGameFileBytes, CTF.TFEncryption.Key));
        c.AddInstance(new CTF.TFWeaponCriticalsConVar());
        CAbi.SideServices.Register(c, Side.Client, typeof(CTF.TFWeaponBehaviour).Assembly.GetTypes());
        c.AddInstance(new CShared.WeaponCatalog(() => c.ResolveAll<CShared.IWeaponTypeSource>(), log));

        Assert.IsType<client::TF2.Client.Weapons.TFWeaponSlotForecast>(
            c.Resolve(typeof(CAbi.IWeaponSlotForecast)));
    }

    /// <summary>
    /// The corpse fade is TF2's (c_tf_player.cpp:513-516, :863; HL2MP's client
    /// has none): the client assembly registers it as the fade the bodies ask,
    /// and the frame walk finds it by its type.
    /// </summary>
    [Fact]
    public void The_client_composes_the_corpse_fade()
    {
        var c = new CAbi.ServiceContainer();
        c.AddInstance<ILog>(new CAbi.RecordingLog());
        c.AddInstance<CAbi.IEngineEntityRender>(new CAbi.RecordingEngine());
        c.AddInstance<CAbi.IGameClock>(new CAbi.RecordingEngine());
        c.AddInstance(new CClient.Ragdolls.RagdollFadeTimeConVar());
        CAbi.SideServices.Register(c, Side.Client, new[] { typeof(client::TF2.Client.Player.TFRagdollFades) });

        var fade = Assert.IsType<client::TF2.Client.Player.TFRagdollFades>(
            c.Resolve(typeof(CClient.Ragdolls.IRagdollFades)));
        Assert.Contains(fade, c.ResolveAll<CClient.IClientFrameThink>());
    }

    private sealed class NoRewind : SourceSharp.Server.ILagCompensation
    {
        public bool IsCurrentlyDoingLagCompensation => false;
        public void Start(EntityHandle player) { }
        public void Start(EntityHandle player, in UserCmd cmd) { }
        public void Finish(EntityHandle player) { }
        public void NoteWeaponFired(EntityHandle player) { }
    }
}
