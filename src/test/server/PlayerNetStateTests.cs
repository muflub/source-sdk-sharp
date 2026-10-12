using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The standalone player's declared state (Phase 9a/9c5).
///
/// <para>
/// The offsets themselves are deliberately NOT pinned here any more: the
/// native sync resolves every field by NAME through NetStateFieldOffset at
/// first use, so a moved offset cannot drift — the ask is the guard. What CAN
/// drift silently is the rest of the contract: the names the native sync
/// resolves, and the tolerances, which are stock's numbers quoted from
/// c_baseentity.cpp and c_baseplayer.cpp and must not be re-invented.
/// </para>
/// </summary>
public sealed class PlayerNetStateTests
{
    /// <summary>
    /// The names the native halves resolve (shell_standalone_player*.cpp).
    /// A copy that FAILS rather than one that drifts: rename a field and this
    /// goes red while the sync would only warn once at first use.
    /// </summary>
    private static readonly string[] SyncedFields =
    {
        "Health", "MaxSpeed", "FallVelocity", "DuckTime", "DuckJumpTime",
        "JumpTime", "StepSize", "WaterJumpTime", "SurfaceFriction",
        "OldForwardMove", "Flags", "OldButtons", "TickBase", "GroundEntity",
        "Alive", "Ducked", "Ducking", "InDuckJump", "AllowAutoMovement",
        "WaterLevel", "WaterType", "MoveType",
        "Origin", "Velocity", "BaseVelocity", "ViewOffset",
        "PunchAngle", "PunchAngleVel", "LadderNormal", "WaterJumpVel",
    };

    [Fact]
    public void IsDeclaredForThePlayerClassname()
    {
        Assert.Equal(EntityNames.SsPlayer, PlayerNetState.NetLayout.ClassName);
    }

    [Fact]
    public void EveryFieldTheSyncResolvesExists()
    {
        var declared = PlayerNetState.NetLayout.Fields.Select(f => f.Name).ToHashSet();

        var missing = SyncedFields.Where(n => !declared.Contains(n)).ToArray();

        Assert.True(missing.Length == 0,
            "the native sync resolves fields that are no longer declared: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// Stock's tolerances, quoted not invented — the numbers themselves are
    /// the contract (plan_baseentity.md 9c3): origin is coordTolerance
    /// (2/2^COORD_FRACTIONAL_BITS), the rest are c_baseplayer.cpp's.
    /// </summary>
    [Fact]
    public void TheTolerancesAreStocks()
    {
        static float TolOf(string name) =>
            typeof(PlayerNetState).GetProperty(name)!
                .GetCustomAttribute<PredictedAttribute>()!.Tolerance;

        Assert.Equal(0.0625f, TolOf("Origin"));
        Assert.Equal(0.5f,    TolOf("Velocity"));
        Assert.Equal(0.05f,   TolOf("BaseVelocity"));
        Assert.Equal(0.25f,   TolOf("ViewOffset"));
        Assert.Equal(0.5f,    TolOf("MaxSpeed"));
        Assert.Equal(0.5f,    TolOf("FallVelocity"));
        Assert.Equal(0.125f,  TolOf("PunchAngle"));
        Assert.Equal(0.125f,  TolOf("PunchAngleVel"));

        // Health is server-authoritative, never predicted; the replay adopts
        // it. An attribute appearing here would put it on the ring for no
        // reason a tolerance could express.
        Assert.Null(typeof(PlayerNetState).GetProperty("Health")!
            .GetCustomAttribute<PredictedAttribute>());
        Assert.Null(typeof(PlayerNetState).GetProperty("Alive")!
            .GetCustomAttribute<PredictedAttribute>());
    }

    /// <summary>
    /// The one offset fact code still quotes directly: Health leads the
    /// declaration, so the first word of the class's own state is the HUD's
    /// number.
    ///
    /// <para>
    /// "Leads" is now relative to the ENGINE BLOCK (plan_entity.md §3.2), which
    /// occupies the front of every blob so that a shell getter is one load at a
    /// constant offset. Health is the first thing the class declares; it is not
    /// the first thing in the blob, and never can be again.
    /// </para>
    /// </summary>
    [Fact]
    public void HealthLeadsTheBlob()
    {
        Assert.Equal(EngineBlock.SizeBytes,
                     PlayerNetState.NetLayout.Fields.Single(f => f.Name == "Health").Offset);
    }

    /// <summary>
    /// FL_ONGROUND is a MIRROR, and the thing it mirrors has to be predicted
    /// too. Nothing in the movement loop reads the bit: the jump refusal
    /// (gamemovement.cpp:2475), the WalkMove/AirMove fork and the gravity
    /// zeroing all ask MvGetGroundEntity(). With Flags predicted and the
    /// handle not, the replay's basis paired a flags word from the
    /// acknowledged command with a ground handle from a later replayed one,
    /// and every jump mispredicted by the whole jump velocity.
    ///
    /// <para>
    /// Stock declares exactly this pair — c_baseplayer.cpp:403-405 predicts
    /// m_nTickBase, the flags and m_hGroundEntity together.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGroundEntityIsPredictedBesideTheFlagsWord()
    {
        var flags  = typeof(PlayerNetState).GetProperty("Flags");
        var ground = typeof(PlayerNetState).GetProperty("GroundEntity");

        Assert.NotNull(flags);
        Assert.NotNull(ground);

        Assert.NotNull(flags!.GetCustomAttribute<PredictedAttribute>());
        Assert.NotNull(ground!.GetCustomAttribute<PredictedAttribute>());
    }

    /// <summary>
    /// And compared EXACTLY. A ground entity is an identity, not a
    /// measurement: two handles name the same entity or they do not, so a
    /// tolerance on this field could only ever hide a real divergence — the
    /// failure mode the whole tolerance table is written to avoid.
    /// </summary>
    [Fact]
    public void TheGroundEntityCarriesNoTolerance()
    {
        Assert.Equal(0f,
            typeof(PlayerNetState).GetProperty("GroundEntity")!
                .GetCustomAttribute<PredictedAttribute>()!.Tolerance);
    }

    /// <summary>
    /// The player is predictable BY DECLARATION — the 9c5 gate: an entity
    /// class is ringed exactly when it declares at least one [Predicted]
    /// field, default no.
    /// </summary>
    [Fact]
    public void ThePlayerDeclaresItselfPredictable()
    {
        Assert.True(
            typeof(PlayerNetState).GetProperties()
                .Any(p => p.GetCustomAttribute<PredictedAttribute>() != null),
            "the player's state declares no [Predicted] field, so nothing would ring it");
    }
}
