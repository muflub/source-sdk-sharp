//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// One object against the world, as the SS-PHYSWORLD report asks it: both
/// sides, whether they are one entity, and the rules' answer (1 collides).
/// </summary>
public readonly record struct WorldVerdict(CollisionSide Side, CollisionSide World, bool SameEntity, int Answer);

/// <summary>
/// Managed's answers to vphysics' <c>IPhysicsCollisionSolver</c>, asked
/// mid-step (stock's CCollisionEvent solver half, game/server/physics.cpp;
/// plan_physics_bones_native_zero 2.1). Each answer reads the pair's owners
/// from the objects' game data, the owners' facts from the engine, and decides
/// here.
/// </summary>
public sealed class PhysicsSolver : IGameLifecycle, IPhysicsSolverAnswers
{
    /// <summary>CALLBACK_IS_VEHICLE_WHEEL.</summary>
    public const ushort CallbackIsVehicleWheel = 0x0040;

    /// <summary>COLLISION_GROUP_INTERACTIVE_DEBRIS.</summary>
    public const int GroupInteractiveDebris = (int)EngineCollisionGroup.InteractiveDebris;

    /// <summary>FVPHYSICS_PENETRATING.</summary>
    public const uint PenetratingGameFlag = 0x0040;

    private readonly IPhysicsSolverWorld _world;
    private readonly IVPhysicsSolverObjects _objects;
    private readonly PhysicsSolverRules _rules;
    private readonly EntityPairCollisions _pairs;
    private readonly PhysicsPenetrationLedger _penetration;
    private readonly IGameClock _clock;
    private readonly Action<PhysicsEvent> _post;
    private readonly IPhysicsCrushes _crushes;
    private readonly Func<int> _tickCount;
    private readonly Action<string> _developer;

    /// <param name="post">Where the step's entity events go (the router's Post), delivered at its next flush.</param>
    /// <param name="crushes">Where a freeze crush is queued (the step's damage queue).</param>
    /// <param name="tickCount">gpGlobals->tickcount.</param>
    /// <param name="developer">Prints under <c>developer 1</c> (stock's DevMsg / DevWarning).</param>
    public PhysicsSolver(IPhysicsSolverWorld world, IVPhysicsSolverObjects objects, PhysicsSolverRules rules,
                         EntityPairCollisions pairs, PhysicsPenetrationLedger penetration, IGameClock clock,
                         Action<PhysicsEvent> post, IPhysicsCrushes crushes, Func<int> tickCount,
                         Action<string> developer)
    {
        _tickCount = tickCount;
        _developer = developer;
        _world = world;
        _objects = objects;
        _rules = rules;
        _pairs = pairs;
        _penetration = penetration;
        _clock = clock;
        _post = post;
        _crushes = crushes;
    }

    /// <summary>The rules ShouldCollide evaluates.</summary>
    public PhysicsSolverRules Rules => _rules;

    /// <summary>The penetration ledger ShouldSolvePenetration keeps.</summary>
    public PhysicsPenetrationLedger Penetration => _penetration;

    /// <summary>The pair set ShouldCollide reads.</summary>
    public EntityPairCollisions Pairs => _pairs;

    // ---- the ask ledger's counters (physics_shell.h's SourceSharpCollisionAskLedger) ----

    private readonly long[] _refusals = new long[(int)CollisionRefusal.Count];

    /// <summary>ShouldCollide asks since the module started.</summary>
    public long CollideAsks { get; private set; }

    /// <summary>Of those, answered 0.</summary>
    public long CollideRefusals { get; private set; }

    /// <summary>ShouldCollide asks with an unowned side, answered 1 before any rule (C8's live check).</summary>
    public long CollideUnowned { get; private set; }

    /// <summary>Whether the last Decide met an unowned side (the counter above reads it).</summary>
    private bool _lastAskUnowned;

    /// <summary>Refusals by the clause that gave them.</summary>
    public long Refusals(CollisionRefusal why) => _refusals[(int)why];

    // ---- ShouldCollide --------------------------------------------------

    /// <summary>Who owns a physics object, as the answers see it.</summary>
    private struct Owner
    {
        public bool Valid;
        public bool World;
        public EntityHandle Handle;
        public SolverEntity Facts;

        public uint Key => World ? EntityPairCollisions.WorldKey : Handle.Value;
    }

    private Owner OwnerOf(nint physicsObject) => OwnerOf(physicsObject, _objects.GameData(physicsObject), _world.WorldObject());

