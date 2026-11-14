using SourceSharp.Abi;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>Where a pilot is in a jump.</summary>
public enum JumpPhase : byte
{
    /// <summary>On the ground before the jump: turning the view to the shot's and running at the takeoff.</summary>
    Approach = 0,

    /// <summary>From the first of the jump's and the trigger's commands to the landing: the jump, the shot and the flight.</summary>
    Launch = 1,

    /// <summary>Back on the ground: everything released.</summary>
    Landed = 2,
}

/// <summary>What a pilot is told of the Soldier each command: where he is, how he moves, where he looks and whether he stands on something.</summary>
public readonly record struct JumpBody(Vector Origin, Vector Velocity, Vector View, bool OnGround);

/// <summary>
/// What a pilot carries from one command to the next, the caller holding it:
/// the phase, the commands sent since the launch began, whether the body has
/// been off the ground since, whether its own blast has been felt, the
/// speed it had a command ago (a blast is felt as a leap in it), and, for a
/// recipe with a second rocket, the count of the command its trigger was
/// pressed on and one more (0 while it has not been).
/// <c>default</c> is a jump not yet begun.
/// </summary>
public readonly record struct JumpPilotState(JumpPhase Phase, int Count, bool Airborne, bool Felt, Vector LastVelocity, int Second = 0);

/// <summary>
/// The numbers a pilot flies by, none of them a simulator's: how fast it may
/// turn the view, the command's length, the class's run speed, the air's
/// rules (the acceleration and the cap on the speed it is measured against),
/// and the move a held key sends.
/// </summary>
/// <param name="TurnDegreesASecond">The fastest the view is asked to turn, pitch and yaw together.</param>
/// <param name="TickSeconds">One command's length.</param>
/// <param name="RunSpeed">The class's speed on the ground (the wish speed's ceiling in the air too).</param>
/// <param name="AirAccelerate">The air's acceleration (<c>sv_airaccelerate</c>).</param>
/// <param name="AirSpeedCap">The speed along the wish direction past which the air adds nothing.</param>
/// <param name="FullMove">The move a held key sends (<c>cl_forwardspeed</c>).</param>
public readonly record struct JumpPilotLimits(float TurnDegreesASecond, float TickSeconds, float RunSpeed, float AirAccelerate, float AirSpeedCap,
                                              float FullMove = JumpPilot.KeyMove)
{
    /// <summary>The mod's movement (<see cref="TFViewVectors"/>) for a class that runs at <paramref name="runSpeed"/>.</summary>
    public static JumpPilotLimits Of(in MovementParams body, float runSpeed, float tickSeconds, float turnDegreesASecond)
        => new(turnDegreesASecond, tickSeconds, runSpeed, body.AirAccelerate, body.AirSpeedCap);
}

/// <summary>One command of a pilot: the usercmd, the state to hand back with the next command, and the move it wants in the world's frame.</summary>
/// <param name="Command">The view, the moves (in that view's frame) and the buttons.</param>
/// <param name="Next">What the caller hands back with the next command.</param>
/// <param name="Wish">
/// The same move in the world's frame, its length the move's: a caller whose
/// view does not get where <paramref name="Command"/> asks (a motion layer in
/// between) turns this into the moves of the view it does send.
/// </param>
public readonly record struct JumpPilotStep(JumpCommand Command, JumpPilotState Next, Vector Wish);

/// <summary>
/// Flies a <see cref="JumpRecipe"/> a command at a time
/// (plan_bots_teams_tf2_part_g G.5 step 2): a pure function from the recipe,
/// the pilot's own small state and the Soldier's to that command's buttons,
/// view and moves. It knows no simulator and no engine, allocates nothing and
/// keeps nothing: the same inputs give the same command, so the search flies it
/// in the simulator and a live controller calls it with the game's player.
///
/// <list type="bullet">
/// <item><b>Approach.</b> The view turns to the shot's, never faster than
/// <see cref="JumpPilotLimits.TurnDegreesASecond"/>. A recipe with a run-up
/// runs along the direction of travel at the takeoff's line (the line through
/// the takeoff across that direction), setting off once the turn left takes no
/// longer than the run left; one without stands. At the line with the view
/// there, it launches; at the line without, it waits.</item>
/// <item><b>Launch.</b> The jump (with the duck, held to the landing, when the
/// recipe is crouched) and the trigger, <see cref="JumpRecipe.ShotDelay"/>
/// commands apart; the view holds the shot's until the trigger is let go.</item>
/// <item><b>Flight.</b> Once its own blast is felt (or has had time to come),
/// it steers for the landing: the speed over the ground it wants is toward the
/// landing, no more than it has (the air gives no more along the way it goes)
/// and no more than it can still brake from, and the wish direction is the one
/// whose air acceleration, by the game's own <see cref="MoveMath.AirAccelerate"/>,
/// takes its speed nearest that. So it turns its path, brakes over the landing
/// and holds there while it falls. The moves are whatever, in the view's
/// frame, make that wish: the view itself turns back to the way it goes.</item>
/// <item><b>A second rocket</b> (a recipe's <see cref="JumpRecipe.Second"/>, lane G7b). Once the first
/// trigger is let go the view turns to the second shot's; the trigger is pressed again, in the air, no
/// sooner than <see cref="JumpSecondShot.Earliest"/> commands after the first and no later than
/// <see cref="JumpSecondShot.Latest"/>, once the body has come over <see cref="JumpSecondShot.Over"/>
/// along the way; only with the view within <see cref="SecondAimDegrees"/> of the shot's and the body
/// over the floor it is fired at. A body not over the place or a view not there by then fires nothing:
/// the flight goes on as a single jump. Then the view turns back to the way it goes.</item>
/// <item><b>Landed.</b> Nothing held, the view turning level.</item>
/// </list>
/// </summary>
public static class JumpPilot
{
    /// <summary>The move a held key sends: TF2's <c>cl_forwardspeed</c> and <c>cl_sidespeed</c>.</summary>
    public const float KeyMove = 450f;

