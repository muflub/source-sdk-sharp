using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The hands and the Soldier's own (plan_bots_teams_tf2 C.3, C.3.2, P6 step 6): fire at what is in
/// sight, a rocket at a grounded target's feet so a miss still lands its blast by them, the
/// launcher kept loaded in a lull, the shotgun when the launcher is dry up close, and the rocket
/// jump (<see cref="Knowledge.TFAbilities.RocketJump"/>, a program that flies a planned recipe):
/// begun where the level's jump book has a plan for the stretch ahead that beats the walk
/// (TFDecider.Jump.cs), with nobody in sight, nothing carried, and the bot's health over its floor
/// after the blast's cost.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The Soldier's stock loadout, as its HUD's weapon list names it.</summary>
    public const string SoldierLauncher = "tf_weapon_rocketlauncher", SoldierShotgun = "tf_weapon_shotgun_soldier";

    /// <summary>
    /// A rocket jump is begun by a bot moving at least <see cref="RocketJumpSpeed"/> whose health
    /// after the jump's blast (the simulator's cost for the recipe) is at least
    /// <see cref="RocketJumpHealthFloor"/>: over the share at which it would leave for a health pack. Or under it, when
    /// the jump lands by a health pack or a cabinet the bot may use and that pays for it (lane G9,
    /// <see cref="Jump.JumpHealthRule"/>).
    /// </summary>
    public const float RocketJumpHealthFloor = 90f, RocketJumpSpeed = 150f;

    /// <summary>The least time between two rocket jumps, and how long from the one-shot a jump's approach keeps the hands off the trigger before the bot's own feet say whether it flew.</summary>
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
    /// on the ground and ahead of it by its flight, and hold the trigger in reach. Not while a rocket
    /// jump's program has the view, nor a rocket so near the blast would take the bot too.
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
    /// The Soldier's rocket jump (C.3.2, plan_bots_teams_tf2_part_g G.5 step 6): where
    /// <see cref="JumpPays"/> has a jump for the stretch ahead, one ability one-shot that carries
    /// the plan's number to the program that flies it; the hands stay off while the jump has the bot
    /// (<see cref="JumpUnderWay"/>) and the next waits <see cref="RocketJumpEverySeconds"/>.
    /// </summary>
    private void RocketJump()
    {
        NoteHealthJump();
        if (JumpUnderWay() || !JumpPays(out Jump.JumpPlan? plan, out JumpPaid paid))
            return;
        _action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(_rocketJumpRow, plan.Id), ++_oneShot);
        _jumpUntil = _now + (int)(RocketJumpBusySeconds / _tick);
        _nextJumpAt = _now + (int)(RocketJumpEverySeconds / _tick);
        RocketJumps++;
        JumpBegun(plan, paid);
        _verdict = "rocket jump";
    }

    /// <summary>
    /// Whether a rocket jump pays now, and which (<see cref="RocketJump"/>'s rule): the link the route takes, which
    /// the bot stands at the start of, under the rule but its spacing and a glance (<see cref="LinkMayBegin"/>,
    /// <see cref="RouteLinkAdvised"/>); else, when the tree allows one (<see cref="JumpAllowed"/>), the jump the
    /// level's book has for the stretch ahead that the bot's health covers (<see cref="JumpAdvised"/>). The book's
    /// jumps are the ones the simulator says beat the walk; nothing is flown or searched for here.
    /// </summary>
    private bool JumpPays([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Jump.JumpPlan? plan, out JumpPaid paid)
    {
        plan = null;
        paid = JumpPaid.No;
        if (!MayRocketJump)
            return false;
        Jump.JumpLinkRefusal why = JumpRefusalNow, refused = Jump.JumpLinkRefusal.Health;
        bool linkMay = LinkMayBegin(why);
        if (linkMay && RouteLinkAdvised(out plan, out refused, out paid))
            return true;
        if (why == Jump.JumpLinkRefusal.None && JumpAdvised(out plan, out paid))
            return true;
        // A wait at a link's start is counted with its reason (the book counts it only when the route has one here).
        Jumps!.LinkRefused(_bot.Slot, _now, _goal, linkMay ? refused : why);
        return false;
    }
}