    // The owner from the game data vphysics hands with the ask: the same word
    // GetGameData answers (vphysics hands pObject->GetGameData()),
    // so the ask costs no call to read it.
    private Owner OwnerOf(nint physicsObject, nint gameData, nint world)
    {
        var owner = default(Owner);
        if (physicsObject == 0)
            return owner;

        // The world first, by identity: its game data is NULL on purpose.
        if (world != 0 && physicsObject == world)
        {
            owner.Valid = true;
            owner.World = true;
            return owner;
        }

        // THE STATIC PROPS ARE THE WORLD'S (2026-10-12, plan_physics_bones_perf
        // C8): stock's PhysCreateWorld hands the world entity to
        // CreateVPhysicsRepresentations (physics.cpp:422), so its rule runs for
        // a static prop as for the world. Ours are made with NULL game data, so
        // a static object with none is the world's here; one that is not static
        // stays unowned, as stock's NULL entity is.
        if (gameData == 0 && _objects.IsStatic(physicsObject))
        {
            owner.Valid = true;
            owner.World = true;
            return owner;
        }

        EntityHandle handle = PhysicsGameData.Owner(gameData);
        if (handle != EntityHandle.None && _world.TryEntity(handle, out owner.Facts))
        {
            owner.Valid = true;
            owner.Handle = handle;
        }

        return owner;
    }

    // SourceSharp_FillCollisionSide: the owner's block and the object's answers.
    // The world is on neither entity arm: COLLISION_GROUP_NONE, SOLID_VPHYSICS,
    // solid, MOVETYPE_NONE, MASK_SOLID (physicsowner.h's neutral answers).
    private CollisionSide Side(in Owner owner, nint physicsObject)
    {
        if (!owner.Valid)
            return default;

        var side = new CollisionSide { Valid = true };
        if (owner.World)
        {
            side.Solid = PhysicsSolverRules.SolidVPhysics;
            side.MoveType = PhysicsSolverRules.MoveTypeNone;
            side.SolidMask = EnginePushWorld.MaskSolid;
        }
        else
        {
            side.CollisionGroup = owner.Facts.CollisionGroup;
            side.Solid = owner.Facts.Solid;
            side.SolidFlags = owner.Facts.SolidFlags;
            side.MoveType = owner.Facts.MoveType;
            side.SolidMask = owner.Facts.IsPlayer ? GameMovement.MaskPlayerSolid : EnginePushWorld.MaskSolid;
        }

        side.GameFlags = _objects.GameFlags(physicsObject);
        side.Contents = _objects.Contents(physicsObject);
        side.Moveable = _objects.IsMoveable(physicsObject);
        side.Trigger = _objects.IsTrigger(physicsObject);
        side.Shadow = _objects.HasShadowController(physicsObject);
        return side;
    }

    /// <summary>
    /// Stock's ShouldCollide: 1 when the pair may push each other apart. vphysics
    /// caches the answer per pair until RecheckCollisionFilter.
    /// </summary>
    public int ShouldCollide(nint object0, nint object1)
        => ShouldCollide(object0, object1, _objects.GameData(object0), _objects.GameData(object1));

    /// <summary>
    /// <see cref="ShouldCollide(nint, nint)"/> as vphysics asks it, with each
    /// object's game data (its <c>GetGameData</c>) handed in.
    /// </summary>
    public int ShouldCollide(nint object0, nint object1, nint gameData0, nint gameData1)
    {
        int answer = Decide(object0, object1, gameData0, gameData1, out CollisionRefusal why);

        CollideAsks++;
        if (_lastAskUnowned)
            CollideUnowned++;
        if (answer == 0)
        {
            CollideRefusals++;
            _refusals[(int)why]++;
        }

        return answer;
    }

    /// <summary>
    /// <see cref="ShouldCollide"/>'s answer without the ask ledger: what the
    /// cost bench (<c>cs_sv_physics_bench</c>) times, so timing never moves the
    /// counters a live check reads.
    /// </summary>
    public int Decide(nint object0, nint object1, out CollisionRefusal why)
        => Decide(object0, object1, _objects.GameData(object0), _objects.GameData(object1), out why);