    /// <summary>How near the shot's view counts as there, degrees.</summary>
    public const float AimDegrees = 0.25f;

    /// <summary>How near the takeoff's line counts as on it, beyond half a command's travel.</summary>
    public const float LineSlack = 1f;

    /// <summary>Commands the trigger is held for.</summary>
    public const int TriggerCommands = 2;

    /// <summary>How near the second shot's view the view must be for it to be fired, degrees: what a search's trials put a view off by.</summary>
    public const float SecondAimDegrees = 3f;

    /// <summary>A leap in speed over one command that is the body's own blast, units a second.</summary>
    public const float BlastFeltSpeed = 100f;

    /// <summary>Commands after the trigger by which a blast that is coming has come: the flight is steered from then whatever was felt.</summary>
    public const int BlastWaitCommands = 16;

    /// <summary>Commands after the jump's by which a body still on the ground is not going to leave it.</summary>
    public const int GroundedCommands = 4;

    /// <summary>The share of the air's braking a flight plans to stop with: the rest is margin.</summary>
    public const float BrakeShare = 0.5f;

    /// <summary>Commands over which the last of the way to the landing is closed.</summary>
    public const float HoldCommands = 4f;

    /// <summary>Wish directions tried each command of a steered flight, round the circle.</summary>
    public const int WishDirections = 16;

    private const int InAttack = GameMovement.InAttack, InJump = GameMovement.InJump, InDuck = GameMovement.InDuck;

    /// <summary>An angle in degrees brought into (-180, 180].</summary>
    public static float WrapDegrees(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees <= -180f ? degrees + 360f : degrees;
    }

    /// <summary>How far apart two views are, degrees: pitch and yaw together.</summary>
    public static float ViewGap(Vector from, Vector to)
    {
        float pitch = to.X - from.X, yaw = WrapDegrees(to.Y - from.Y);
        return MathF.Sqrt(pitch * pitch + yaw * yaw);
    }

    /// <summary><paramref name="from"/> turned toward <paramref name="to"/> by at most <paramref name="degrees"/>, pitch and yaw together.</summary>
    public static Vector Turn(Vector from, Vector to, float degrees)
    {
        float pitch = to.X - from.X, yaw = WrapDegrees(to.Y - from.Y);
        float gap = MathF.Sqrt(pitch * pitch + yaw * yaw);
        if (gap <= degrees)
            return new Vector(to.X, WrapDegrees(to.Y), 0f);
        float share = degrees / gap;
        return new Vector(from.X + pitch * share, WrapDegrees(from.Y + yaw * share), 0f);
    }

    /// <summary>
    /// Where the view of a pilot in <paramref name="state"/> is turning to: the shot's until the
    /// trigger has been let go, then the second shot's until its trigger has been let go or its time
    /// has passed (a recipe with one), then the way it goes. A caller that turns the view by a motion
    /// of its own (a live bot's) hands that motion this, where <see cref="Step"/>'s own command has
    /// one command's turn toward it.
    /// </summary>
    public static Vector LooksTo(in JumpRecipe recipe, in JumpPilotState state)
    {
        int shotAt = Math.Max(0, recipe.ShotDelay);
        bool shot = state.Phase == JumpPhase.Approach || (state.Phase == JumpPhase.Launch && state.Count < shotAt + TriggerCommands);
        if (shot)
            return recipe.ShotView;
        if (recipe.Second is { } second && state.Phase == JumpPhase.Launch
            && (state.Second == 0 ? state.Count <= shotAt + second.Latest : state.Count < state.Second - 1 + TriggerCommands))
            return recipe.SecondView;
        return new Vector(0f, recipe.TravelYaw, 0f);
    }

