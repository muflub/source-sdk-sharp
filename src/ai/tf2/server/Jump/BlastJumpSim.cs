//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using TF2.Server.Projectiles;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// Where a rocket ended: after which command, at what point, and what its
/// blast did to the shooter (no damage and no push when it did not reach him).
/// </summary>
public readonly record struct JumpImpact(int Command, Vector At, float Damage, Vector Impulse);

/// <summary>
/// What a simulated run did. <paramref name="Track"/> has a point per
/// command. <paramref name="Takeoff"/> is the first command that left the body
/// off the ground and <paramref name="Landing"/> the first after it that put
/// it back, each -1 when it never happened. <paramref name="From"/> is where
/// it last stood before taking off (the start, when it never stood) and
/// <paramref name="To"/> where it came down (where the run ended, when it
/// never did). <paramref name="Apex"/> is the highest the origin rose in
/// between, over <paramref name="From"/>. <paramref name="Rockets"/> counts
/// the rockets fired and <paramref name="Impacts"/> lists the ones that met
/// a solid, in the order they did.
/// </summary>
public sealed record JumpFlight(JumpPoint[] Track, int Takeoff, int Landing, Vector From, Vector To, float Apex, float AirSeconds,
                                int Rockets, JumpImpact[] Impacts)
{
    /// <summary>Whether it left the ground and came back to it.</summary>
    public bool Landed => Landing >= 0;

    /// <summary>How many rockets' blasts reached the shooter.</summary>
    public int Blasts => Impacts.Count(static i => i.Damage > 0f);

    /// <summary>What the shooter's own blasts handed the damage layer, before the damage rules.</summary>
    public float BlastDamage => Impacts.Sum(static i => i.Damage);
}

/// <summary>
/// A blast jump, simulated (plan_bots_teams_tf2_part_g G.5 step 3): from a
/// start state and a list of commands, where a Soldier firing at his own feet
/// goes. Each command is moved by the game's own loop
/// (<see cref="SoldierMover"/>) through the nav's free space
/// (<see cref="VoxelMoveWorld"/>); a command that holds the trigger while the
/// launcher is ready fires a rocket from the muzzle as the mod's launcher
/// does; every rocket in the air then flies one tick as the server's
/// projectiles do (<see cref="MoveMath.AdvanceFlight"/>), and the one that
/// meets a solid blasts its shooter with the mod's own arithmetic
/// (<see cref="TFRadiusDamage"/>), in the order the mod's blast runs its
/// own-pass (<c>TFBlastApplier</c>: reach, line, distance, damage, push).
///
/// <para>
/// No engine and no entity: nobody else is hurt, nothing is in the way but
/// the map, the launcher is loaded, and the body cannot die. The same start
/// and commands give the same flight, bit for bit. A simulator holds one
/// body, so it serves one thread at a time; any number of them share one
/// <see cref="VoxelColumns"/>.
/// </para>
/// </summary>
public sealed class BlastJumpSim
{
    /// <summary>The blast's source is a unit over where the rocket stopped (<c>TFBlastApplier</c>'s ground lift).</summary>
    public const float BlastLift = 1f;

    /// <summary>A rocket that has flown this many ticks without meeting a solid is dropped.</summary>
    public const int RocketLifeTicks = 1024;

    private readonly VoxelMoveWorld _world;
    private readonly SoldierMover _mover;
    private readonly JumpLauncher _launcher;
    private readonly Vector _bodyMins, _bodyMaxs, _duckMins, _duckMaxs;
    private readonly float _gravity;
    private readonly List<Rocket> _rockets = [];
    private readonly List<JumpImpact> _impacts = [];
    private int _count, _takeoff, _landing, _fired;
    private Vector _from, _landed;
    private float _top, _readyAt, _blastDamage;
    private bool _wasOnGround;

    internal BlastJumpSim(VoxelMoveWorld world, SoldierMover mover, JumpLauncher launcher, Vector bodyMins, Vector bodyMaxs, Vector duckMins, Vector duckMaxs, float gravity)
    {
        _world = world;
        _mover = mover;
        _launcher = launcher;
        _bodyMins = bodyMins;
        _bodyMaxs = bodyMaxs;
        _duckMins = duckMins;
        _duckMaxs = duckMaxs;
        _gravity = gravity;
    }

    /// <summary>The movement under the flight, for a caller that walks without firing.</summary>
    public SoldierMover Mover => _mover;

    /// <summary>The free space it flies through, for a caller that asks whether a body fits somewhere before flying from it.</summary>
    public VoxelMoveWorld World => _world;

    private struct Rocket
    {
        public Vector Origin, Velocity;
        public int Age;
    }

