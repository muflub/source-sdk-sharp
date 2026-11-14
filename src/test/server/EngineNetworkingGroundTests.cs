using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHAT AN ENTITY IS, as the server's networking seam answers it over the
/// unattached side (plan_server_native_v2 T4g): the move type is the managed
/// record's, and the ground is the engine side's own live flag — stock's blast
/// reads the victim's flags at the hit (tf_player.cpp:10266-10273), and a
/// record published inside the Move stage still carries the ground the last
/// command found while the player rides the arc above it.
/// </summary>
public sealed unsafe class EngineNetworkingGroundTests
{
    private static readonly EntityHandle Player = new(0x0003_0002u);

    /// <summary>The entities area answering one bit pair for the player and forwarding the rest to the default.</summary>
    private sealed class LiveAnswer(EntityState answer, MoveType answerMove) : DefaultEntitiesArea, IEntitiesServerArea
    {
        private readonly DefaultEntitiesServerArea _rest = new();

        public int EntityIsPlayer(uint a0) => a0 == Player.Value ? 1 : 0;

        public int EntityGetState(uint a0, int* a1)
        {
            if (a1 is not null)
                *a1 = (int)answerMove;

            return (int)answer;
        }

        public void EntityGetClassName(uint a0, byte* a1, int a2) => _rest.EntityGetClassName(a0, a1, a2);
        public void EntityGetName(uint a0, byte* a1, int a2) => _rest.EntityGetName(a0, a1, a2);
        public uint FindEntityByClassname(uint a0, byte* a1) => _rest.FindEntityByClassname(a0, a1);
        public int EntitiesInSphere(float* a0, float a1, uint* a2, int a3) => _rest.EntitiesInSphere(a0, a1, a2, a3);
        public int EntitySetKeyValue(uint a0, byte* a1, byte* a2) => _rest.EntitySetKeyValue(a0, a1, a2);
        public void EntityGetSpawnOrigin(uint a0, float* a1) => _rest.EntityGetSpawnOrigin(a0, a1);
        public void EntityGetSpawnAngles(uint a0, float* a1) => _rest.EntityGetSpawnAngles(a0, a1);
        public int EntityGetSpawnFlags(uint a0) => _rest.EntityGetSpawnFlags(a0);
        public void EntitySetSpawnFlags(uint a0, int a1) => _rest.EntitySetSpawnFlags(a0, a1);
        public int EntityGetHealth(uint a0) => _rest.EntityGetHealth(a0);
        public void EntitySetHealth(uint a0, int a1) => _rest.EntitySetHealth(a0, a1);
        public void EntitySetVisible(uint a0, int a1) => _rest.EntitySetVisible(a0, a1);
        public void EntitySetOwner(uint a0, uint a1) => _rest.EntitySetOwner(a0, a1);
        public void EntitySetClassName(uint a0, byte* a1) => _rest.EntitySetClassName(a0, a1);
        public int EntitiesStandalonePlayerSkyboxArea(int a0, int* a1) => _rest.EntitiesStandalonePlayerSkyboxArea(a0, a1);
    }

    /// <summary>The route answering one record's move type and ground, and ignoring the writes.</summary>
    private sealed class RecordRoute(MoveType heldMove, bool heldGround) : IServerPlayerMovementRoute
    {
        public bool PlayerState(EntityHandle entity, out MoveType moveType, out bool onGround)
        {
            moveType = heldMove;
            onGround = heldGround;
            return true;
        }

        public MoveType SetMoveType(EntityHandle player, MoveType moveType) => moveType;
        public void SetSlowMovement(EntityHandle player, bool slow) { }
        public void SetSuitPower(EntityHandle player, float power) { }
        public void SwallowUse(EntityHandle player) { }
        public void QueueUseDenySound(EntityHandle player) { }
        public bool TakeUseDenySound(EntityHandle player) => false;
        public void SetObserverMode(EntityHandle player, int mode) { }
        public bool AddBaseVelocity(EntityHandle entity, Vector velocity) => false;
        public bool ApplyVelocityImpulse(EntityHandle entity, Vector impulse, bool clearGround) => false;
        public bool Velocity(EntityHandle entity, out Vector velocity)
        {
            velocity = Vector.Zero;
            return false;
        }

        public bool SetVelocity(EntityHandle entity, Vector velocity) => false;
        public bool SmoothedVelocity(EntityHandle entity, out Vector velocity)
        {
            velocity = Vector.Zero;
            return false;
        }

        public bool SetPunch(EntityHandle entity, Vector punch, Vector velocity) => false;
        public Vector Punch(EntityHandle entity) => Vector.Zero;
        public void ViewPunch(Vector center, Vector punch, float radius, bool inAir) { }
        public void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate, int start = 0) { }
        public void SetDefaultFov(EntityHandle player, int fov) { }
    }

    private static IEngineNetworking Networking(EntityState live, MoveType liveMove,
                                                 MoveType heldMove, bool heldGround)
    {
        // The side over the generated default areas with the entities slot
        // answered by the fact's own, as TestSides builds it; the binding is
        // the one ServerModule registers (SideBindings.Of, phase 6).
        var areas = new ServiceContainer();
        ServerAreaDefaults.AddTo((type, area) => areas.AddInstance(type, area));
        areas.AddInstance(typeof(IEntitiesServerArea), new LiveAnswer(live, liveMove));

        EngineSide side = EngineSide.Over(SideAttachment.Over(Side.Server, new SideState(Side.Server), areas));
        EngineServerBinding.Bind(side.Attachment);
        DefaultForwarders.HoldOn(side);

        var route = new SideRoute<IServerPlayerMovementRoute>();
        route.Fill(new RecordRoute(heldMove, heldGround));

        return SideBindings.Of<IEngineNetworking>(side, route);
    }

    /// <summary>
    /// THE LIVE FLAG ANSWERS THE GROUND THROUGH THE SEAM. An airborne soldier
    /// whose record still says standing (the command published inside the
    /// Move stage) reads airborne at the blast, so the self blast takes the
    /// rocket-jump scale rather than the grounded hop's.
    /// </summary>
    [Fact]
    public void TheGroundIsTheEngineSidesLiveAnswerWhenTheRecordStillSaysStanding()
    {
        IEngineNetworking networking = Networking(EntityState.Player, MoveType.None, MoveType.Walk, heldGround: true);

        EntityState state = networking.EntityGetState(Player, out _);

        Assert.False(state.HasFlag(EntityState.OnGround));
    }

    /// <summary>
    /// THE SAME BOTH WAYS DOWN: a standing soldier whose record lost the
    /// ground bit still reads standing, because the live flag is the answer.
    /// </summary>
    [Fact]
    public void TheGroundIsTheEngineSidesLiveAnswerWhenTheRecordSaysAirborne()
    {
        IEngineNetworking networking = Networking(EntityState.Player | EntityState.OnGround, MoveType.None,
                                                   MoveType.Walk, heldGround: false);

        EntityState state = networking.EntityGetState(Player, out _);

        Assert.True(state.HasFlag(EntityState.OnGround));
    }

    /// <summary>
    /// THE MOVE TYPE STAYS THE RECORD'S: the engine side's own word for it
    /// loses to what the managed command loop left (plan_server_native_v2 T4g).
    /// </summary>
    [Fact]
    public void TheMoveTypeIsStillTheManagedRecords()
    {
        IEngineNetworking networking = Networking(EntityState.Player, MoveType.None, MoveType.Walk, heldGround: false);

        networking.EntityGetState(Player, out MoveType moveType);

        Assert.Equal(MoveType.Walk, moveType);
    }
}
