using System;
using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Entities.Rooms;
using TF2.Shared;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Pushers;

/// <summary>
/// Facts for the push hook TF answers at (<see cref="TFPhysicsPushEntities"/>,
/// stock <c>CTFPhysicsPushEntities</c> in tf_pushentity.cpp): on an escort map
/// a mover shoves a TF player along the face its step hits and finishes the
/// step itself (:111-145), where the core's generic answer blocks the pusher
/// on the same player (<see cref="PhysicsPushedEntities"/>); and a train
/// pushing a player through a respawn room visualizer kills them instead of
/// carrying them through (:429-441).
/// </summary>
public sealed class TFPhysicsPushEntityFacts
{
    private const uint MoverBits = 0x0042_0001u;
    private const uint PlayerBits = 0x0042_0002u;

    /// <summary>
    /// A world of boxes for these facts: entities answer <see cref="IPushWorld"/>
    /// from boxes, brushes are boxes no one enumerates, and a sweep is the
    /// swept box against every other box and brush but itself — its contact
    /// place, the hit's face (<see cref="PushTrace.Normal"/>) and the hit
    /// (<see cref="PushTrace.Entity"/>), which is what the hook's slide reads.
    /// The stock arm traces from four units over the feet, so a mover whose box
    /// is a step below that height never enters the trace, as in stock.
    /// </summary>
    private sealed class PushRoomWorld : IPushWorld
    {
        private sealed class Box
        {
            public Vector Origin;
            public Vector Mins = Vector.Zero;
            public Vector Maxs = Vector.Zero;
            public EntityState State = EntityState.Solid;
            public int Index;
            public int Ground = -1;
        }

        private readonly Dictionary<EntityHandle, Box> _boxes = new();
        private readonly List<EntityHandle> _order = new();
        private readonly List<(Vector Mins, Vector Maxs)> _brushes = new();

        public EntityHandle AddMover(Vector origin, Vector mins, Vector maxs, int index)
        {
            var handle = new EntityHandle(MoverBits);
            _boxes[handle] = new Box { Origin = origin, Mins = mins, Maxs = maxs, Index = index };
            _order.Add(handle);
            return handle;
        }

        public EntityHandle AddPlayer(EntityHandle handle, Vector origin, int ground, int index)
        {
            _boxes[handle] = new Box
            {
                Origin = origin,
                Mins = new Vector(-16f, -16f, 0f),
                Maxs = new Vector(16f, 16f, 72f),
                State = EntityState.Solid | EntityState.Player,
                Index = index,
                Ground = ground,
            };
            _order.Add(handle);
            return handle;
        }

        public void AddBrush(Vector mins, Vector maxs) => _brushes.Add((mins, maxs));

        public Vector OriginOf(EntityHandle handle) => _boxes[handle].Origin;

        private static bool Overlaps((Vector Mins, Vector Maxs) a, (Vector Mins, Vector Maxs) b)
            => a.Mins.X < b.Maxs.X && a.Maxs.X > b.Mins.X
               && a.Mins.Y < b.Maxs.Y && a.Maxs.Y > b.Mins.Y
               && a.Mins.Z < b.Maxs.Z && a.Maxs.Z > b.Mins.Z;

