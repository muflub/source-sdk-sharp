using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The movement loop's player-solid mask (<c>CGameMovement::PlayerSolidMask</c>,
/// gamemovement.cpp:715) as the rules' arm answers it: with no mod owning
/// movement the mask is the stock one on every trace the command makes, and a
/// mod's answer is the mask the walk carries (stock TF's
/// <c>CTFGameMovement::PlayerSolidMask</c>, tf_gamemovement.cpp:259-284).
/// </summary>
public sealed class PlayerSolidMaskRuleTests
{
    private const int InForward = 1 << 3;

    /// <summary>One bit no stock mask carries, so a fact can see the mod's answer land.</summary>
    private const uint ModBit = 0x4000_0000u;

    private sealed class WideMaskRules : IMovementRules
    {
        public MovementParams? Constants => null;

        public float JumpScale(in JumpRequest request) => 1f;

        public float FrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial)
            => friction;

        public float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
            => maxSpeed;

        public uint PlayerSolidMask(int playerIndex, uint mask, bool brushOnly) => mask | ModBit;
    }

    /// <summary>A world that answers every trace clear and writes down each mask.</summary>
    private sealed class MaskedWorld : IMoveWorld
    {
        public List<uint> Masks { get; } = new();

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                               uint mask, int collisionGroup)
        {
            Masks.Add(mask);
            return MoveTrace.Clear(end);
        }

        public int PointContents(in Vector point, int slot) => 0;

        public void ResetPointContentsCache(bool optimizations) { }

        public MoveSurface Surface(int surfaceProps) => new()
        {
            Valid = true, Friction = 0.8f, Material = 'C', Climbable = 0,
            MaxSpeedFactor = 1f, JumpFactor = 1f,
        };

        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        public bool PlayerFallingDamage(float damage) => true;
        public void PlayStepSound(in Vector o, int p, float v, bool f) { }
        public void UpdateStepSound(in Vector o, in Vector v, int p) { }
        public void Splash() { }
        public void RumbleEffect(byte i, byte d, byte f) { }
        public float EngineTime() => 100f;
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }
    }

    /// <summary>A standing player who walks this command, over <see cref="MaskedWorld"/>.</summary>
    private static List<uint> Walk(MovementRules rules)
    {
        MaskedWorld world = new();
        MoveState s = new()
        {
            CommandNumber = 1,
            FrameTime = 0.0625f,
            MoveType = GameMovement.MoveTypeWalk,
            Flags = GameMovement.FlOnGround,
            HasGround = true,
            Origin = Vector.Zero,
            Velocity = new Vector(0f, 100f, 0f),
            ViewAngles = new Vector(0f, 90f, 0f),
            MaxSpeed = 320f,
            ClientMaxSpeed = 320f,
            StepSize = 18f,
            ViewOffset = new Vector(0f, 0f, 64f),
            CurrentGravity = 600f,
            SurfaceFriction = 1f,
            Health = 100,
            IsAlive = true,
            LaggedMovementValue = 1f,
            ModelScale = 1f,
            EntIndex = 1,
            AllowAutoMovement = true,
            SurfaceDataValid = true,
            SurfaceProps = 3,
            IsServer = true,
            Buttons = InForward,
            ForwardMove = 100f,
        };

        new GameMovement(world, MoveConstantDefaults.Stock) { Rules = rules }.ProcessMovement(s);
        return world.Masks;
    }

    /// <summary>
    /// No mod owning movement, the mask is the stock one — the resolver's
    /// answer on the nose, and every trace the walked command makes.
    /// </summary>
    [Fact]
    public void NoModWalksTheStockMask()
    {
        var rules = new ServiceContainer().Create<MovementRulesResolver>().Rules;

        Assert.Equal(GameMovement.MaskPlayerSolid, rules.SolidMask(1, GameMovement.MaskPlayerSolid, false));
        Assert.Equal(GameMovement.MaskPlayerSolidBrushOnly,
                     rules.SolidMask(1, GameMovement.MaskPlayerSolidBrushOnly, true));

        List<uint> masks = Walk(rules);

        Assert.NotEmpty(masks);
        Assert.All(masks, mask => Assert.Equal(GameMovement.MaskPlayerSolid, mask));
    }

    /// <summary>
    /// A mod's mask is the walk's: the bit reaches the loop's traces, and
    /// nothing the command traces carries a mask the arm did not answer.
    /// </summary>
    [Fact]
    public void AModsMaskIsTheWalksMask()
    {
        var services = new ServiceContainer();
        services.AddInstance<IMovementRules>(new WideMaskRules());
        MovementRules rules = services.Create<MovementRulesResolver>().Rules;

        List<uint> masks = Walk(rules);

        uint wide = GameMovement.MaskPlayerSolid | ModBit;
        Assert.Contains(wide, masks);
        Assert.All(masks, mask => Assert.True(mask == GameMovement.MaskPlayerSolid || mask == wide,
            $"mask {mask} is neither the stock one nor the mod's over it"));
    }
}