    /// <summary>Runs <paramref name="commands"/> from <paramref name="start"/>.</summary>
    public JumpFlight Fly(in JumpStart start, ReadOnlySpan<JumpCommand> commands)
    {
        Begin(start);
        var track = new JumpPoint[commands.Length];
        for (int i = 0; i < commands.Length; i++)
            track[i] = Step(commands[i]);
        return new JumpFlight(track, _takeoff, _landing, _from, To, Apex, AirSeconds, _fired, [.. _impacts]);
    }

    /// <summary>
    /// Starts a run at <paramref name="start"/> that <see cref="Step"/> then moves a command at a
    /// time: for a caller that decides each command from where the last left the body (a pilot).
    /// <see cref="Fly(in JumpStart, ReadOnlySpan{JumpCommand})"/> is this and a step a command.
    /// </summary>
    public void Begin(in JumpStart start)
    {
        _mover.Reset(start);
        _rockets.Clear();
        _impacts.Clear();
        _count = 0;
        _takeoff = -1;
        _landing = -1;
        _fired = 0;
        _from = start.Origin;
        _landed = start.Origin;
        _top = start.Origin.Z;
        _readyAt = 0f;
        _wasOnGround = start.OnGround;
        _blastDamage = 0f;
    }

    /// <summary>Runs one command of the run <see cref="Begin"/> started, and answers where it left the body.</summary>
    public JumpPoint Step(in JumpCommand command)
    {
        float tick = _mover.TickSeconds;
        int i = _count++;
        _mover.Step(command);

        // The launcher, after the move as the weapon's frame is: a held trigger fires when the refire time has passed.
        float now = (i + 1) * tick;
        if ((command.Buttons & GameMovement.InAttack) != 0 && now >= _readyAt)
        {
            _rockets.Add(Fire());
            _fired++;
            _readyAt = now + _launcher.RefireSeconds;
        }

        // The projectiles' frame: each rocket one tick, the new one too.
        for (int r = _rockets.Count - 1; r >= 0; r--)
        {
            Rocket rocket = _rockets[r];
            if (Advance(ref rocket, tick, out Vector struck))
            {
                _rockets.RemoveAt(r);
                float dealt = Blast(struck, out Vector impulse);
                _blastDamage += dealt;
                _impacts.Add(new JumpImpact(i, struck, dealt, impulse));
            }
            else if (++rocket.Age >= RocketLifeTicks)
            {
                _rockets.RemoveAt(r);
            }
            else
            {
                _rockets[r] = rocket;
            }
        }

        bool onGround = _mover.OnGround;
        if (_takeoff < 0)
        {
            if (onGround)
            {
                _from = _mover.Origin;
            }
            else if (_wasOnGround || i == 0)
            {
                // Off the ground from standing on it, or in the air from the start.
                _takeoff = i;
                _top = MathF.Max(_from.Z, _mover.Origin.Z);
            }
        }
        else if (_landing < 0)
        {
            _top = MathF.Max(_top, _mover.Origin.Z);
            if (onGround)
            {
                _landing = i;
                _landed = _mover.Origin;
            }
        }

        _wasOnGround = onGround;
        return _mover.Point;
    }

    /// <summary>Commands run since <see cref="Begin"/>.</summary>
    public int Commands => _count;

    /// <summary>The first command that left the body off the ground, and the first after it that put it back; each -1 until it happens.</summary>
    public int Takeoff => _takeoff;

    public int Landing => _landing;

    /// <summary>Where it last stood before taking off, and where it came down (where it is now, until it does).</summary>
    public Vector From => _from;

    public Vector To => _count == 0 || _landing >= 0 ? _landed : _mover.Origin;

    /// <summary>The highest the origin has risen since the takeoff, over <see cref="From"/>.</summary>
    public float Apex => _takeoff < 0 ? 0f : _top - _from.Z;

    /// <summary>The time off the ground so far, to the landing once there is one.</summary>
    public float AirSeconds => _takeoff < 0 ? 0f : ((_landing >= 0 ? _landing : _count - 1) - _takeoff) * _mover.TickSeconds;

    /// <summary>Rockets fired so far, and what the shooter's own blasts have handed the damage layer.</summary>
    public int Rockets => _fired;

    /// <summary>The rockets that met a solid so far, in the order they did (lane G7b: where a second rocket went off).</summary>
    public IReadOnlyList<JumpImpact> Impacts => _impacts;

    /// <summary>
    /// The commands after one that fires before the launcher fires again (lane G7b): the first command whose time, a tick on
    /// from the firing command's, is past the refire.
    /// </summary>
    public int RefireCommands => (int)MathF.Ceiling(_launcher.RefireSeconds / _mover.TickSeconds - 1e-4f);

    public float BlastDamage => _blastDamage;

