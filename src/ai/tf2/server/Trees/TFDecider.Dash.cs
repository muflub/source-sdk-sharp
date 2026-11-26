using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The hands for a triple jump (round 2's S2): the Atomizer's <c>air_dash_count</c> gives a third jump only while it is in
/// the hands, deployed 0.7 s (stock <c>CTFPlayer::CanAirDash</c>, tf_player_shared.cpp:12861-12873), so a bot whose route
/// just ahead has a triple jump (<see cref="Nav.TFRouteDashes"/>) takes it out in a lull and keeps it until the jump is
/// over. The routes plan the jump from the kit (<see cref="Nav.TFJumpKit"/>); these hands make it answer.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>What each bot's route said of the air jumps just ahead; null in a replay, and the hands never reach for the dash weapon.</summary>
    public Nav.TFRouteDashes? RouteDashes { get; init; }

    /// <summary>How long after the route last said a triple jump was near the dash weapon stays out: the jump's whole flight and a margin.</summary>
    public const float DashHoldSeconds = 1.5f;

    private Tick _dashHoldUntil;

    /// <summary>Decides on which the hands took the dash weapon out for a triple jump, for the facts and the counters.</summary>
    public int DashWeaponSelects { get; private set; }

    /// <summary>
    /// Flights begun with a triple jump just ahead that rose past the most a double jump can (<see cref="DoubleJumpTop"/>)
    /// over their take-off: triple jumps flown, as the bot's own place says, for the counters.
    /// </summary>
    public int TripleJumps { get; private set; }

    /// <summary>The highest any flight begun with a triple jump ahead rose over its take-off, for the counters.</summary>
    public float TripleJumpBestRise { get; private set; }

    /// <summary>
    /// The most a double jump's feet rise: the ground jump and one dash (the graph's 97.2) and the tuck (82 standing less 62
    /// crouched): 117.2. A flight over it had a third jump.
    /// </summary>
    public static float DoubleJumpTop
        => Nav.TFNavProfile.ModNumbers.MaxDoubleJumpRise + Nav.TFNavProfile.ModNumbers.StandHeight - Nav.TFNavProfile.ModNumbers.CrouchHeight;

    private bool _dashFlying, _dashFlightTriple;
    private float _dashTakeoffZ, _dashTopZ;

    /// <summary>
    /// Each decide: a flight's rise over the floor it left, when the route said a triple jump was just ahead as it left it.
    /// A flight is the decides off the ground; sampled a decide at a time, its top is within a fraction of a unit (the speed
    /// up is near 0 there).
    /// </summary>
    private void NoteDashFlight()
    {
        bool grounded = (_self.Flags & SelfFlags.Grounded) != 0;
        if (!_dashFlying)
        {
            if (grounded)
            {
                _dashTakeoffZ = _self.Origin.Z;
                return;
            }

            (_dashFlying, _dashTopZ) = (true, _self.Origin.Z);
            _dashFlightTriple = RouteDashes is not null && RouteDashes.Before(_bot.Slot, _now) >= 2;
            return;
        }

        _dashTopZ = MathF.Max(_dashTopZ, _self.Origin.Z);
        if (!grounded)
            return;
        _dashFlying = false;
        if (!_dashFlightTriple)
            return;
        float rise = _dashTopZ - _dashTakeoffZ;
        TripleJumpBestRise = MathF.Max(TripleJumpBestRise, rise);
        if (rise > DoubleJumpTop)
            TripleJumps++;
    }

    /// <summary>The triple jump's counters for the A/B harness (<c>/ai/classes</c>): none until the hands took a dash weapon out.</summary>
    private void CountDash(List<KeyValuePair<string, long>> into)
    {
        if (DashWeaponSelects == 0 && TripleJumps == 0)
            return;
        into.Add(new("dash weapon out", DashWeaponSelects));
        into.Add(new("triple jumps", TripleJumps));
        into.Add(new("triple jump best rise", (long)TripleJumpBestRise));
    }

    /// <summary>
    /// The weapon kind the hands hold for a triple jump now: the kit's weapon with an <c>air_dash_count</c> when the route
    /// just ahead says two presses in the air, or said so within <see cref="DashHoldSeconds"/>; -1 otherwise.
    /// </summary>
    private int DashWeaponNow()
    {
        if (RouteDashes is null || Kit is not { } kit)
            return -1;
        int kind = -1;
        foreach (Senses.TFKitItem item in kit.Kit)
        {
            if (item.Wielded && item.Attribute(TFJump.AirDashCountAttrib, 0f) >= 1f)
            {
                kind = item.WeaponKind;
                break;
            }
        }

        if (kind < 0)
            return -1;
        if (RouteDashes.Before(_bot.Slot, _now) >= 2)
            _dashHoldUntil = _now + (int)(DashHoldSeconds / _tick);
        return _now < _dashHoldUntil ? kind : -1;
    }

    /// <summary>
    /// The general hands' lull, before the primary is taken back: the dash weapon out for a triple jump just ahead
    /// (<see cref="DashWeaponNow"/>). Answers whether the hands are the jump's this decide.
    /// </summary>
    private bool DashHands()
    {
        int kind = DashWeaponNow();
        if (kind < 0)
            return false;
        if (_self.ActiveWeapon != kind)
        {
            Select(kind);
            DashWeaponSelects++;
        }

        return true;
    }
}
