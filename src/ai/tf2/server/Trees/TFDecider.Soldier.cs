using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The hands and the Soldier's own (plan_bots_teams_tf2 C.3, C.3.2, P6 step 6): fire at what is in
/// sight, a rocket at a grounded target's feet so a miss still lands its blast by them, the
/// launcher kept loaded in a lull, the shotgun when the launcher is dry up close, and the rocket
/// jump (<see cref="Knowledge.TFAbilities.RocketJump"/>) on a long run with nobody in sight, where
/// the nav says the way ahead is clear and the bot's health pays the jump's cost.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The Soldier's stock loadout, as its HUD's weapon list names it.</summary>
    public const string SoldierLauncher = "tf_weapon_rocketlauncher", SoldierShotgun = "tf_weapon_shotgun_soldier";

    /// <summary>
    /// A rocket jump pays on a run to a goal at least this far (C.3.2's <c>blastRouteUnits</c>), with
    /// health at least <see cref="RocketJumpHealthFloor"/> (C.3.2's Soldier floor: a jump costs some
    /// 40-60), moving at least <see cref="RocketJumpSpeed"/>, and the nav walkable straight ahead for
    /// <see cref="RocketJumpClear"/>, where it lands.
    /// </summary>
    public const float RocketJumpRoute = 1200f, RocketJumpHealthFloor = 90f, RocketJumpSpeed = 150f, RocketJumpClear = 512f;

    /// <summary>The least time between two rocket jumps, and how long one's sequence keeps the hands off the trigger.</summary>
    public const float RocketJumpEverySeconds = 6f, RocketJumpBusySeconds = 1.3f;

    /// <summary>A target moving up or down slower than this stands on something: its feet are where a blast goes.</summary>
    public const float GroundedSpeed = 60f;

    /// <summary>Feet aim reaches this far: past it the rocket's flight is long and a body shot is the better bet.</summary>
    public const float FeetReach = 1500f;

    /// <summary>The longest a gun is fired at; a launcher's rockets fly further but a person seldom tries.</summary>
    public const float FireReach = 2000f, ShotgunReach = 800f, SwapNear = 600f;

    /// <summary>A lull's reload or a weapon change, at most once in this long.</summary>
    public const float HandsEverySeconds = 1f;

    private readonly int _launcher, _shotgun, _rocketJumpRow;
    private Tick _jumpUntil, _nextJumpAt, _handsAt;

    /// <summary>Rocket jumps begun (an ability one-shot sent), for the facts and the counters.</summary>
    public int RocketJumps { get; private set; }

    /// <summary>Decides on which the hands fired a rocket at a target's feet.</summary>
    public int FeetShots { get; private set; }

    /// <summary>Decides on which the hands fired at all.</summary>
    public int Shots { get; private set; }

    private int KindNamed(string name)
    {
        foreach (WeaponFacts w in _map.Weapons.All)
        {
            if (w.Name == name)
                return w.Kind;
        }

        return -1;
    }

    private int AbilityRow(string name)
    {
        for (int i = 0; i < _map.Abilities.Count; i++)
        {
            if (_map.Abilities.TryGet(i, out AbilityRow row) && row.Name == name)
                return i;
        }

        return -1;
    }

    private bool HoldingLauncher => _launcher >= 0 && _self.ActiveWeapon == _launcher;

    private bool LauncherDry => _targetSighted && HoldingLauncher && _self.Clip == 0;

    private bool Busy => _now < _jumpUntil;

    /// <summary>Whether the weapon in hand fires a rocket: a projectile that flies flat (no fall), whose blast is aimed at the floor.</summary>
    private bool Splash(out WeaponFacts own)
        => _map.Weapons.TryGet(_self.ActiveWeapon, out own) && own.ProjectileSpeed > 0f && own.ProjectileGravity <= 0f;

    /// <summary>
    /// The hands on a target in sight: look at it with the bot's error, a rocket at the feet of one
    /// on the ground and ahead of it by its flight, and hold the trigger in reach. Not while a jump's
    /// sequence has the view, nor a rocket so near the blast would take the bot too.
    /// </summary>
    private void Engage()
    {
        if (!_targetSighted || _target is null || Busy)
            return;
        bool splash = Splash(out WeaponFacts own);
        bool feet = splash && MathF.Abs(_targetVel.Z) < GroundedSpeed && _targetDist < FeetReach;
        var aim = new AiVector(_targetPos.X, _targetPos.Y, AimHeight(feet));
        AiVector eye = OwnEye;
        float lead = 0f;
        if (own.ProjectileSpeed > 0f)
        {
            AiVector led = AimLead.Point(eye, aim, _targetVel, own.ProjectileSpeed, LeadNow);
            lead = (led - aim).Length;
            aim = led;
        }

        // The error by Accuracy: a point off the body, wandering from where first sight put it.
        aim += AimError(aim, feet);
        _look = new LookIntent(LookKind.Actor, aim, _target.Id, feet ? AimPoint.Feet : AimPoint.Body, lead, 0);

        float reach = _self.ActiveWeapon == _shotgun ? ShotgunReach : FireReach;
        if (_targetDist > reach || (splash && _targetDist < SelfBlastUnits) || (own.HasClip && _self.Clip <= 0))
            return;
        _action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
        Shots++;
        if (feet)
            FeetShots++;
    }

    /// <summary>
    /// What the hands do beside the fire: the shotgun when the launcher is dry with the target near,
    /// the launcher back in a lull, and a reload in a lull when the clip is not full.
    /// </summary>
    private void Hands()
    {
        if (_action.Kind != ActionKind.None || Busy || _now < _handsAt)
            return;
        if (_targetSighted && HoldingLauncher && _self.Clip == 0 && _shotgun >= 0 && _targetDist < SwapNear)
        {
            Select(_shotgun);
            return;
        }

        if (_targetSighted)
            return;
        if (_launcher >= 0 && _self.ActiveWeapon != _launcher)
        {
            Select(_launcher);
            return;
        }

        if (_map.Weapons.TryGet(_self.ActiveWeapon, out WeaponFacts own) && own.HasClip && _self.Clip < own.ClipSize && _self.Reserve > 0
            && (_self.Flags & SelfFlags.Reloading) == 0)
        {
            _action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++_oneShot);
            _handsAt = _now + (int)(HandsEverySeconds / _tick);
        }
    }

    private void Select(int kind)
    {
        _action = new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, SourceSharp.Ai.Server.EngineBotSelf.ByKind + kind, ++_oneShot);
        _handsAt = _now + (int)(HandsEverySeconds / _tick);
    }

    /// <summary>
    /// The Soldier's rocket jump (C.3.2): only a Soldier, with its launcher loaded in hand, on the
    /// ground, nobody in sight and not carrying the flag, health over the floor, on a run of at
    /// least <see cref="RocketJumpRoute"/> to its goal, moving, and the nav walkable straight along
    /// its way for <see cref="RocketJumpClear"/>. One ability one-shot, then the hands stay off for
    /// the sequence and the next waits <see cref="RocketJumpEverySeconds"/>.
    /// </summary>
    private void RocketJump()
    {
        if (!JumpPays())
            return;
        _action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, _rocketJumpRow, ++_oneShot);
        _jumpUntil = _now + (int)(RocketJumpBusySeconds / _tick);
        _nextJumpAt = _now + (int)(RocketJumpEverySeconds / _tick);
        RocketJumps++;
        _verdict = "rocket jump";
    }

    /// <summary>Whether a rocket jump pays now (<see cref="RocketJump"/>'s rule).</summary>
    private bool JumpPays()
    {
        if (_class != TFClass.Soldier || _rocketJumpRow < 0 || !HoldingLauncher || _self.Clip <= 0 || _now < _nextJumpAt
            || (_self.Flags & SelfFlags.Grounded) == 0 || _targetSighted || Carrying || _self.Health < RocketJumpHealthFloor
            || !_hasGoal || _move.Kind != MoveKind.Follow || _self.Origin.DistanceTo(_goal) < RocketJumpRoute || _graph is null)
            return false;
        var way = new AiVector(_self.Velocity.X, _self.Velocity.Y, 0f);
        if (way.Length < RocketJumpSpeed)
            return false;
        // Along its way, toward the goal, not off to a side of it.
        AiVector dir = way.Normalised();
        AiVector toGoal = new AiVector(_goal.X - _self.Origin.X, _goal.Y - _self.Origin.Y, 0f).Normalised();
        if (dir.Dot(toGoal) < 0.5f)
            return false;
        CellId at = _graph.FindCell(_self.Origin);
        if (!at.IsValid)
            return false;
        AiVector land = _self.Origin + dir * RocketJumpClear;
        _clear ??= new NavObstacleState(_graph.ObstacleCount);
        return _graph.WalkableLine(at.Index, _self.Origin, new AiVector(land.X, land.Y, _graph.Cell(at).FloorZ), ClearanceClass.Stand, _clear);
    }

    private NavObstacleState? _clear;
}
