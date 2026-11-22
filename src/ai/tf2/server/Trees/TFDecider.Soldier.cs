using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The Soldier's own (plan_bots_teams_tf2 C.3, C.3.2, P6 step 6): its stock weapons' names, and the rocket
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

    private readonly int _rocketJumpRow;
    private Tick _jumpUntil, _nextJumpAt;

    /// <summary>Rocket jumps begun (an ability one-shot sent), for the facts and the counters.</summary>
    public int RocketJumps { get; private set; }

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
