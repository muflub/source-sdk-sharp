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
        private readonly IEngineEntities _engine;
        private readonly ScriptedEntityRegistry _registry;
        private readonly Dictionary<uint, string> _created = new();
        private uint _next = 0x0500;

        public readonly List<EntityHandle> WeaponHandles = new();
        public readonly List<EntityHandle> ViewModelHandles = new();

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
            else if (className == EntityNames.SsViewModel)
                ViewModelHandles.Add(handle);

            return handle;
        }

        // The map spawner's creation (plan_entity.md step 2d). Through the same
        // body as Create, because the difference between them is a payload of
        // keys the SHELL keeps and this double has no shell.
        public EntityHandle CreateFromMap(string className, byte[] packedKeys)
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
        public int GetIndex(EntityHandle h) => _engine.GetIndex(h);
        public string GetName(EntityHandle entity) => _engine.GetName(entity);
        public Vector GetOrigin(EntityHandle h) => _engine.GetOrigin(h);
        public Vector GetSpawnAngles(EntityHandle entity) => _engine.GetSpawnAngles(entity);
        public int GetSpawnFlags(EntityHandle entity) => _engine.GetSpawnFlags(entity);
        public Vector GetSpawnOrigin(EntityHandle entity) => _engine.GetSpawnOrigin(entity);
        public Vector GetVelocity(EntityHandle h) => _engine.GetVelocity(h);
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
        public void SetBounds(EntityHandle entity, Vector mins, Vector maxs) => _engine.SetBounds(entity, mins, maxs);
        public void SetSpawnFlags(EntityHandle entity, int spawnFlags) => _engine.SetSpawnFlags(entity, spawnFlags);
    }

    private static (Arsenal arsenal, RecordingEngine engine, SpawningEntities world) World()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
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

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        // THE PLAYER IS SPAWNED INTO THE REGISTRY, and this fixture used to
        // stub only the classname.
        //
        // plan-audit D19 measured Arsenal.IsStandalonePlayer asking the shell
        // for a classname 6.000 times a server frame and made it a registry
        // lookup. On a live server the two are the same fact -- SsPlayer IS the
        // [ScriptedEntity] bound to "ss_player" -- but a fixture that answers
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
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var world = new SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        return (arsenal, engine, world);
    }

    /// <summary>The models handed to the renderer for the given handles, in call order.</summary>
    private static List<string> DressedModels(RecordingEngine engine, List<EntityHandle> handles)
        => engine.Calls
            .Where(c => c.Member == "EntityRender.SetModel" && handles.Contains(c.ArgAs<EntityHandle>(0)))
            .Select(c => c.ArgAs<string>(1))
            .ToList();

    private static EntityHandle Player => new(PlayerHandle);

    //-------------------------------------------------------------------------
    // Fault 1's server contract, crossed twice and back: the models follow.
    //-------------------------------------------------------------------------

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
}