    /// <summary>
    /// <see cref="Decide(nint, nint, out CollisionRefusal)"/> with each object's
    /// game data as vphysics hands it.
    /// </summary>
    public int Decide(nint object0, nint object1, nint gameData0, nint gameData1, out CollisionRefusal why)
    {
        nint world = _world.WorldObject();

        // Nobody owns one of them: the game has no rule for it, and stock
        // collides before it reads anything of either side (physics.cpp:470).
        Owner owner0 = OwnerOf(object0, gameData0, world);
        if (!owner0.Valid)
        {
            why = CollisionRefusal.Allowed;
            _lastAskUnowned = true;
            return 1;
        }

        Owner owner1 = OwnerOf(object1, gameData1, world);
        if (!owner1.Valid)
        {
            why = CollisionRefusal.Allowed;
            _lastAskUnowned = true;
            return 1;
        }

        CollisionSide side0 = Side(owner0, object0);
        CollisionSide side1 = Side(owner1, object1);

        var facts = default(CollisionPairFacts);
        facts.SameEntity = side0.Valid && side1.Valid && owner0.World == owner1.World && owner0.Key == owner1.Key;

        if (side0.Valid && side1.Valid && !facts.SameEntity)
        {
            // Vehicle wheels collide by their own rule (stock's WheelCollidesWith).
            if ((_objects.CallbackFlags(object0) & CallbackIsVehicleWheel) != 0 && !WheelCollidesWith(object1, owner1))
                facts.WheelRefused = true;
            if (!facts.WheelRefused && (_objects.CallbackFlags(object1) & CallbackIsVehicleWheel) != 0
                && !WheelCollidesWith(object0, owner0))
                facts.WheelRefused = true;

            // ForcesCollisionWith is the SDK's CFuncVPhysicsClip override, which
            // nothing on this hierarchy has: false, as physicsowner.h answers.
            facts.ForcedCollision = false;

            if (!owner0.World && !owner1.World)
            {
                // "Don't collide with your owner", when both have edicts.
                if (!owner0.Handle.IsInvisibleRange && !owner1.Handle.IsInvisibleRange)
                {
                    facts.OwnerPair = owner0.Facts.Owner == owner1.Handle || owner1.Facts.Owner == owner0.Handle;
                }

                // One root move parent: siblings and parents never collide.
                facts.SameRoot = RootOf(owner0.Handle, owner0.Facts) == RootOf(owner1.Handle, owner1.Facts);
            }

            facts.PairInHash = _pairs.IsDisabled(owner0.Key, owner1.Key);
        }

        _lastAskUnowned = false;
        return _rules.Evaluate(side0, side1, facts, out why);
    }

    /// <summary>
    /// The SS-PHYSWORLD report's question (native's SourceSharp_PhysWorldReport,
    /// physics.cpp at bc21e4d46): would the rules let this object rest on the
    /// world? Both sides as <see cref="ShouldCollide"/> builds them, the pair
    /// facts the report used (only "one entity"; no wheel, owner, root or pair
    /// rule), and the answer. Not counted: it is a diagnostic, not an ask.
    /// </summary>
    public WorldVerdict AgainstTheWorld(nint physicsObject)
    {
        nint world = _world.WorldObject();
        Owner owner = OwnerOf(physicsObject);
        Owner worldOwner = OwnerOf(world);

        CollisionSide side = Side(owner, physicsObject);
        CollisionSide worldSide = Side(worldOwner, world);

        var facts = default(CollisionPairFacts);
        facts.SameEntity = side.Valid && worldSide.Valid && owner.World == worldOwner.World
                           && owner.Key == worldOwner.Key;

        return new WorldVerdict(side, worldSide, facts.SameEntity, _rules.Evaluate(side, worldSide, facts, out _));
    }

    /// <summary>
    /// Stock's <c>PhysForceEntityToSleep</c> (physics.cpp) for one object: its
    /// velocity and contact strain cleared, then asleep. An object the solver
    /// does not move is left alone. The entity's list is one object on this
    /// hierarchy, so the object is the list.
    /// </summary>
    public void ForceEntityToSleep(nint physicsObject) => ForceToSleep(physicsObject);

    // WheelCollidesWith: not interactive debris, and pushed, simulated or static.
    private bool WheelCollidesWith(nint other, in Owner owner)
    {
        int group = owner.World ? 0 : owner.Facts.CollisionGroup;
        if (group == GroupInteractiveDebris)
            return false;

        bool push = !owner.World && owner.Facts.MoveType == PhysicsSolverRules.MoveTypePush;
        bool simulated = !owner.World;   // IsVPhysicsSimulated: an entity with an object
        return push || simulated || _objects.IsStatic(other);
    }