        private bool OverlapsAnything(EntityHandle self, Vector at)
        {
            Box me = _boxes[self];
            var hull = (at + me.Mins, at + me.Maxs);

            foreach ((Vector mins, Vector maxs) brush in _brushes)
            {
                if (Overlaps(hull, (brush.mins, brush.maxs)))
                    return true;
            }

            foreach (EntityHandle other in _order)
            {
                if (other == self)
                    continue;

                Box b = _boxes[other];

                if ((b.State & EntityState.Solid) == 0)
                    continue;

                if (Overlaps(hull, (b.Origin + b.Mins, b.Origin + b.Maxs)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The face of the first thing the hull meets at <paramref name="at"/>,
        /// as <see cref="EnginePushWorld"/> derives it: of the three axes the
        /// smallest overlap is the face's axis, and the normal points toward the
        /// hull. A brush answers as the world itself, entry 0 — the hit the
        /// hook slides along is never "nothing".
        /// </summary>
        private void FindHit(EntityHandle self, Vector at, out Vector normal, out EntityHandle hit)
        {
            normal = Vector.Zero;
            hit = EntityHandle.None;

            Box me = _boxes[self];
            var hull = (at + me.Mins, at + me.Maxs);

            foreach ((Vector mins, Vector maxs) brush in _brushes)
            {
                (Vector lo, Vector hi) box = (brush.mins, brush.maxs);

                if (!Overlaps(hull, box))
                    continue;

                normal = FaceOf(hull, box);
                hit = new EntityHandle(0u);
                return;
            }

            foreach (EntityHandle other in _order)
            {
                if (other == self)
                    continue;

                Box b = _boxes[other];

                if ((b.State & EntityState.Solid) == 0)
                    continue;

                (Vector lo, Vector hi) box = (b.Origin + b.Mins, b.Origin + b.Maxs);

                if (!Overlaps(hull, box))
                    continue;

                normal = FaceOf(hull, box);
                hit = other;
                return;
            }
        }

        private static Vector FaceOf((Vector Mins, Vector Maxs) hull, (Vector Mins, Vector Maxs) box)
        {
            float ox = MathF.Min(hull.Maxs.X, box.Maxs.X) - MathF.Max(hull.Mins.X, box.Mins.X);
            float oy = MathF.Min(hull.Maxs.Y, box.Maxs.Y) - MathF.Max(hull.Mins.Y, box.Mins.Y);
            float oz = MathF.Min(hull.Maxs.Z, box.Maxs.Z) - MathF.Max(hull.Mins.Z, box.Mins.Z);

            static float Side(float hullCentre, float boxCentre) => hullCentre >= boxCentre ? 1f : -1f;

            if (ox <= oy && ox <= oz)
                return new Vector(Side((hull.Mins.X + hull.Maxs.X) * 0.5f, (box.Mins.X + box.Maxs.X) * 0.5f), 0f, 0f);

            if (oy <= ox && oy <= oz)
                return new Vector(0f, Side((hull.Mins.Y + hull.Maxs.Y) * 0.5f, (box.Mins.Y + box.Maxs.Y) * 0.5f), 0f);

            return new Vector(0f, 0f, Side((hull.Mins.Z + hull.Maxs.Z) * 0.5f, (box.Mins.Z + box.Maxs.Z) * 0.5f));
        }

        public int EntitiesInBox(Vector mins, Vector maxs, Span<EntityHandle> into)
        {
            int kept = 0;

            foreach (EntityHandle h in _order)
            {
                Box b = _boxes[h];
                Vector lo = b.Origin + b.Mins;
                Vector hi = b.Origin + b.Maxs;

                if (lo.X <= maxs.X && hi.X >= mins.X && lo.Y <= maxs.Y && hi.Y >= mins.Y
                    && lo.Z <= maxs.Z && hi.Z >= mins.Z && kept < into.Length)
                {
                    into[kept++] = h;
                }
            }

            return kept;
        }

        public EntityState State(EntityHandle entity, out MoveType moveType)
        {
            moveType = MoveType.Walk;
            return _boxes[entity].State;
        }

        public SolidFlags SolidFlags(EntityHandle entity) => SourceSharp.Abi.SolidFlags.None;
        public SolidType SolidType(EntityHandle entity) => SourceSharp.Abi.SolidType.Bsp;
        public int CollisionGroup(EntityHandle entity) => 0;
        public Vector Origin(EntityHandle entity) => _boxes[entity].Origin;
        public QAngle Angles(EntityHandle entity) => QAngle.Zero;
        public (Vector Mins, Vector Maxs) Bounds(EntityHandle entity) => (_boxes[entity].Mins, _boxes[entity].Maxs);

        public bool CollisionBox(EntityHandle entity, out Vector mins, out Vector maxs)
        {
            mins = _boxes[entity].Mins;
            maxs = _boxes[entity].Maxs;
            return true;
        }

        public int GroundIndex(EntityHandle entity) => _boxes[entity].Ground;
        public int Index(EntityHandle entity) => _boxes[entity].Index;
        public void SetOrigin(EntityHandle entity, Vector origin) => _boxes[entity].Origin = origin;
        public void SetAngles(EntityHandle entity, QAngle angles) { }
        public void MarkRotPushed(EntityHandle player) { }

        /// <summary>
        /// The hull from <paramref name="start"/> to <paramref name="end"/> against
        /// every box and brush but its own: starting inside is fraction zero;
        /// otherwise the last clear place along the way is the contact, with the
        /// hit's face and entity read just past it.
        /// </summary>
        public PushTrace Sweep(EntityHandle entity, Vector start, Vector end)
        {
            if (OverlapsAnything(entity, start))
                return new PushTrace(0f, start, true, OverlapsAnything(entity, end));

            float clear = 0f;
            const int Steps = 256;

            for (int i = 1; i <= Steps; i++)
            {
                float t = i / (float)Steps;

                if (OverlapsAnything(entity, start + (end - start) * t))
                {
                    float lo = clear, hi = t;

                    for (int k = 0; k < 30; k++)
                    {
                        float mid = (lo + hi) * 0.5f;

                        if (OverlapsAnything(entity, start + (end - start) * mid))
                            hi = mid;
                        else
                            lo = mid;
                    }

                    FindHit(entity, start + (end - start) * hi, out Vector normal, out EntityHandle hit);

                    return new PushTrace(lo, start + (end - start) * lo, false, false, normal, hit);
                }

                clear = t;
            }

            return new PushTrace(1f, end, false, false);
        }
    }

    /// <summary>The rooms seam, recording every <c>PointsCrossRespawnRoomVisualizer</c> ask.</summary>
    private sealed class CrossRooms : ITFRespawnRooms
    {
        public readonly List<(Vector Start, Vector End, int TeamToIgnore)> Asks = new();

        public bool Answers = true;

        public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly) => false;

        public bool PointInRespawnRoom(Vector point) => false;

        public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly) => false;

        public int TouchCount(EntityHandle player) => 0;

        public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0)
        {
            Asks.Add((start, end, teamToIgnore));
            return Answers;
        }
    }