    /// <summary>One command: see the class.</summary>
    public static JumpPilotStep Step(in JumpRecipe recipe, in JumpPilotState state, in JumpBody body, in JumpPilotLimits limits)
    {
        float travelYaw = recipe.TravelYaw, radians = travelYaw * (MathF.PI / 180f);
        var way = new Vector(MathF.Cos(radians), MathF.Sin(radians), 0f);
        float turn = limits.TurnDegreesASecond * limits.TickSeconds;
        Vector shotView = recipe.ShotView;
        JumpPilotState next = state with { LastVelocity = body.Velocity };

        if (state.Phase == JumpPhase.Landed)
            return Answer(Turn(body.View, new Vector(0f, travelYaw, 0f), turn), Vector.Zero, 0, next);

        if (state.Phase == JumpPhase.Approach)
        {
            Vector view = Turn(body.View, shotView, turn);
            float left = ViewGap(view, shotView);
            float along = (recipe.Takeoff.X - body.Origin.X) * way.X + (recipe.Takeoff.Y - body.Origin.Y) * way.Y;
            float speed = MathF.Max(0f, body.Velocity.X * way.X + body.Velocity.Y * way.Y);
            bool onLine = recipe.RunUp <= 0f || along <= speed * limits.TickSeconds * 0.5f + LineSlack;
            if (!onLine)
            {
                // Run once the turn left takes no longer than the run left at full speed: the view is there by the line.
                bool run = along >= left / limits.TurnDegreesASecond * limits.RunSpeed;
                return Answer(view, run ? way * limits.FullMove : Vector.Zero, 0, next);
            }

            if (left > AimDegrees)
                return Answer(view, Vector.Zero, 0, next);
            next = next with { Phase = JumpPhase.Launch, Count = 0, Airborne = false, Felt = false };
        }

        // The launch and the flight. n counts its commands; the jump's and the trigger's are ShotDelay apart, the earlier at 0.
        int n = next.Count, jumpAt = Math.Max(0, -recipe.ShotDelay), shotAt = Math.Max(0, recipe.ShotDelay);
        bool airborne = next.Airborne || (n > jumpAt && !body.OnGround);
        if (n > jumpAt && body.OnGround && (airborne || n > jumpAt + GroundedCommands))
            return Answer(Turn(body.View, new Vector(0f, travelYaw, 0f), turn), Vector.Zero, 0, next with { Phase = JumpPhase.Landed, Airborne = airborne });

        // The body seen at n is what the commands before n left: the jump's own leap shows at jumpAt + 1 and is not a blast.
        bool felt = next.Felt || (n > 0 && n != jumpAt + 1 && (body.Velocity - state.LastVelocity).Length > BlastFeltSpeed);
        int buttons = (n == jumpAt ? InJump : 0) | (recipe.Ducked && n >= jumpAt ? InDuck : 0)
                      | (n >= shotAt && n < shotAt + TriggerCommands ? InAttack : 0);
        int second = next.Second;
        if (recipe.Second is { } s && second == 0 && SecondNow(recipe, s, n - shotAt, airborne, body, way))
            second = n + 1;
        if (second > 0 && n < second - 1 + TriggerCommands)
            buttons |= InAttack;
        next = next with { Second = second };
        Vector look = Turn(body.View, LooksTo(recipe, next), turn);
        Vector wish = Vector.Zero;
        if (body.OnGround && n <= jumpAt)
        {
            if (recipe.RunUp > 0f)
                wish = way * limits.FullMove;
        }
        else if (airborne && (felt || n > shotAt + BlastWaitCommands))
        {
            wish = Steer(recipe.Landing, body, limits);
        }

        return Answer(look, wish, buttons, next with { Count = n + 1, Airborne = airborne, Felt = felt });
    }

    /// <summary>
    /// Whether the second rocket is fired on the command <paramref name="sinceShot"/> commands after the first trigger's:
    /// in the air, the launcher ready and its time not past, over its floor, the body come over its place, the view there.
    /// A body that has not come over the place by <see cref="JumpSecondShot.Latest"/> fires nothing (lane G7c: in the game a
    /// second rocket fired there wherever the body was never made the jump).
    /// </summary>
    private static bool SecondNow(in JumpRecipe recipe, in JumpSecondShot s, int sinceShot, bool airborne, in JumpBody body, Vector way)
    {
        if (!airborne || sinceShot < s.Earliest || sinceShot > s.Latest || body.Origin.Z <= s.Over.Z)
            return false;
        float before = (s.Over.X - body.Origin.X) * way.X + (s.Over.Y - body.Origin.Y) * way.Y;
        return before <= 0f && ViewGap(body.View, recipe.SecondView) <= SecondAimDegrees;
    }