    // StandaloneEntity_RootParentHandle: up the parent chain while each parent
    // resolves; a chain deeper than 32 is a loop and ends where it is.
    private EntityHandle RootOf(EntityHandle handle, in SolverEntity facts)
    {
        EntityHandle root = handle;
        EntityHandle parent = facts.Parent;
        for (int i = 0; i < 32 && parent != EntityHandle.None; i++)
        {
            if (!_world.TryEntity(parent, out SolverEntity up))
                break;
            root = parent;
            parent = up.Parent;
        }

        return root;
    }

    // ---- ShouldSolvePenetration and the penetration ledger -----------------

    /// <summary>
    /// Between a level's pre-entity and post-entity init (CPhysicsHook's
    /// m_bPaused): entities are being made on the other side of a load, and
    /// every penetration is left to the solver.
    /// </summary>
    public bool Paused { get; private set; }

    /// <summary>ShouldSolvePenetration asks, before anything.</summary>
    public long PenetrationAsks { get; private set; }

    /// <summary>Of those, turned away by <see cref="Paused"/>.</summary>
    public long PenetrationPaused { get; private set; }

    /// <summary>Of those, a pair of two different owners: what reaches stock's rules.</summary>
    public long PenetrationPastGuard { get; private set; }

    private static bool IsDebris(int group)
        => group is (int)EngineCollisionGroup.Debris or (int)EngineCollisionGroup.InteractiveDebris
                 or (int)EngineCollisionGroup.DebrisTrigger;

    private static int GroupOf(in Owner owner) => owner.World ? 0 : owner.Facts.CollisionGroup;

    /// <summary>
    /// Stock's ShouldSolvePenetration (physics.cpp:980-1040): every pair of two
    /// owners is ledgered, both carry FVPHYSICS_PENETRATING while it lasts, and
    /// a pair stuck past the give-up time with no player or shadow on it is
    /// told not to solve once (and put to sleep by the next frame's update).
    /// The give-up is decided here, mid-step (user ruling 2026-10-11).
    /// </summary>
    public int ShouldSolvePenetration(nint object0, nint object1)
        => ShouldSolvePenetration(object0, object1, _objects.GameData(object0), _objects.GameData(object1));

    /// <summary>
    /// <see cref="ShouldSolvePenetration(nint, nint)"/> as vphysics asks it,
    /// with each object's game data handed in.
    /// </summary>
    public int ShouldSolvePenetration(nint object0, nint object1, nint gameData0, nint gameData1)
    {
        PenetrationAsks++;

        // This can be asked as entities are made on the other side of a load.
        if (Paused)
        {
            PenetrationPaused++;
            return 1;
        }

        nint world = _world.WorldObject();
        Owner owner0 = OwnerOf(object0, gameData0, world);
        Owner owner1 = OwnerOf(object1, gameData1, world);
        if (!owner0.Valid || !owner1.Valid || (owner0.World == owner1.World && owner0.Key == owner1.Key))
            return 1;   // unowned, or one entity with itself

        PenetrationPastGuard++;

        bool exempt = (!owner0.World && owner0.Facts.IsPlayer) || (!owner1.World && owner1.Facts.IsPlayer)
                      || _objects.HasShadowController(object0) || _objects.HasShadowController(object1);

        // Debris stuck in a non-static object (stock :1010): the entity solver
        // takes the pair after half a second (A9).
        bool debrisStuck = (IsDebris(GroupOf(owner0)) && !_objects.IsStatic(object1))
                           || (IsDebris(GroupOf(owner1)) && !_objects.IsStatic(object0));

        var ask = _penetration.Report(owner0.Key, owner1.Key, _clock.Now, exempt, debrisStuck);
        if (ask.Added)
        {
            SetPenetrating(owner0.Key, true);
            SetPenetrating(owner1.Key, true);
        }

        return ask.Solve ? 1 : 0;
    }

