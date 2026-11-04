/*
 * A weapon switch, crossed — the axis the first live switch proved had no
 * coverage at all.
 *
 * ViewModelPresenterTests and the live verification both held ONE weapon;
 * the first invnext found two faults with one cause: the viewmodel entity
 * dropped out of the client's PVS (its box is the v_ model's, authored in
 * camera space) and went dormant holding the OLD weapon's model. What a unit
 * test can hold still of that is the server's side of the contract:
 *
 *   - a switch DRESSES both carried entities in the new weapon's models,
 *     and dresses them back on the way back;
 *   - the viewmodel is marked always-transmitted the moment it exists, so
 *     no PVS box — sane or authored-in-camera-space — can strand it.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class WeaponSwitchTests
{
    private const uint PlayerHandle = 0x0001_0001;

    //-------------------------------------------------------------------------
    // The world: a recording engine everywhere, except that Create hands out
    // real distinct handles and Spawn builds the managed entity in a registry,
    // because the Arsenal's carried pair are real ScriptedEntity instances and
    // a double that answered one handle for every Create would fold the weapon
    // and the viewmodel into the same entity. Shared with WeaponDeathTests,
    // whose Strip/re-deploy cycle needs the same real carried pair.
    //-------------------------------------------------------------------------

    internal sealed class SpawningEntities : IEngineEntities
    {
        // The interface's former default body (plan_shim_areas_split phase 4: interface assemblies hold no bodies).
        public IInterpolationBlocks InterpolationBlocks => NoInterpolationBlocks.Instance;
        public IRenderPolicyBlocks RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
        public IClientReplayEngine ClientReplay => NoClientReplayEngine.Instance;

        private readonly IEngineEntities _engine;
        private readonly ScriptedEntityRegistry _registry;
        private readonly Dictionary<uint, string> _created = new();

        /// <summary>Every classname this double has been asked to create, in no order.</summary>
        public IReadOnlyCollection<string> CreatedClasses => _created.Values;
        private uint _next = 0x0500;

        public readonly List<EntityHandle> WeaponHandles = new();
        public readonly List<EntityHandle> ViewModelHandles = new();
        public readonly List<EntityHandle> AttachmentHandles = new();
        public readonly List<EntityHandle> AttachedModelHandles = new();

        /// <summary>A fact's own entity index per handle; null asks the engine.</summary>
        public Func<EntityHandle, int?>? IndexOf;

        public SpawningEntities(RecordingEngine engine, ScriptedEntityRegistry registry)
        {
            _engine = engine;
            _registry = registry;
        }

        // How full the edict table is. Forwarded like every other member this
        // double does not decide for itself.
        public int Count() => _engine.Count();

        // LevelInit's two entity-list steps (plan_gamedll.md Part 4). Forwarded
        // like everything else this double does not decide for itself.
        public void PurgeMapRefs() => _engine.PurgeMapRefs();
        public void CleanupDeleteList() => _engine.CleanupDeleteList();

        // The model-cache flush walk (plan_gamedll_after.md Phase A2). Forwarded
        // for the same reason: this double decides handles, and nothing else.
        public int InvalidateMdlCacheAll() => _engine.InvalidateMdlCacheAll();
        public int UpdateVisibilityAll() => _engine.UpdateVisibilityAll();

        public EntityHandle Create(string className)
        {
            var handle = new EntityHandle(_next++);
            _created[handle.Value] = className;

            if (className == EntityNames.SsWeapon)
                WeaponHandles.Add(handle);
            else if (className == EntityNames.PredictedViewModel)
                ViewModelHandles.Add(handle);
            else if (className == EntityNames.SsViewmodelAttachment)
                AttachmentHandles.Add(handle);
            else if (className == EntityNames.SsAttachedModel)
                AttachedModelHandles.Add(handle);

            return handle;
        }

        // The map spawner's creation (plan_entity.md step 2d). Through the same
        // body as Create, because the difference between them is a payload of
        // keys the SHELL keeps and this double has no shell.
        public EntityHandle CreateFromMap(string className, byte[] packedKeys)
            => Create(className);

        public EntityHandle CreateFromMapAt(string className, byte[] packedKeys, int edictIndex)
            => Create(className);

        public void Spawn(EntityHandle entity)
        {
            if (_created.TryGetValue(entity.Value, out string? className))
                _registry.Spawn(entity, className);
        }

        // Everything else is the recording double's, so stubs and assertions
        // keep working through this wrapper.
        public void AddBaseVelocity(EntityHandle entity, Vector velocity) => _engine.AddBaseVelocity(entity, velocity);
        public void PositionChanged(EntityHandle entity, Vector previousOrigin) => _engine.PositionChanged(entity, previousOrigin);
        public void ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => _engine.ApplyVelocityImpulse(h, impulse, clearGround);
        public IEnumerable<EntityHandle> FindAllByClassname(string className) => _engine.FindAllByClassname(className);
        public List<EntityHandle> FindAllByName(string name, int limit = 512) => _engine.FindAllByName(name, limit);
        public EntityHandle FindByName(string name) => _engine.FindByName(name);
        public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => _engine.FireInput(target, input, value, activator);
        public Vector GetAngles(EntityHandle h) => _engine.GetAngles(h);
        public Vector GetAngularVelocity(EntityHandle entity) => _engine.GetAngularVelocity(entity);
        public int GetArmor(EntityHandle entity) => _engine.GetArmor(entity);
        public (Vector Mins, Vector Maxs) GetBounds(EntityHandle entity) => _engine.GetBounds(entity);
        public Vector GetBoundsSize(EntityHandle entity) => _engine.GetBoundsSize(entity);
        public string GetClassName(EntityHandle h) => _engine.GetClassName(h);
        public Vector GetEyeAngles(EntityHandle h) => _engine.GetEyeAngles(h);
        public Vector GetEyePosition(EntityHandle h) => _engine.GetEyePosition(h);
        public int GetHealth(EntityHandle h) => _engine.GetHealth(h);
        public int GetIndex(EntityHandle h) => IndexOf?.Invoke(h) ?? _engine.GetIndex(h);
        public string GetName(EntityHandle entity) => _engine.GetName(entity);
        public Vector GetOrigin(EntityHandle h) => _engine.GetOrigin(h);
        public Vector GetSpawnAngles(EntityHandle entity) => _engine.GetSpawnAngles(entity);
        public int GetSpawnFlags(EntityHandle entity) => _engine.GetSpawnFlags(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => _engine.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => _engine.GetVelocity(h);
        public Vector GetSmoothedVelocity(EntityHandle h) => default;
        public Vector GetWorldSpaceCenter(EntityHandle h) => _engine.GetWorldSpaceCenter(h);
        public bool IsPlayer(EntityHandle h) => _engine.IsPlayer(h);
        public bool IsValid(EntityHandle h) => _engine.IsValid(h);
        public void Remove(EntityHandle entity) => _engine.Remove(entity);
        public void SetAngles(EntityHandle entity, Vector angles) => _engine.SetAngles(entity, angles);
        public void SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => _engine.SetAngularVelocity(entity, degreesPerSecond);
        public void SetArmor(EntityHandle entity, int armor) => _engine.SetArmor(entity, armor);
        public void SetHealth(EntityHandle h, int health) => _engine.SetHealth(h, health);
        public bool SetKeyValue(EntityHandle entity, string key, string value) => _engine.SetKeyValue(entity, key, value);
        public void SetOrigin(EntityHandle h, Vector origin) => _engine.SetOrigin(h, origin);
        public void SetOwner(EntityHandle h, EntityHandle owner) => _engine.SetOwner(h, owner);
        public void SetClassName(EntityHandle h, string className) => _engine.SetClassName(h, className);
        public void SetVelocity(EntityHandle h, Vector velocity) => _engine.SetVelocity(h, velocity);
        public void Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => _engine.Teleport(h, origin, angles, zeroVelocity);
        public bool SetParent(EntityHandle child, EntityHandle parent, int parentAttachment = 0) => _engine.SetParent(child, parent, parentAttachment);
        public EntityHandle GetParent(EntityHandle child) => _engine.GetParent(child);
        public int GetParentAttachment(EntityHandle child) => _engine.GetParentAttachment(child);
        public void SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => _engine.SetLocalTransform(child, origin, angles);
        public void GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles) => _engine.GetLocalTransform(child, out origin, out angles);
        public ParentProbe ParentProbe(EntityHandle entity) => _engine.ParentProbe(entity);
        public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles) => _engine.GetAttachmentFrame(entity, attachment, out origin, out angles);
        public int LookupAttachmentAny(EntityHandle entity, string attachmentName) => _engine.LookupAttachmentAny(entity, attachmentName);
        public EntityHandle FindBaseByName(string name) => _engine.FindBaseByName(name);
        public Span<byte> GetEngineBlock(EntityHandle entity) => _engine.GetEngineBlock(entity);
        public void DestroyPhysics(EntityHandle entity) => _engine.DestroyPhysics(entity);

        // The native half of a savegame. Forwarded like everything else this
        // double does not decide for itself -- an Arsenal never asks any of
        // them, and a double that answered them itself would be inventing an
        // answer no test chose.
        public float GetGravity(EntityHandle entity) => _engine.GetGravity(entity);
        public void SetGravity(EntityHandle entity, float gravity) => _engine.SetGravity(entity, gravity);
        public SolidFlags GetSolidFlags(EntityHandle entity) => _engine.GetSolidFlags(entity);
        public SolidType GetSolidType(EntityHandle entity) => _engine.GetSolidType(entity);
        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => _engine.SetSolidity(entity, solidType, solidFlags);
        public void UseTriggerBounds(EntityHandle entity, bool enable, float bloat, bool uniform = false) => _engine.UseTriggerBounds(entity, enable, bloat, uniform);
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => _engine.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => _engine.SetSpawnFlags(entity, spawnFlags);
    }

    internal static (Arsenal arsenal, RecordingEngine engine, SpawningEntities world) World()
        => World(out _, viewmodelModel: null);

    internal static (Arsenal arsenal, RecordingEngine engine, SpawningEntities world) World(out ScriptedEntityRegistry carried)
        => World(out carried, viewmodelModel: null);

    internal static (Arsenal arsenal, RecordingEngine engine, SpawningEntities world) World(
        out ScriptedEntityRegistry carried, ViewmodelModel? viewmodelModel,
        ViewmodelSkin? viewmodelSkin = null, IServerSkins? skins = null, RecordingLog? log = null,
        WeaponCatalog? weapons = null)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            // Alive: the Arsenal refuses a dead player's hands outright, and
            // everything below is about what a LIVING player's switch does.
            .Returns("Players.IsAlive", true)
            // Where a dropped weapon lands. Stated here rather than per test
            // because dropping is part of switching and dying alike, and it
            // reaches the floor trace either way.
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f),
                                     new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        // THE PLAYER IS SPAWNED INTO THE REGISTRY, and this fixture used to
        // stub only the classname.
        //
        // plan-audit D19 measured Arsenal.IsStandalonePlayer asking the shell
        // for a classname 6.000 times a server frame and made it a registry
        // lookup. On a live server the two are the same fact -- SsPlayer IS the
        // [ScriptedEntity] bound to "player" -- but a fixture that answers
        // the classname and holds no instance is a player the ARSENAL can see
        // and the REGISTRY cannot, which is a state no running game is ever in.
        // The stand-in had drifted from the thing it stands in for; this is the
        // agreement asserted rather than assumed.
        //
        // KeyValue and not Spawn, because Spawn is not what makes the instance
        // exist -- the registry's own comment says so: "Created on demand
        // rather than at spawn because of the order Source works in: an entity
        // is constructed, then handed every keyvalue in the map, and only then
        // spawned." A fixture stopping after the keyvalues is a state every
        // running server passes through; running OnSpawn here would put an
        // EntityRender.SetVisible into engine.Calls, which two facts in this
        // file read.
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new SpawningEntities(engine, registry);

        var arsenal = new Arsenal(log ?? new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(),
                                  weapons: weapons, viewmodelModel: viewmodelModel,
                                  viewmodelSkin: viewmodelSkin, skins: skins);

        carried = registry;
        return (arsenal, engine, world);
    }

    /// <summary>The models handed to the renderer for the given handles, in call order.</summary>
    private static List<string> DressedModels(RecordingEngine engine, List<EntityHandle> handles)
        => engine.Calls
            .Where(c => c.Member == "EntityRender.SetModel" && handles.Contains(c.ArgAs<EntityHandle>(0)))
            .Select(c => c.ArgAs<string>(1))
            .ToList();

    internal static EntityHandle Player => new(PlayerHandle);

    //-------------------------------------------------------------------------
    // Fault 1's server contract, crossed twice and back: the models follow.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASwitchZeroesTheBehavioursOwnState()
    {
        var (arsenal, engine, _) = World(out var registry);

        // The carried pair stays valid, so the switch re-deploys the SAME
        // weapon entity rather than creating a fresh one (whose state would
        // be zero without anything clearing it).
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");

        var state = Assert.IsType<SsWeapon>(registry.Get(arsenal.CarriedPair(Player).Weapon)).State;

        // What the crowbar's behaviour would have left behind: non-zero, so
        // the switch is seen to clear it rather than find it clear.
        state.BehaviourMode = 7;
        state.BehaviourTimeA = 11f;
        state.BehaviourTimeB = 12f;
        state.BehaviourMeter = 13f;

        var weapon = arsenal.CarriedPair(Player).Weapon;
        Assert.True(arsenal.Switch(Player, "weapon_pistol") == SwitchOutcome.Switched, "the switch succeeds");
        Assert.Equal(weapon, arsenal.CarriedPair(Player).Weapon);

        Assert.Equal(0, state.BehaviourMode);
        Assert.Equal(0f, state.BehaviourTimeA);
        Assert.Equal(0f, state.BehaviourTimeB);
        Assert.Equal(0f, state.BehaviourMeter);
    }

    [Fact]
    public void ASwitchDressesTheViewModelInTheNewWeaponsModelAndBackAgain()
    {
        var (arsenal, engine, world) = World();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"),
            "the first weapon is taken and deploys itself");
        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"),
            "a second weapon is taken and waits to be asked for");

        Assert.True(arsenal.Switch(Player, "weapon_pistol") == SwitchOutcome.Switched,
            "the switch to it succeeds");
        Assert.True(arsenal.Switch(Player, "weapon_crowbar") == SwitchOutcome.Switched,
            "and the switch back succeeds");

        var models = DressedModels(engine, world.ViewModelHandles);

        Assert.True(models.Count == 3,
            $"three deploys dress the viewmodel three times, not {models.Count} — " +
            "a switch that does not re-dress is exactly the live bug's shape");
        Assert.True(models[0] == "models/weapons/v_crowbar.mdl",
            "the first deploy wears the crowbar's arms");
        Assert.True(models[1] == "models/weapons/v_pistol.mdl",
            "the switch wears the NEW weapon's arms — the model follows the switch");
        Assert.True(models[2] == "models/weapons/v_crowbar.mdl",
            "and the switch back wears the old ones again");
    }

    [Fact]
    public void ASwitchDressesTheCarriedWeaponInTheNewWorldModelToo()
    {
        var (arsenal, engine, world) = World();

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        arsenal.Switch(Player, "weapon_pistol");

        var models = DressedModels(engine, world.WeaponHandles);

        Assert.True(models.Count == 2 &&
                    models[0] == "models/weapons/w_crowbar.mdl" &&
                    models[1] == "models/weapons/w_pistol.mdl",
            "what everyone else sees in the hand follows the switch the same way");
    }

    //-------------------------------------------------------------------------
    // The wire semantic: CUserCmd::weaponselect (stock's CreateMove latch,
    // in_main.cpp:1207-1214, and RunCommand's SelectItem,
    // player_command.cpp:376-385) names a weapon by its index in the shared
    // namespace. Ours is the WeaponCatalog's — the same index ss_weapon's
    // WeaponTypeId already carries — because the one ss_weapon per player is
    // re-dressed at deploy, so no entity index exists to send.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASwitchByCatalogTypeDeploysTheWeaponTheWireNames()
    {
        var (arsenal, engine, _) = World(out var registry);
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");

        int pistol = arsenal.Weapons.IndexOf("weapon_pistol");

        Assert.True(arsenal.SwitchType(Player, pistol) == SwitchOutcome.Switched,
            "the switch by catalog type succeeds");

        var weapon = Assert.IsType<SsWeapon>(registry.Get(arsenal.CarriedPair(Player).Weapon)).State;

        Assert.Equal(pistol, weapon.WeaponTypeId);

        // The deploy clocks the client's replay has to match.
        float deploy = arsenal.Weapons.For("weapon_pistol").GetValueOrDefault().DeployTime;

        Assert.Equal(10f + deploy, weapon.NextPrimaryAttack, 4);
        Assert.Equal(10f + deploy, weapon.NextSecondaryAttack, 4);
        Assert.Equal(10f + deploy, weapon.TimeWeaponIdle, 4);
    }

    [Fact]
    public void ASwitchByATypeNotCarriedIsRefusedNotThrown()
    {
        var (arsenal, _, _) = World();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the crowbar is taken");

        Assert.Equal(SwitchOutcome.NotOwned, arsenal.SwitchType(Player, arsenal.Weapons.IndexOf("weapon_pistol")));
        Assert.Equal(SwitchOutcome.NotOwned, arsenal.SwitchType(Player, 0x7FFF));
    }

    //-------------------------------------------------------------------------
    // The viewmodel's model chain (IViewmodelModelRule): a rule names what the
    // hands wear before the row's own model is dressed — stock's
    // CTFWeaponBase::GetViewModel answering the class hand model
    // (tf_weaponbase.cpp:651-676).
    //-------------------------------------------------------------------------

    private sealed class HandsRule(string hands) : IViewmodelModelRule
    {
        public string? Model(int playerIndex, WeaponType type, string? chosen) => hands;
    }

    private sealed class SilentRule : IViewmodelModelRule
    {
        public string? Model(int playerIndex, WeaponType type, string? chosen) => chosen;
    }

    private sealed class ThrowingModelRule : IViewmodelModelRule
    {
        public string? Model(int playerIndex, WeaponType type, string? chosen)
            => throw new InvalidOperationException("the rule is broken");
    }

    private sealed class ListHooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;
        public void Each(string what, Action<T> call)
        {
            foreach (var hook in all)
                call(hook);
        }
    }

    [Fact]
    public void ARuleDressesTheViewModelInTheModelItNames()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        var models = DressedModels(engine, world.ViewModelHandles);

        Assert.True(models.Count == 1 && models[0] == hands,
            $"the rule's model is what the viewmodel wears, not {string.Join(", ", models)}");
    }

    [Fact]
    public void ARuleThatAnswersNothingLeavesTheRowsOwnModel()
    {
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new SilentRule())));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        var models = DressedModels(engine, world.ViewModelHandles);

        Assert.True(models.Count == 1 && models[0] == "models/weapons/v_crowbar.mdl",
            $"a rule with no opinion leaves the row's own v_ model, not {string.Join(", ", models)}");
    }

    [Fact]
    public void TheChainHasNoRulesByDefault()
    {
        var (arsenal, engine, world) = World();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        var models = DressedModels(engine, world.ViewModelHandles);

        Assert.True(models.Count == 1 && models[0] == "models/weapons/v_crowbar.mdl",
            $"no chain means the row's own v_ model, not {string.Join(", ", models)}");
    }

    [Fact]
    public void TheLaterRuleOverridesTheEarlierOne()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        const string other = "models/weapons/c_models/c_scout_arms.mdl";

        var chain = new ViewmodelModel(new ListHooks<IViewmodelModelRule>(
            new HandsRule(hands), new HandsRule(other)));

        Assert.Equal(other, chain.Model(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));
    }

    [Fact]
    public void AThrowingModelRuleIsSkippedAndTheAnswerBeforeItStands()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";

        var keeps = new ViewmodelModel(new ListHooks<IViewmodelModelRule>(
            new HandsRule(hands), new ThrowingModelRule()), new RecordingLog());
        Assert.Equal(hands, keeps.Model(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));

        var alone = new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new ThrowingModelRule()),
                                       new RecordingLog());
        Assert.Null(alone.Model(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));
    }

    //-------------------------------------------------------------------------
    // The viewmodel attachment (stock's C_ViewmodelAttachmentModel): when the
    // chain chose something other than the row's own model for the viewmodel —
    // the hands — the row's own model rides them as a bonemerged attachment.
    // When the row's own model IS the viewmodel, there is nothing to attach.
    //-------------------------------------------------------------------------

    [Fact]
    public void AChosenPairOfHandsAttachesTheRowsOwnModel()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out var registry,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));

        engine.Returns("Entities.GetIndex", 3);
        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        Assert.True(world.AttachmentHandles.Count == 1,
            $"the attachment exists beside the pair, not {world.AttachmentHandles.Count}");

        var models = DressedModels(engine, world.AttachmentHandles);
        Assert.True(models.Count == 1 && models[0] == "models/weapons/v_crowbar.mdl",
            $"the row's own model is what rides the hands, not {string.Join(", ", models)}");

        var state = Assert.IsType<SsViewmodelAttachment>(registry.Get(world.AttachmentHandles[0])).State;
        Assert.True(state.OwnerIndex == 3, "the attachment names whose viewmodel it rides");

        var shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(shown.Count > 0 && shown[^1], "the attachment is drawn, not left hidden");
    }

    [Fact]
    public void TheAttachmentNamesTheViewmodelItRides()
    {
        // Stock's attachment follows the viewmodel entity itself
        // (C_EconEntity::UpdateAttachmentModels' FollowEntity). The client
        // cannot find it by looking: the attachment decodes rarely, and the
        // viewmodel's first decode may come after its only one.
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out var registry,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));
        world.IndexOf = h => world.ViewModelHandles.Contains(h) ? 0x42 : 3;

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        var state = Assert.IsType<SsViewmodelAttachment>(registry.Get(world.AttachmentHandles[0])).State;
        Assert.True(state.ViewModelIndex == 0x42,
            $"the attachment names the viewmodel it rides (0x42), not {state.ViewModelIndex}");
        Assert.True(state.OwnerIndex == 3, "and still whose it is");
    }

    [Fact]
    public void TheRowsOwnViewModelAttachesNothing()
    {
        var (arsenal, engine, world) = World();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        Assert.True(world.AttachmentHandles.Count == 0,
            "no chain, no attachment: the viewmodel already IS the row's own model");
    }

    private sealed class ConditionalHandsRule(string forName, string hands) : IViewmodelModelRule
    {
        public string? Model(int playerIndex, WeaponType type, string? chosen)
            => type.Name == forName ? hands : chosen;
    }

    [Fact]
    public void ASwitchFromHandsToTheRowsOwnModelHidesTheAttachment()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(
                new ConditionalHandsRule("weapon_pistol", hands))));

        // The pistol deploys with hands: the attachment is created and shown.
        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "the first weapon is taken");
        Assert.True(world.AttachmentHandles.Count == 1, "the attachment exists");

        var shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(shown.Count > 0 && shown[^1], "the attachment was shown under the hands rule");

        // The switch to the crowbar's own arms: same chain, no opinion now.
        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the second weapon is taken and waits");
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");
        Assert.True(arsenal.Switch(Player, "weapon_crowbar") == SwitchOutcome.Switched,
            "the switch succeeds");
        shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(!shown[^1], "and the switch to the row's own model hides it again");
    }

    private sealed class OneRowSource(WeaponType row) : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [row];
    }

    [Fact]
    public void ARowWithoutADisplayModelHidesTheAttachment()
    {
        // TF's fists: the item's model_player is EMPTY (the hands are the whole
        // viewmodel), so stock's UpdateAttachmentModels dresses the attachment
        // in nothing and the previous weapon's model must not keep standing
        // (econ_entity.cpp:1167, a display model of "").
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var bare = new WeaponType("weapon_barehands", 0.5f)
        {
            ViewModel = "",
            WorldModel = "models/weapons/w_barehands.mdl",
        };
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))),
            weapons: new WeaponCatalog(() => [new OneRowSource(bare)]));

        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "the first weapon is taken");
        Assert.True(world.AttachmentHandles.Count == 1, "the attachment exists");

        var shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(shown.Count > 0 && shown[^1], "the attachment was shown under the hands rule");

        Assert.True(arsenal.GiveWeapon(Player, "weapon_barehands"), "the bare row is taken");
        Assert.True(arsenal.Switch(Player, "weapon_barehands") == SwitchOutcome.Switched,
            "and switches to it");

        shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(!shown[^1], "a row with no display model leaves nothing riding the hands");
    }

    //-------------------------------------------------------------------------
    // The deploy stamp (WeaponNetState.DeployEpoch): stock's fresh weapon
    // entity at respawn is what our persistent pair cannot be, and the stamp
    // is the event the recreation would have been — the client adopts an
    // arrival stamped ahead of its acked slot rather than judging it. Only
    // Deploy stamps: a refill, a fire and the machine's every-command writes
    // leave it standing, so an unmirrored clock write stays judgable.
    //-------------------------------------------------------------------------

    private static WeaponNetState CarriedState(Arsenal arsenal, ScriptedEntityRegistry registry)
        => Assert.IsType<SsWeapon>(registry.Get(arsenal.CarriedPair(Player).Weapon)).State;

    [Fact]
    public void EveryDeployStampsTheWeaponWithANewEpoch()
    {
        var (arsenal, engine, _) = World(out var registry);
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        Assert.True(CarriedState(arsenal, registry).DeployEpoch == 1,
            "the first deploy stamps 1");

        arsenal.GiveWeapon(Player, "weapon_pistol");
        Assert.True(CarriedState(arsenal, registry).DeployEpoch == 1,
            "a taken weapon that only fills the shelves stamps nothing");

        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_pistol"));
        Assert.True(CarriedState(arsenal, registry).DeployEpoch == 2,
            "the switch's deploy stamps again");

        // Stock's respawn order (see ARespawnReusesTheViewmodelAttachment):
        // the hands emptied, then the kit handed again — a third deploy, on
        // the SAME entity, which is the spawn the client cannot predict.
        arsenal.Strip(Player);
        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the kit is handed again");
        Assert.True(CarriedState(arsenal, registry).DeployEpoch == 3,
            "the respawn's redeploy stamps a third time on the one entity");
    }

    [Fact]
    public void ADuplicateGiveStampsWithoutADeploy()
    {
        var (arsenal, engine, _) = World(out var registry);
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        int before = CarriedState(arsenal, registry).DeployEpoch;

        // The duplicate's Weapon_EquipAmmoOnly arm: shelves only, hands
        // untouched. An epoch that moved here would adopt away a real
        // misprediction of the hands.
        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "the duplicate is worth its ammo");
        Assert.True(CarriedState(arsenal, registry).DeployEpoch == before,
            "the hands saw no deploy, so the stamp stands");
    }

    [Fact]
    public void AStripHidesTheAttachment()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");
        Assert.True(world.AttachmentHandles.Count == 1, "the attachment exists");

        arsenal.Strip(Player);

        var shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(shown.Count > 0 && !shown[^1],
            "death's rule holds for the attachment too: nothing shown in empty hands");
    }

    [Fact]
    public void AForgottenPlayersAttachmentIsRemoved()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");
        Assert.True(world.AttachmentHandles.Count == 1, "the attachment exists");

        engine.Returns("Entities.IsValid", true);
        arsenal.Forget(Player);

        Assert.True(engine.Calls.Any(c => c.Member == "Entities.Remove" &&
                                          c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0]),
            "a departed player's attachment goes with the pair");
    }

    [Fact]
    public void ARespawnOfTheKitDeploysEachWeaponOnce()
    {
        // The round gate's premise was that every respawn logs its
        // "[SourceSharp/Arsenal] deployed" line twice. Measured live at this
        // tip instead (tf2-gate's respawn step: two waves of
        // spawns: 2 / deploys: 2 / attachments: 0): each wave deploys the kit
        // once, and the console's double lines were two separate respawns of
        // one player — a queued-class death, then the jointeam respawn — each
        // deploying once. This holds the once-per-respawn shape: the strip is
        // the wipe (hands emptied, the carry and its entities kept, as the
        // live RemoveAllItems and the surviving handles do it), and the kit
        // handed again is the wave, three times over.
        var log = new RecordingLog();
        var (arsenal, engine, _) = World(out _, null, log: log);
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        for (var wave = 1; wave <= 3; wave++)
        {
            Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the kit is handed again");
            arsenal.Strip(Player);
        }

        var deploys = log.Lines.Count(l => l.Text.Contains("deployed") && l.Text.Contains("weapon_crowbar"));
        Assert.True(deploys == 3,
            $"three respawns of one weapon log three deploys, not {deploys} — a respawn that " +
            "deploys twice is the double line the gate premise reported");
    }

    [Fact]
    public void ARespawnReusesTheViewmodelAttachment()
    {
        // The attachment half of the same premise (an ss_viewmodel_attachment
        // created per deploy). The strip keeps the carry — only Forget drops
        // its entities — so the respawn's redeploy finds the attachment still
        // valid and re-dresses it, as the live waves measured zero creations.
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out _,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))));
        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        for (var wave = 1; wave <= 3; wave++)
        {
            // Stock's respawn order: OnPlayerSpawn empties the hands, then the
            // kit is handed again — so the last word here is the redeploy's.
            arsenal.Strip(Player);
            Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the kit is handed again");
        }

        Assert.True(world.AttachmentHandles.Count == 1,
            $"three respawns ride the one attachment, not {world.AttachmentHandles.Count} entities");

        var shown = engine.Calls
            .Where(c => c.Member == "EntityRender.SetVisible" &&
                        c.ArgAs<EntityHandle>(0) == world.AttachmentHandles[0])
            .Select(c => c.ArgAs<bool>(1))
            .ToList();
        Assert.True(shown.Count > 0 && shown[^1],
            "and the last word on it is shown: the redeploy rode the reused entity, not a hidden one");
    }

    //-------------------------------------------------------------------------
    // The viewmodel's skin chain (IViewmodelSkinRule): stock's
    // CTFViewModel::GetSkin (tf_viewmodel.cpp:362-398) folded at the deploy,
    // the attachment riding the same skin (econ_entity.cpp:1196), and an
    // attack's own write — the crossbow's glowing bolt — standing when the
    // fold has no opinion.
    //-------------------------------------------------------------------------

    private sealed class TeamSkinRule(int skin) : IViewmodelSkinRule
    {
        public int? Skin(int playerIndex, WeaponType type, int? chosen) => skin;
    }

    private sealed class SilentSkinRule : IViewmodelSkinRule
    {
        public int? Skin(int playerIndex, WeaponType type, int? chosen) => chosen;
    }

    private sealed class ThrowingSkinRule : IViewmodelSkinRule
    {
        public int? Skin(int playerIndex, WeaponType type, int? chosen)
            => throw new InvalidOperationException("the rule is broken");
    }

    private static ViewModelNetState ViewModelState(Arsenal arsenal, ScriptedEntityRegistry registry)
        => Assert.IsType<SsViewModel>(registry.Get(arsenal.CarriedPair(Player).ViewModel)).State;

    [Fact]
    public void ASkinRuleDressesTheViewModelInTheSkinItNames()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var (arsenal, engine, world) = World(out var registry,
            new ViewmodelModel(new ListHooks<IViewmodelModelRule>(new HandsRule(hands))),
            new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(new TeamSkinRule(1))));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        Assert.True(ViewModelState(arsenal, registry).Skin == 1,
            $"the rule's skin is what the hands wear, not {ViewModelState(arsenal, registry).Skin}");
    }

    [Fact]
    public void TheAttachmentWearsTheSameSkinAsTheHands()
    {
        const string hands = "models/weapons/c_models/c_soldier_arms.mdl";
        var skins = new RecordingServerSkins();
        var (arsenal, engine, world) = World(out _, new ViewmodelModel(
            new ListHooks<IViewmodelModelRule>(new HandsRule(hands))),
            new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(new TeamSkinRule(1))), skins);

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");
        Assert.True(world.AttachmentHandles.Count == 1, "the attachment exists");

        Assert.True(skins.Set.Contains((world.AttachmentHandles[0], 1)),
            "stock's m_hViewmodelAttachment->m_nSkin = vm->GetSkin(): the gun rides in the hands' skin");
    }

    [Fact]
    public void NoSkinRuleLeavesTheSkinAnAttackWroteAlone()
    {
        var (arsenal, engine, world) = World(out var registry);

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the first weapon is taken and deploys");
        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "the second is taken and waits");

        engine.Returns("Entities.IsValid", true).Ignores("EntityRender.LookupSequenceByActivity");

        // What an attack's own skin write leaves (the crossbow's glow), before
        // a switch whose deploy has no opinion about skins.
        var state = ViewModelState(arsenal, registry);
        state.Skin = 4;

        Assert.True(arsenal.Switch(Player, "weapon_pistol") == SwitchOutcome.Switched,
            "the switch succeeds");

        Assert.True(state.Skin == 4,
            $"a deploy with no opinion touches nothing — HL2's skins stay as its attacks wrote them, not {state.Skin}");
    }

    [Fact]
    public void ASkinRuleWithNoOpinionLeavesTheSkinAlone()
    {
        var (arsenal, engine, world) = World(out var registry, null,
            new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(new SilentSkinRule())));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crowbar"), "the weapon is taken and deploys");

        Assert.True(ViewModelState(arsenal, registry).Skin == 0,
            "a rule that passes the question through leaves the model's default skin");
    }

    [Fact]
    public void TheAttacksOwnSkinWriteStillWinsDuringTheAttack()
    {
        var (arsenal, engine, world) = World(out var registry, null,
            new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(new TeamSkinRule(5))));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_crossbow"), "the crossbow is taken and deploys");

        // The behaviour's Deploy runs after the fold and gets the deploy's
        // last word: the glowing bolt, whatever a rule said at the switch.
        Assert.True(ViewModelState(arsenal, registry).Skin == 1,
            $"CWeaponCrossbow::Deploy's glow stands over the chain's answer, not {ViewModelState(arsenal, registry).Skin}");
    }

    [Fact]
    public void TheLaterSkinRuleOverridesTheEarlierOne()
    {
        var chain = new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(
            new TeamSkinRule(1), new TeamSkinRule(2)));

        Assert.Equal(2, chain.Skin(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));
    }

    [Fact]
    public void AThrowingSkinRuleIsSkippedAndTheAnswerBeforeItStands()
    {
        var keeps = new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(
            new TeamSkinRule(1), new ThrowingSkinRule()), new RecordingLog());
        Assert.Equal(1, keeps.Skin(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));

        var alone = new ViewmodelSkin(new ListHooks<IViewmodelSkinRule>(new ThrowingSkinRule()),
                                      new RecordingLog());
        Assert.Null(alone.Skin(1, WeaponCatalog.Stock().For("weapon_crowbar").GetValueOrDefault()));
    }

    //-------------------------------------------------------------------------
    // Fault 2's fix: no PVS box may strand the viewmodel.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheViewModelIsMarkedAlwaysTransmittedTheMomentItExists()
    {
        var (arsenal, engine, world) = World();

        arsenal.GiveWeapon(Player, "weapon_crowbar");

        var always = engine.Calls
            .Where(c => c.Member == "EntityRender.SetTransmitAlways")
            .ToList();

        Assert.True(world.ViewModelHandles.Count > 0, "a viewmodel was created at all");

        foreach (var handle in world.ViewModelHandles)
        {
            Assert.True(always.Any(c => c.ArgAs<EntityHandle>(0) == handle && c.ArgAs<bool>(1)),
                "every created viewmodel is marked always-transmitted — its PVS box is " +
                "the v_ model's, authored in camera space, and the live switch proved " +
                "such a box can sit below the floor and strand the entity dormant");
        }

        foreach (var handle in world.WeaponHandles)
        {
            Assert.True(!always.Any(c => c.ArgAs<EntityHandle>(0) == handle),
                "the carried weapon stays PVS-checked — w_ models are authored around " +
                "their origin, and other players SHOULD cull it");
        }
    }

    //-------------------------------------------------------------------------
    // The clip's writer, beside Holdings' read: the holstered store and the
    // hands' live blob, the pickup's two rests.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASetClipReachesAHolsteredWeaponsStoredClip()
    {
        var (arsenal, engine, _) = World();

        // The carried pair survive their deploys: a double answering
        // default-false had EnsureEntities recreate them on every switch, and
        // a store read through the fresh entity's empty blob. The draw's
        // sequence lookups ride along.
        engine.Returns("Entities.IsValid", true)
              .Returns("EntityRender.LookupSequence", -1)
              .Returns("EntityRender.LookupSequenceByActivity", -1);

        arsenal.GiveWeapon(Player, "weapon_smg1");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_pistol"));

        arsenal.SetClip(Player, "weapon_smg1", 7);

        Assert.Equal(7, arsenal.Holdings(Player).First(h => h.ClassName == "weapon_smg1").Clip);
    }

    [Fact]
    public void ASetClipOnTheActiveWeaponReachesTheLiveBlobAndSurvivesASwitch()
    {
        var (arsenal, engine, _) = World();

        // As the fact above: the pair survive their deploys, and the draw's
        // lookups are answered.
        engine.Returns("Entities.IsValid", true)
              .Returns("EntityRender.LookupSequence", -1)
              .Returns("EntityRender.LookupSequenceByActivity", -1);

        arsenal.GiveWeapon(Player, "weapon_smg1");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        // the first given is drawn: the smg1 is in the hands already

        arsenal.SetClip(Player, "weapon_smg1", 3);

        // The hands' blob, which the live fire spends and Holdings reads.
        Assert.Equal(3, arsenal.Holdings(Player).First(h => h.ClassName == "weapon_smg1").Clip);

        // And the store took it: a switch away and back redeploys THE 3, not
        // the full clip a give left.
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_pistol"));
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_smg1"));
        Assert.Equal(3, arsenal.Holdings(Player).First(h => h.ClassName == "weapon_smg1").Clip);
    }

    [Fact]
    public void ATakenActiveWeaponLeavesTheHandsAndTheInventory()
    {
        var (arsenal, engine, _) = World();

        // The carried pair survive their deploys, as the SetClip facts found.
        engine.Returns("Entities.IsValid", true)
              .Returns("EntityRender.LookupSequence", -1)
              .Returns("EntityRender.LookupSequenceByActivity", -1);

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_pistol"));

        Assert.True(arsenal.TakeWeapon(Player, "weapon_pistol"));

        Assert.Equal("weapon_crowbar", arsenal.Holdings(Player).Single().ClassName);
        Assert.Equal(SwitchOutcome.NotOwned, arsenal.Switch(Player, "weapon_pistol"));
    }

    [Fact]
    public void ATakenHolsteredWeaponSlidesTheRowsBehindTheHands()
    {
        var (arsenal, engine, _) = World();

        // As the facts above: the pair survive their deploys.
        engine.Returns("Entities.IsValid", true)
              .Returns("EntityRender.LookupSequence", -1)
              .Returns("EntityRender.LookupSequenceByActivity", -1);

        arsenal.GiveWeapon(Player, "weapon_crowbar");
        arsenal.GiveWeapon(Player, "weapon_pistol");
        arsenal.GiveWeapon(Player, "weapon_357");
        Assert.Equal(SwitchOutcome.Switched, arsenal.Switch(Player, "weapon_357"));

        Assert.True(arsenal.TakeWeapon(Player, "weapon_pistol"));

        // The hands keep the weapon they held: the rows behind it slid down
        // one, and the active index followed them or would name the 357's
        // new neighbour.
        var active = arsenal.Holdings(Player).Single(h => h.Active);
        Assert.Equal("weapon_357", active.ClassName);
        Assert.Equal(2, arsenal.Holdings(Player).Count);
    }
}
