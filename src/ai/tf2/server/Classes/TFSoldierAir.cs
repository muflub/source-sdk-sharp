using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Soldier's hands in a rocket jump's flight (round 2's S1, <see cref="JumpHands"/>): what its play asks of the keys,
    /// the look and the weapon its program lends it in the air.
    /// <list type="bullet">
    /// <item><b>The Market Gardener</b> (416; any melee with <c>crit_while_airborne</c>). Stock crits its every hit while the
    /// attacker is <c>InAirDueToExplosion</c>: off the ground, out of water and in a blast jump's state
    /// (tf_gamerules.cpp:6073-6085, tf_player.h:599), which a rocket jump sets until the landing (tf_player.cpp:8701, :8712).
    /// So in the air after its own blast it looks at its landing past the apex, takes the melee weapon out for an enemy a swing
    /// from the landing reaches while the fall leaves the switch (stock's 0.5 s, tf_weaponbase.cpp:1255), and swings falling
    /// at him when the swing meets him with the bot still in the air when it lands, stock's smack 0.2 s after the press
    /// (tf_weapon_parse.cpp:96) (<c>Garden</c>).</item>
    /// <item><b>The B.A.S.E. Jumper</b> (1101; any item with <c>parachute_attribute</c>, worn). Stock opens it on a press of
    /// the jump key in the air (tf_gamemovement.cpp:1132-1154) and holds the fall to 100 u/s under it (:55, :2626-2628).
    /// Once past the apex, when the landing is <see cref="ChuteDrop"/> or more under the jump's takeoff or the way to it crosses a gap
    /// (<see cref="ChuteGap"/>), it presses once: the high ground is kept longer. The program closes it for the landing it
    /// needs (<see cref="JumpHands.ChuteKeepSeconds"/>).</item>
    /// </list>
    /// </summary>
    private sealed partial class SoldierPlay
    {
        /// <summary>Stock's smack: a melee swing's hit is traced this long after its press (tf_weapon_parse.cpp:96, <c>SmackDelay</c> 0.2).</summary>
        public const float SmackSeconds = 0.2f;

        /// <summary>Stock's weapon switch: a weapon is ready this long after it is taken out (tf_weaponbase.cpp:1255, <c>flWeaponSwitchTime</c> 0.5).</summary>
        public const float DeploySeconds = 0.5f;

        /// <summary>The world's gravity a body falls at (<see cref="TFViewVectors.Params"/>: <c>sv_gravity</c> 800).</summary>
        private static float Gravity => TFViewVectors.Params.Gravity;

        /// <summary>The parachute opens past the apex when the landing is this far under the takeoff, or the way to it crosses floor this far under the lower end.</summary>
        public const float ChuteDrop = 256f, ChuteGap = 128f;

        /// <summary>How finely the way to the landing is read for a gap.</summary>
        public const float ChuteGapStep = 32f;

        /// <summary>The attributes the air play reads off the kit.</summary>
        public const string CritWhileAirborne = "crit_while_airborne", ParachuteAttribute = "parachute_attribute";

        private AirFlight? _airFlight;
        private bool _chuteAsked;
        private int _gardenLooks, _gardenTakeOuts, _gardenSwings, _chuteAsks;

        public override AirAsk? InFlight(AirFlight flight)
        {
            if (!ReferenceEquals(flight, _airFlight))
                (_airFlight, _chuteAsked) = (flight, false);
            JumpPlan plan = flight.Plan;
            if (D.Kit is not { } kit)
                return null;
            int hold = 0, press = 0, weapon = -1;
            AiAngles? look = null;
            if (Gardener(kit, out int melee))
                Garden(plan, melee, ref look, ref weapon, ref hold);

            if (ChuteDue(kit, plan))
            {
                press |= GameMovement.InJump;
                _chuteAsked = true;
                _chuteAsks++;
            }

            return hold == 0 && press == 0 && weapon < 0 && look is null ? null : new AirAsk(D._bot) { Hold = hold, Press = press, Look = look, Weapon = weapon };
        }

        /// <summary>The melee weapon in the kit that crits in a blast jump (<c>crit_while_airborne</c>), if any.</summary>
        private static bool Gardener(TFHud kit, out int melee)
        {
            melee = kit.TryWielded(TFLoadoutSlot.Melee, out TFKitItem item) && item.Has(CritWhileAirborne) ? item.WeaponKind : -1;
            return melee >= 0;
        }

        /// <summary>
        /// The Market Garden in the air of <paramref name="plan"/>'s flight:
        /// <list type="bullet">
        /// <item>past the apex, with no enemy sighted, the view goes to the landing, so one standing there comes into the sight's
        /// cone (a level view along the way, the program's own, never holds what is under it);</item>
        /// <item>an enemy in sight a swing from the landing reaches (<see cref="SpyPlay.InSwingReach"/> from the eye at the touch):
        /// the view on him, and the melee taken out only while the fall leaves its switch (<see cref="DeploySeconds"/>) before the
        /// landing: out by then, it is the weapon for him on the ground too;</item>
        /// <item>with the melee out, falling, a swing that meets him with the bot still in the air when it lands (stock decides the
        /// crit at the damage: tf_gamerules.cpp:6073-6085): the trigger.</item>
        /// </list>
        /// </summary>
        private void Garden(JumpPlan plan, int melee, ref AiAngles? look, ref int weapon, ref int hold)
        {
            AiVector touch = Touch(plan);
            bool falling = D._self.Velocity.Z < 0f;
            if (!D._targetSighted || D._target is not { } target || !D._view.TryActor(target.Id, out ActorFacts enemy) || !InReachOfTheTouch(touch, enemy))
            {
                if (falling)
                {
                    look = SourceSharp.Ai.Server.BotCommandApplier.AnglesTo(D.OwnEye, touch);
                    _gardenLooks++;
                }

                return;
            }

            look = SourceSharp.Ai.Server.BotCommandApplier.AnglesTo(D.OwnEye, enemy.Origin + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Body, (TFClass)enemy.Class).At((enemy.Activity & ActorActivity.Crouched) != 0)));
            if (D._self.ActiveWeapon != melee)
            {
                if (AirLeft(touch.Z) < DeploySeconds)
                    return;
                weapon = SourceSharp.Ai.Server.EngineBotSelf.ByKind + melee;
                _gardenTakeOuts++;
            }
            else if (falling && FeetAfter(SmackSeconds) > touch.Z && SwingLands(enemy))
            {
                hold |= GameMovement.InAttack;
                _gardenSwings++;
            }
        }

        /// <summary>Whether a swing from the eye of a bot standing on the jump's landing reaches <paramref name="enemy"/>.</summary>
        private bool InReachOfTheTouch(AiVector touch, in ActorFacts enemy)
            => SpyPlay.InSwingReach(touch + (D.OwnEye - D._self.Origin), enemy.Origin, (enemy.Activity & ActorActivity.Crouched) != 0);

        /// <summary>The seconds before the bot's feet, falling freely, come down to <paramref name="floor"/>; 0 under it.</summary>
        private float AirLeft(float floor)
        {
            float z = D._self.Origin.Z - floor, vz = D._self.Velocity.Z;
            return z <= 0f ? 0f : (vz + MathF.Sqrt(vz * vz + 2f * Gravity * z)) / Gravity;
        }

        /// <summary>The bot's feet <paramref name="seconds"/> from now, falling freely.</summary>
        private float FeetAfter(float seconds) => D._self.Origin.Z + D._self.Velocity.Z * seconds - 0.5f * Gravity * seconds * seconds;

        private static AiVector Touch(JumpPlan plan) => new(plan.Touch.X, plan.Touch.Y, plan.Touch.Z);

        /// <summary>Whether a swing pressed now meets <paramref name="enemy"/>: both led by their speeds over stock's smack delay.</summary>
        private bool SwingLands(in ActorFacts enemy)
            => SpyPlay.InSwingReach(D.OwnEye + D._self.Velocity * SmackSeconds, enemy.Origin + enemy.Velocity * SmackSeconds, (enemy.Activity & ActorActivity.Crouched) != 0);

        /// <summary>Whether the parachute is opened now: worn, not asked yet this jump, past the apex, and a landing far below or across a gap.</summary>
        private bool ChuteDue(TFHud kit, JumpPlan plan)
        {
            if (_chuteAsked || D._self.Velocity.Z >= 0f || !kit.TryItem(TFLoadoutSlot.Secondary, out TFKitItem chute) || !chute.Has(ParachuteAttribute))
                return false;
            AiVector at = D._self.Origin, touch = Touch(plan);
            return plan.Recipe.Takeoff.Z - touch.Z >= ChuteDrop || CrossesGap(at, touch, MathF.Min(plan.Recipe.Takeoff.Z, touch.Z) - ChuteGap);
        }

        /// <summary>Whether the way over the ground from <paramref name="from"/> to <paramref name="to"/> passes over no floor, or floor under <paramref name="floor"/> (the jump's lower end less <see cref="ChuteGap"/>).</summary>
        private bool CrossesGap(AiVector from, AiVector to, float floor)
        {
            if (D._graph is not { } graph)
                return false;
            float flat = (to - from).Length2D;
            for (float along = ChuteGapStep; along < flat - ChuteGapStep; along += ChuteGapStep)
            {
                float t = along / flat;
                var p = new AiVector(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z);
                CellId cell = graph.FindCell(p);
                if (!cell.IsValid || graph.Cell(cell).FloorZ < floor)
                    return true;
            }

            return false;
        }

        private void CountAir(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("soldier.garden_looks", _gardenLooks));
            into.Add(new("soldier.garden_takeouts", _gardenTakeOuts));
            into.Add(new("soldier.garden_swings", _gardenSwings));
            into.Add(new("soldier.chute_asks", _chuteAsks));
        }
    }
}