    /// <summary>
    /// Stock's UpdatePenetrateEvents (physics.cpp:837), once per frame after
    /// the step: stuck pairs are put to sleep, stuck debris is handed to the
    /// entity solver, quiet pairs are dropped and their flags cleared.
    /// </summary>
    public void FrameUpdate()
    {
        if (_penetration.Count == 0)
            return;

        var update = _penetration.Advance(_clock.Now, _bothAlive ??= (k0, k1) => Alive(k0) && Alive(k1));

        foreach (var pair in update.Sleep)
        {
            ForceToSleep(ObjectOfKey(pair.Key0));
            ForceToSleep(ObjectOfKey(pair.Key1));
        }

        // TRYENTITYSOLVER: which side is debris moved by physics, the fact
        // stock's swap reads; the solver decides the rest (A9).
        foreach (var pair in update.Solve)
        {
            if (ObjectOfKey(pair.Key0) == 0 || ObjectOfKey(pair.Key1) == 0)
                continue;

            uint flags = 0;
            if (DebrisNotPlayer(pair.Key0))
                flags |= PhysicsEvent.SolverDebris0;
            if (DebrisNotPlayer(pair.Key1))
                flags |= PhysicsEvent.SolverDebris1;

            _post(new PhysicsEvent
            {
                KindRaw = (uint)PhysicsEventKind.EntitySolver,
                Entity0 = pair.Key0,
                Entity1 = pair.Key1,
                Flags = flags,
            });
        }

        foreach (var pair in update.Removed)
        {
            SetPenetrating(pair.Key0, false);
            SetPenetrating(pair.Key1, false);
        }
    }

    private Func<uint, uint, bool>? _bothAlive;

    /// <summary>
    /// VPhysicsShadowUpdate's walk of PhysGetListOfPenetratingEntities (B7):
    /// whether anything this entity penetrates is a MOVETYPE_VPHYSICS mover.
    /// </summary>
    public bool PenetratedByMover(EntityHandle entity)
    {
        foreach (uint partner in _penetration.Partners(entity.Value))
        {
            var handle = new EntityHandle(partner);
            if (partner != EntityPairCollisions.WorldKey && _world.TryEntity(handle, out SolverEntity facts)
                && _world.PhysicsObjectOf(handle) != 0 && facts.MoveType == PhysicsSolverRules.MoveTypeVPhysics)
                return true;
        }

        return false;
    }

    private bool DebrisNotPlayer(uint key)
        => key != EntityPairCollisions.WorldKey && _world.TryEntity(new EntityHandle(key), out SolverEntity f)
           && IsDebris(f.CollisionGroup) && !f.IsPlayer;

    // A key's object is its entity's own (stock walks VPhysicsGetObjectList; a
    // managed entity has one). The world's is static: never flagged or slept.
    private nint ObjectOfKey(uint key)
        => key == EntityPairCollisions.WorldKey ? 0 : _world.PhysicsObjectOf(new EntityHandle(key));

    private bool Alive(uint key) => key == EntityPairCollisions.WorldKey || ObjectOfKey(key) != 0;

    // UpdateEntityPenetrationFlag (physics.cpp:800): non-static objects only.
    private void SetPenetrating(uint key, bool on)
    {
        nint o = ObjectOfKey(key);
        if (o == 0 || _objects.IsStatic(o))
            return;

        uint flags = _objects.GameFlags(o);
        _objects.SetGameFlags(o, on ? flags | PenetratingGameFlag : flags & ~PenetratingGameFlag);
    }

    // PhysForceEntityToSleep for one object: its velocity and contact strain
    // cleared, then asleep. Not one the solver does not move.
    private void ForceToSleep(nint o)
    {
        if (o == 0 || !_objects.IsMoveable(o))
            return;

        _objects.ClearVelocityAndStrain(o);
        _objects.Sleep(o);
    }

    // ---- ShouldFreezeObject and the crush ------------------------------------

    /// <summary>ShouldFreezeObject asks.</summary>
    public long FreezeAsks { get; private set; }

    /// <summary>Of those, debris crushed by what rests on it.</summary>
    public long FreezeCrushes { get; private set; }