    /// <summary>The command whose view is <paramref name="view"/> and whose moves, in that view's frame, are <paramref name="wish"/>.</summary>
    private static JumpPilotStep Answer(Vector view, Vector wish, int buttons, in JumpPilotState next)
    {
        float yaw = view.Y * (MathF.PI / 180f), fx = MathF.Cos(yaw), fy = MathF.Sin(yaw);
        var command = new JumpCommand(view, wish.X * fx + wish.Y * fy, wish.X * fy - wish.Y * fx, 0f, buttons);
        return new JumpPilotStep(command, next, wish);
    }

    /// <summary>
    /// The wish (direction and move) of one command in the air that takes the
    /// speed over the ground nearest the one wanted: toward the landing, no
    /// more than the body has along that way (or the air's cap, which it can
    /// always have), and no more than it brakes from in the way left.
    /// </summary>
    private static Vector Steer(Vector landing, in JumpBody body, in JumpPilotLimits limits)
    {
        float toX = landing.X - body.Origin.X, toY = landing.Y - body.Origin.Y, distance = MathF.Sqrt(toX * toX + toY * toY);
        var velocity = new Vector(body.Velocity.X, body.Velocity.Y, 0f);
        Vector wanted = Vector.Zero;
        if (distance > 1e-3f)
        {
            var toward = new Vector(toX / distance, toY / distance, 0f);
            float brake = BrakeShare * limits.AirAccelerate * limits.RunSpeed;
            float curve = MathF.Min(MathF.Sqrt(2f * brake * distance), distance / (HoldCommands * limits.TickSeconds));
            wanted = toward * MathF.Min(curve, MathF.Max(velocity.Dot(toward), limits.AirSpeedCap));
        }

        Vector change = wanted - velocity;
        float need = change.Length;
        if (need < 1e-3f)
            return Vector.Zero;

        // The change's own direction first and the circle round from it, then straight across the way it moves on
        // either side (where the air gives the cap whatever its speed): the nearest the air allows wins.
        float cos = MathF.Cos(2f * MathF.PI / WishDirections), sin = MathF.Sin(2f * MathF.PI / WishDirections);
        Vector direction = change * (1f / need), best = Vector.Zero;
        float bestMiss = need * need;
        for (int i = 0; i < WishDirections; i++)
        {
            Try(direction, change, velocity, limits, ref best, ref bestMiss);
            direction = new Vector(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos, 0f);
        }

        float speed = velocity.Length;
        if (speed > limits.AirSpeedCap)
        {
            var across = new Vector(-velocity.Y / speed, velocity.X / speed, 0f);
            Try(across, change, velocity, limits, ref best, ref bestMiss);
            Try(across * -1f, change, velocity, limits, ref best, ref bestMiss);
        }

        return best;
    }

    /// <summary>One wish direction: the move along it that adds nearest what is wanted there, kept when it leaves less of <paramref name="change"/> undone than the best so far.</summary>
    private static void Try(Vector direction, Vector change, Vector velocity, in JumpPilotLimits limits, ref Vector best, ref float bestMiss)
    {
        float along = change.Dot(direction);
        if (along <= 0f)
            return;

        // The move that would add just that along this direction, if the air gives it; else a held key's.
        float move = MathF.Min(along / (limits.AirAccelerate * limits.TickSeconds), limits.RunSpeed);
        float gained = Gain(velocity, direction, move, limits);
        if (gained < along - 1e-3f && move < limits.RunSpeed)
        {
            float held = Gain(velocity, direction, limits.RunSpeed, limits);
            if (MathF.Abs(held - along) < MathF.Abs(gained - along))
                (move, gained) = (limits.RunSpeed, held);
        }

        float miss = (change - direction * gained).LengthSquared;
        if (gained > 0f && miss < bestMiss)
        {
            bestMiss = miss;
            best = direction * (move >= limits.RunSpeed ? limits.FullMove : move);
        }
    }

    /// <summary>What one command of the air's acceleration adds along <paramref name="direction"/> for a wish of <paramref name="move"/>: the game's own function, asked.</summary>
    private static float Gain(Vector velocity, Vector direction, float move, in JumpPilotLimits limits)
    {
        Vector added = Vector.Zero;
        MoveMath.AirAccelerate(ref velocity, ref added, direction, move, limits.AirAccelerate, limits.TickSeconds, 1f, limits.AirSpeedCap, true);
        return added.Dot(direction);
    }
}