    /// <summary>
    /// The cart geometry: a mover whose box is a step against the player's
    /// hull (so the hook's gate sees a collision, and the +4 trace does not),
    /// the player ahead of it, and the wall face the slide ends against.
    /// </summary>
    private static void BuildTrack(PushRoomWorld world)
    {
        world.AddMover(new Vector(-6f, 0f, 0f), new Vector(-8f, -8f, 0f), new Vector(9.5f, 8f, 0.5f), index: 1);
        world.AddPlayer(new EntityHandle(PlayerBits), new Vector(10f, 0f, 0f), ground: -1, index: 2);
        world.AddBrush(new Vector(36f, -64f, 0f), new Vector(52f, 64f, 128f));
    }

    private static RecordingEngine TrainEngine()
    {
        var engine = TestServices.Fake();
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.GetClassName", "func_tracktrain");
        return engine;
    }

    /// <summary>The escort game type's flag, on: what a payload map's rule reads.</summary>
    private static TFGameModePayloadConVar Escort()
    {
        var payload = DeclaredConVars.Fresh<TFGameModePayloadConVar>();
        payload.NotifyChanged("1", "0");
        return payload;
    }

    [Fact]
    public void TheHookSlidesThePlayerAlongTheWallItHits_AndThePushIsNeverBlockedByThem()
    {
        var world = new PushRoomWorld();
        var mover = new EntityHandle(MoverBits);
        var player = new EntityHandle(PlayerBits);
        BuildTrack(world);

        var hook = new TFPhysicsPushEntities(world, TrainEngine(), new CrossRooms { Answers = false },
                                             new FakeMapPlayers(), TrainEngine(), Escort());
        var pushed = new PhysicsPushedEntities(world, new RecordingLog()) { Hook = hook };

        Assert.Equal(1, pushed.BeginLinear(mover, null, new Vector(-6f, 0f, 0f), new Vector(24f, 10f, 0f),
                                           QAngle.Zero, unblockableByPlayer: false));
        Assert.Equal(player, pushed.PushedAt(0));

        // The hook answered: the generic finish's re-check is skipped, as stock's
        // player arm clears m_bBlocked itself (tf_pushentity.cpp:358).
        Assert.Equal(EntityHandle.None, pushed.Finish());

        Vector at = world.OriginOf(player);
        Assert.Equal(20f, at.X, 2);                    // hull against the wall face
        Assert.Equal(10.34f, at.Y, 2);                 // slid along it to the push's own end
        Assert.Equal(2.67f, at.Z, 2);                  // the trace lerps from the raised start back to the feet
    }

    [Fact]
    public void WithNoHookTheSamePushBlocksOnThePlayer_AndNothingIsMoved()
    {
        var world = new PushRoomWorld();
        var mover = new EntityHandle(MoverBits);
        var player = new EntityHandle(PlayerBits);
        BuildTrack(world);

        var pushed = new PhysicsPushedEntities(world, new RecordingLog());

        Assert.Equal(1, pushed.BeginLinear(mover, null, new Vector(-6f, 0f, 0f), new Vector(24f, 10f, 0f),
                                           QAngle.Zero, unblockableByPlayer: false));
        Assert.Equal(player, pushed.PushedAt(0));

        // The generic answer: the sweep starts inside the mover's box where the
        // player stands, so the blocker is named and everything is put back.
        Assert.Equal(player, pushed.Finish());

        Vector at = world.OriginOf(player);
        Assert.Equal(10f, at.X, 2);
        Assert.Equal(0f, at.Y, 2);
    }

    [Fact]
    public void WithTheEscortFlagOffTheHookLeavesTheGenericAnswer()
    {
        var world = new PushRoomWorld();
        var mover = new EntityHandle(MoverBits);
        var player = new EntityHandle(PlayerBits);
        BuildTrack(world);

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(TFPhysicsPushEntities).Assembly);