    /// <summary>The view the body was last commanded.</summary>
    public Vector ViewAngles => _mover.ViewAngles;

    /// <summary>
    /// A rocket as the launcher bears it (<c>TFProjectileEffects</c>' rocket
    /// arm): at the eye, out along the aim by the muzzle's offsets, flying
    /// straight along the aim. The eye is where the firing command's move
    /// left it, and the muzzle's crouched height goes by the duck that same
    /// move left: the launcher reads the player's published flags
    /// (<c>TFWeaponOwners.DuckingOf</c>), and the server publishes a
    /// command's inside its move, before its weapon frame
    /// (<c>ServerPlayerCommands.Move</c>), so a duck taken in the firing
    /// command is the muzzle's already, as stock's is
    /// (<c>tf_weaponbase_gun.cpp:521-525</c>). The recorded flights hold that.
    /// </summary>
    private Rocket Fire()
    {
        Vector forward = Angles.Forward(_mover.ViewAngles);
        (Vector right, Vector up) = Angles.RightUp(forward);
        float muzzleUp = _mover.Ducking ? _launcher.MuzzleUpDucked : _launcher.Muzzle.Z;
        return new Rocket
        {
            Origin = _mover.EyePosition + forward * _launcher.Muzzle.X + right * _launcher.Muzzle.Y + up * muzzleUp,
            Velocity = forward * _launcher.Speed,
        };
    }

    /// <summary>One tick of a rocket's flight (<c>EntityMotion.Integrate</c>): true when it met a solid, at <paramref name="struck"/>.</summary>
    private bool Advance(ref Rocket rocket, float tick, out Vector struck)
    {
        var input = new FlightInput(tick, _gravity, 0f, rocket.Origin, rocket.Velocity, 0);
        FlightStep step = MoveMath.AdvanceFlight(input);
        MoveTrace trace = _world.Trace(rocket.Origin, step.Destination, Vector.Zero, Vector.Zero, 0u, 0);
        FlightResult result = MoveMath.ResolveFlight(input, step, trace.Fraction, trace.EndPos, trace.PlaneNormal);
        rocket.Origin = result.Origin;
        rocket.Velocity = result.Velocity;
        struck = result.Origin;
        return result.Struck;
    }

    /// <summary>
    /// The shooter's own pass of a blast at <paramref name="at"/>
    /// (<c>TFBlastApplier.Apply</c>'s, <c>CTFGameRules::RadiusDamage</c>'s
    /// second sweep): the damage it deals him, pushed by
    /// <paramref name="impulse"/> as <c>ApplyPushFromDamage</c> pushes; 0 and
    /// no push when it does not reach him.
    /// </summary>
    private float Blast(Vector at, out Vector impulse)
    {
        impulse = Vector.Zero;
        Vector source = at + new Vector(0f, 0f, BlastLift);
        Vector origin = _mover.Origin;
        // His box follows the duck, as the server's does (FinishDuck/FinishUnDuck set the bounds).
        bool ducked = _mover.Ducking;
        Vector mins = ducked ? _duckMins : _bodyMins, maxs = ducked ? _duckMaxs : _bodyMaxs;
        Vector center = origin + (mins + maxs) * 0.5f;
        if (!TFRadiusDamage.Reaches(source, _launcher.SelfRadius, origin + mins, origin + maxs))
            return 0f;

        // The line from the blast to his centre: the map in the way spares him.
        MoveTrace line = _world.Trace(source, center, Vector.Zero, Vector.Zero, 0u, 0);
        if (line.Fraction != 1f)
            return 0f;

        bool blast = (_launcher.Bits & DamageType.Blast) != 0, onGround = _mover.OnGround;
        float distance = TFRadiusDamage.VictimDistance(source, directHit: false, isPlayer: true, center, origin, line.EndPos);
        TFBlast own = TFBlast.Of(_launcher.Damage, _launcher.Radius, _launcher.Bits).SelfPass(_launcher.SelfRadius, _launcher.Damage);
        float forceDamage = TFRadiusDamage.VictimDamage(own, distance, self: true, _launcher.Weapon)
                            * TFRadiusDamage.SelfDamageScale(soldier: true, blast, onGround, inWater: false);
        if (forceDamage <= 0f)
            return 0f;

        Vector size = TFRadiusDamage.SelfPushSize(maxs - mins, _duckMaxs - _duckMins);
        impulse = TFRadiusDamage.Push(TFRadiusDamage.PushDirection(source, center), size, forceDamage, self: true,
                                      TFRadiusDamage.SelfPushScale(soldier: true, blast, onGround, pyroFlareJump: false), heavy: false);
        _mover.Push(impulse);
        if (blast)
            _mover.ForgiveAirDucks();
        return forceDamage;
    }
}
