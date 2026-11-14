namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// TF2's body and movement as the derivation needs them (plan_bots_teams_tf2
/// C.1, C.7), for TF2's binding to publish through its <see cref="INavProfile"/>.
/// Plain data: the server binding references no game, so the numbers are the
/// TF2 mod's movement restated, each with where it is (src/tf2/shared/Movement,
/// src/tf2/shared/Combat); the binding that publishes them is where a fact holds
/// them to the mod's own constants.
/// <list type="bullet">
/// <item>One hull for every class (<c>TFMovementRules.Params</c>): 48 wide, 82
/// standing, 62 crouched; the step 18; gravity 800.</item>
/// <item>The ground jump leaves at 289 u/s (<c>TFViewVectors.JumpHeight</c>,
/// stock's <c>CheckJumpButton</c>), 289² / 1600 ≈ 52.2 u up; a plain jump
/// clears that and a step (70.2), a crouched one that and the 20 u the duck
/// tucks the feet up (72.2).</item>
/// <item>The Scout's air dash leaves at 268.33 u/s (<c>TFJump.DashSpeed</c>),
/// 45 u more from the first jump's top: a double jump clears 52.2 + 45 + 20 ≈ 117.2.</item>
/// <item>A landing under 650 u/s does no damage (<c>TFDamageDefs.PlayerMaxSafeFallSpeed</c>):
/// 650² / 1600 ≈ 264 u of safe drop.</item>
/// </list>
/// </summary>
public static class TF2NavNumbers
{
    public const float HullWidth = 48f, StandHeight = 82f, CrouchHeight = 62f, StepHeight = 18f, Gravity = 800f;

    /// <summary>The ground jump's take-off speed.</summary>
    public const float JumpSpeed = 289f;

    /// <summary>The Scout's air dash's take-off speed.</summary>
    public const float DashSpeed = 268.3281572999747f;

    /// <summary>The fastest landing that does no damage.</summary>
    public const float MaxSafeFallSpeed = 650f;

    /// <summary>TF's <c>TF_CLASS_SCOUT</c>: the one class with a double jump.</summary>
    public const int ScoutClass = 1;

    /// <summary>How high the ground jump rises.</summary>
    public static float JumpHeight => JumpSpeed * JumpSpeed / (2f * Gravity);

    /// <summary>How much higher the air dash takes a body from the top of the first jump.</summary>
    public static float DashHeight => DashSpeed * DashSpeed / (2f * Gravity);

    /// <summary>The derivation's numbers: TF's hull, step, jumps and safe drop, with the Scout's double jump as the extra reach.</summary>
    public static NavNumbers Numbers => new(
        HullWidth,
        StandHeight,
        CrouchHeight,
        StepHeight,
        JumpHeight + StepHeight,
        JumpHeight + (StandHeight - CrouchHeight),
        MaxSafeFallSpeed * MaxSafeFallSpeed / (2f * Gravity),
        JumpHeight + DashHeight + (StandHeight - CrouchHeight));

    /// <summary>The jump reaches of a player of TF class <paramref name="playerClass"/>: the Scout's double jump, else none.</summary>
    public static JumpReach ReachOf(int playerClass) => playerClass == ScoutClass ? JumpReach.Double : JumpReach.None;
}