    /// <summary>
    /// Stock's ShouldFreezeObject (physics.cpp:657-711, physics stock parity
    /// G): vphysics freezes an object that blew its per-object collision
    /// budget this tick. An AI mover is never frozen; debris that something
    /// heavy rests on is crushed by the contact pressing hardest (stock's
    /// FindMaxContact over the friction snapshot), DMG_CRUSH at a tenth of that
    /// force, queued as PhysCallbackDamage does. The entity decides what a
    /// crush does (a gib that cannot be damaged removes itself).
    /// </summary>
    public bool ShouldFreezeObject(nint physicsObject)
    {
        FreezeAsks++;

        Owner owner = OwnerOf(physicsObject);
        if (!owner.Valid || owner.World)
            return true;

        // For now, no per-object limit on MOVETYPE_PUSH objects.
        if (owner.Facts.MoveType == PhysicsSolverRules.MoveTypePush)
            return false;

        // IsNPC is false for every entity here.
        if (!IsDebris(owner.Facts.CollisionGroup))
            return true;

        float mass = _objects.Mass(physicsObject);
        if (MaxContact(physicsObject, mass) is { } found)
        {
            Owner other = OwnerOf(found.Other);
            EntityHandle attacker = other.Valid && !other.World ? other.Handle : EntityHandle.None;
            _crushes.AddCrush(owner.Handle, attacker, found.Force.Length * 0.1f, found.Force, found.Position);
            FreezeCrushes++;
        }

        return true;
    }

    // FindMaxContact over the object's friction snapshot: the moveable object,
    // heavier than this one, pressing past ten times its mass.
    private MaxContact? MaxContact(nint physicsObject, float mass)
    {
        Span<SnapshotContact> read = stackalloc SnapshotContact[32];
        int count = _objects.Contacts(physicsObject, read);
        if (count > read.Length)
        {
            read = new SnapshotContact[count];
            count = _objects.Contacts(physicsObject, read);
        }

        Span<FrictionContact> contacts = count <= 32 ? stackalloc FrictionContact[32] : new FrictionContact[count];
        for (int i = 0; i < count; i++)
        {
            SnapshotContact c = read[i];
            contacts[i] = new FrictionContact(c.Other, _objects.IsMoveable(c.Other), _objects.Mass(c.Other),
                                              c.NormalForce, c.Point, c.Normal);
        }

        return PhysicsMaxContact.Find(mass, mass * 10f, contacts[..count]);
    }

    // ---- ShouldFreezeContacts and the collision-check budget -----------------

    /// <summary>The per-tick collision-check budget vphysics may spend (stock's 1200).</summary>
    public const int CollisionCheckBudget = 1200;

    // The tick the large-friction warning last printed, as stock's
    // m_lastTickFrictionError: once per run of consecutive ticks.
    private int _lastTickFrictionError;

    /// <summary>ShouldFreezeContacts asks: a friction system vphysics found too large.</summary>
    public long LargeFrictionSystems { get; private set; }

    /// <summary>AdditionalCollisionChecksThisTick asks.</summary>
    public long BudgetAsks { get; private set; }

    /// <summary>Of those, granted nothing: ivp stopped checking for the tick.</summary>
    public long BudgetRefusals { get; private set; }

    /// <summary>The most checks any tick had done when it asked.</summary>
    public long WorstChecksDone { get; private set; }

    /// <summary>
    /// Stock's ShouldFreezeContacts (physics.cpp:741-768): a performance
    /// warning, printed once per run of consecutive ticks, and never a freeze.
    /// </summary>
    public bool ShouldFreezeContacts(int objectCount)
    {
        LargeFrictionSystems++;

        int tick = _tickCount();
        if (_lastTickFrictionError > tick || _lastTickFrictionError < tick - 1)
            _developer($"Performance Warning: large friction system ({objectCount} objects)!!!\n");
        _lastTickFrictionError = tick;
        return false;
    }

    /// <summary>
    /// Stock's AdditionalCollisionChecksThisTick (physics_collisionevent.h):
    /// what is left of the 1200-check budget, none past it, at which ivp stops
    /// checking and interpenetration may result.
    /// </summary>
    public int AdditionalCollisionChecksThisTick(int currentChecksDone)
    {
        int granted = currentChecksDone < CollisionCheckBudget ? CollisionCheckBudget - currentChecksDone : 0;

        BudgetAsks++;
        if (granted == 0)
            BudgetRefusals++;
        if (currentChecksDone > WorstChecksDone)
            WorstChecksDone = currentChecksDone;

        _developer(granted > 0
            ? "SERVER: VPhysics Collision detection getting expensive, check for too many convex pieces!\n"
            : $"SERVER: VPhysics exceeded collision check limit ({currentChecksDone})!!!\nInterpenetration may result!\n");
        return granted;
    }

    // ---- lifecycle --------------------------------------------------------

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsSolver;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName) => Paused = true;

    void IGameLifecycle.OnLevelInitPostEntity(string mapName) => Paused = false;

    void IGameLifecycle.OnLevelShutdown()
    {
        _pairs.Clear();
        _penetration.Clear();
    }
}