        var hook = new TFPhysicsPushEntities(world, TrainEngine(), new CrossRooms(), new FakeMapPlayers(),
                                             TrainEngine(), convars.Declared<TFGameModePayloadConVar>());
        var pushed = new PhysicsPushedEntities(world, new RecordingLog()) { Hook = hook };

        Assert.Equal(1, pushed.BeginLinear(mover, null, new Vector(-6f, 0f, 0f), new Vector(24f, 10f, 0f),
                                           QAngle.Zero, unblockableByPlayer: false));

        // tf_gamemode_payload at its default (off): the generic check answers,
        // which blocks on the player rather than moving them (:113-115).
        Assert.Equal(player, pushed.Finish());

        Vector at = world.OriginOf(player);
        Assert.Equal(10f, at.X, 2);
        Assert.Equal(0f, at.Y, 2);
    }

    [Fact]
    public void ATrainPushingAPlayerThroughARespawnRoomVisualizerKillsThemWhereTheCartLeavesThem()
    {
        var world = new PushRoomWorld();
        var mover = new EntityHandle(MoverBits);
        var player = new EntityHandle(PlayerBits);
        BuildTrack(world);

        var engine = TrainEngine();
        var rooms = new CrossRooms();
        var players = new FakeMapPlayers();

        // The hook's team ask reaches the map-player table, which answers 0 for
        // a stranger — the ignore nobody skips (tf_pushentity.cpp:432).
        var hook = new TFPhysicsPushEntities(world, engine, rooms, players, engine, Escort());

        Assert.True(hook.SpeculativelyCheckLinearPush(mover, player, new Vector(30f, 10f, 0f)));

        EngineCall? kill = engine.Last("Damage.Apply");
        Assert.NotNull(kill);
        Assert.Equal(player, (EntityHandle)kill!.Value.Arg(0)!);
        Assert.Equal(EntityHandle.None, (EntityHandle)kill.Value.Arg(1)!);   // nobody as the attacker
        Assert.Equal(PlayerSelfCommand.SuicideDamage, kill.Value.ArgAs<float>(2));
        Assert.Equal(DamageType.Generic, kill.Value.ArgAs<DamageType>(3));

        // The clip's ask: from four over the feet to where the first trace
        // ended, the blocker's team as the one to ignore (:429-441). The arm's
        // second MovePlayer asks again (:348-354): two identical asks.
        Assert.Equal(2, rooms.Asks.Count);
        (Vector start, Vector end, int ignore) = rooms.Asks[0];
        Assert.Equal(10f, start.X, 2);
        Assert.Equal(4f, start.Z, 2);
        Assert.Equal(20f, end.X, 2);
        Assert.Equal(3.33f, end.Y, 2);
        Assert.Equal(2.67f, end.Z, 2);
        Assert.Equal(0, ignore);

        // And the kill returns where the blocker stands: no half-move is left
        // behind it (the :443 move is after the :438 return).
        Assert.Equal(10f, world.OriginOf(player).X, 2);
        Assert.Equal(0f, world.OriginOf(player).Y, 2);
    }

    /// <summary>The rotating arm of stock: riding the mover, the push is upward only.</summary>
    [Fact]
    public void ARidingPlayerIsLiftedByThePitchOfThePushersTurn_AndNotSideways()
    {
        var world = new PushRoomWorld();
        var mover = new EntityHandle(MoverBits);
        var player = new EntityHandle(PlayerBits);
        world.AddMover(new Vector(0f, 0f, 0f), new Vector(-32f, -32f, 0f), new Vector(32f, 32f, 68f), index: 1);
        world.AddPlayer(player, new Vector(0f, 0f, 64f), ground: 1, index: 2);

        var hook = new TFPhysicsPushEntities(world, TrainEngine(), new CrossRooms { Answers = false },
                                             new FakeMapPlayers(), TrainEngine(), Escort());

        // amove.x = 45° of pitch: the rim of the turn lifts the rider by
        // tan(45°) * radius, plus stock's ten percent (:204-213). The rider's
        // own hull traces clear of the mover, so the full lift lands in one
        // short try and the rider stays inside the turn's column.
        Assert.True(hook.SpeculativelyCheckRotPush(mover, player, new QAngle(45f, 0f, 0f)));

        Vector at = world.OriginOf(player);
        // pusher radius |(64,64,68)|/2 ≈ 56.60, times tan(45°)=1 and 1.1, times
        // the check's 0.35 scale: 64 + 21.79.
        Assert.Equal(0f, at.X, 2);
        Assert.Equal(0f, at.Y, 2);
        Assert.Equal(85.79f, at.Z, 2);
    }
}
