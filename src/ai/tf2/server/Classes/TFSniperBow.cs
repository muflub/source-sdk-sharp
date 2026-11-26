//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Knowledge;
using TF2.Server.Projectiles;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>The Huntsman's release rule, pure: when a drawn arrow goes (<see cref="TFDecider"/>'s bow play asks it).</summary>
public static class TFSniperBowRule
{
    /// <summary>Stock's release charge (a share of the full charge): bot/behavior/tf_bot_behavior.cpp:1382.</summary>
    public const float Release = 0.95f;

    /// <summary>Near, a half-drawn arrow goes at once (ours: 85 to the body, a headshot's crit 255).</summary>
    public const float NearUnits = 400f, NearRelease = 0.5f;

    /// <summary>The draw's charge, 0..1: stock's GetCurrentCharge over GetChargeMaxTime (tf_weapon_compound_bow.cpp:281-287).</summary>
    public static float Charge(float drawSeconds) => Math.Clamp(drawSeconds / TFBowBehaviour.ChargeTime, 0f, 1f);

    /// <summary>Whether a drawn arrow goes now: a full draw (stock's 0.95), or half of one at a target near; never undrawn.</summary>
    public static bool Ready(float charge, float distance) => charge >= Release || (distance < NearUnits && charge >= NearRelease);
}

public sealed partial class TFDecider
{
    /// <summary>
    /// The Huntsman's play (round2.md §5 "Sniper", the research's archetype C): mid-range bow fights beside the team, not a
    /// held sight line (the Sniper's class branch stays off with no scoped rifle). The draw starts once the eye is on the
    /// target; the arrow goes at a full charge (stock's 0.95, bot/behavior/tf_bot_behavior.cpp:1382-1386) or, near, at half of one, with
    /// the crosshair on the led head: the lead is the arrow's flight at the charge's speed and its drop at the charge's
    /// gravity (stock's CTFCompoundBow::GetProjectileSpeed 1,800-2,600 u/s and GetProjectileGravity 0.5-0.1,
    /// tf_weapon_compound_bow.cpp:306-317, at stock's 1 s charge, :281-287). A draw is never held into the tired shot (5 s,
    /// TF_ARROW_MAX_CHARGE_TIME): past 4.5 s, or with the target gone, the bow is lowered. In the fight it holds still while
    /// drawn (stock's CTFBotMainAction::Dodge does not dodge a drawing bot, bot/behavior/tf_bot_behavior.cpp:1609-1616); another
    /// branch's walk goes on. It prefers Heavies, Soldiers, Demomen and Pyros, and leaves Scouts.
    /// </summary>
    private sealed partial class SniperPlay
    {
        private const float BowRelease = TFSniperBowRule.Release, BowNearUnits = TFSniperBowRule.NearUnits, BowNearRelease = TFSniperBowRule.NearRelease;

        /// <summary>The draw starts with the view this near its point (ours), on a target held this long (a flick and a settle).</summary>
        public const float BowDrawStartDeg = 8f, BowDrawSettle = 0.25f;

        /// <summary>The arrow goes with the view within the head's angular radius of its point, never tighter than this (ours).</summary>
        public const float BowOnAimLeastDeg = 0.6f, BowHeadRadius = 6f;

        /// <summary>No draw held past this (the tired shot is 5 s, TFBowBehaviour.TiredAfter); a lost target's draw is held this long for its peek back.</summary>
        public const float BowTiredHold = 4.5f, BowLostHold = 1.5f;

        /// <summary>The bow's fight band (ours, the research's 300-1,500 u): closes past the far end, backs off inside the near.</summary>
        public const float BowBandNear = 300f, BowBandFar = 1200f;

        /// <summary>The bow's pool (a Huntsman carries 12 arrows, its mult_maxammo_primary 0.5): under this many it fetches ammo in a lull (ours, the shared errand S5).</summary>
        public const int BowAmmoLow = 4;

        /// <summary>The ammo trip's utility with the bow: over the points' and the team's 0.5, under a fight in sight (ours).</summary>
        public const float BowAmmoUtility = 0.6f;

        public override TFAmmoErrand Ammo => BowKind >= 0 ? new TFAmmoErrand(BowAmmoLow, BowAmmoUtility) : TFAmmoErrand.Off;

        /// <summary>Target bonuses with the bow (round2.md, the guide: good against these four, weak against Scouts).</summary>
        public const float BowHeavyBonus = 0.3f, BowSoldierBonus = 0.25f, BowDemoBonus = 0.25f, BowPyroBonus = 0.2f, BowScoutMalus = -0.4f;

        private int _lowerRow = -2;
        private Tick _bowLowerAt = new(-100000);
        private float _drawnBefore;
        private bool _letGo;
        private long _arrows, _bowReleases, _bowDraws, _bowLowered, _bowNear;
        private int _bowAmmoSeen = -1;

        private int LowerRow => _lowerRow == -2 ? _lowerRow = D.AbilityRow(TFSniperClass.BowLower) : _lowerRow;

        private int BowKind => D.Kit is { } kit && kit.TryWielded(TFLoadoutSlot.Primary, out Senses.TFKitItem i) && i.ItemClass == TFSniperKit.BowClass ? i.WeaponKind : -1;

        private bool HoldingBow => BowKind >= 0 && D._self.ActiveWeapon == BowKind;

        /// <summary>How long the bow has been drawn, as the bot's own HUD shows it (its charge bar, filling for a second); 0 when it is not.</summary>
        private float DrawSeconds => HoldingBow ? D.Kit?.Meter(TFSniperMeters.Draw, 0f) ?? 0f : 0f;

        private bool BowDrawn => DrawSeconds > 0f;

        /// <summary>The bow's aim and trigger: the led head, the draw held while it charges, let go on aim when ready.</summary>
        private void BowEngage()
        {
            float drawn = DrawSeconds;
            if (drawn > 0f && _drawnBefore <= 0f)
                _bowDraws++;
            _drawnBefore = drawn;
            _letGo = false;
            if (!D._targetSighted || D._target is not { } target || D.Busy)
            {
                // A target just lost: the draw is kept a moment for it to show again (the peek back), then lowered. The look stays
                // on him, where he was last seen, so his showing again is no new stimulus for the eye (the review's H2).
                if (drawn > 0f && drawn < BowTiredHold && _held is { EverSeen: true } lost && (D._now - lost.LastSeenTick) * D._tick <= BowLostHold)
                {
                    D._action = DrawHeld;
                    AiVector last = lost.LastSeenPos;
                    float up = TFKnowledge.AimHeight(AimPoint.Head, (TFClass)lost.ClassBelieved).Standing;
                    D._look = new LookIntent(LookKind.Actor, new AiVector(last.X, last.Y, last.Z + up), lost.Id, AimPoint.Head, 0f, 0);
                }

                return;
            }

            if (!ReferenceEquals(target, _held))
                (_held, _heldSince) = (target, D._now);
            int cls = target.ClassBelieved;
            bool seen = D._view.TryActor(target.Id, out ActorFacts f);
            if (seen)
                cls = f.Class;
            bool crouched = seen && (f.Activity & ActorActivity.Crouched) != 0;
            var head = new AiVector(D._targetPos.X, D._targetPos.Y, HeadZ(target.Id, cls, crouched));
            float charge = TFSniperBowRule.Charge(drawn);
            // The arrow leaves at the charge it will have when it goes: a full one unless it goes near at once.
            float at = D._targetDist < BowNearUnits ? Math.Max(charge, BowNearRelease) : Math.Max(charge, BowRelease);
            float speed = TFProjectileDefs.ArrowSpeed(at * TFBowBehaviour.ChargeTime), gravity = TFProjectileDefs.ArrowGravity(at * TFBowBehaviour.ChargeTime) * TFKnowledge.Gravity;
            AiVector eye = D.OwnEye;
            AiVector aim = AimLead.Point(eye, head, D._targetVel, speed, D.LeadNow);
            if (AimLead.Lob(eye, aim, speed, gravity) is { } lobbed)
                aim = lobbed;
            aim += D.AimError(aim, feet: false);
            D._look = new LookIntent(LookKind.Actor, aim, target.Id, AimPoint.Head, (aim - head).Length, 0);

            float off = OffAim(eye, aim);
            if (drawn <= 0f)
            {
                // The draw starts with the eye on the target, after a settle: a draw the eye's reaction cut would let a weak arrow go.
                if (off <= BowDrawStartDeg && Seconds(_heldSince) >= BowDrawSettle && Seconds(_bowLowerAt) > 1f)
                    D._action = DrawHeld;
                return;
            }

            float onAim = MathF.Max(BowOnAimLeastDeg, MathF.Atan2(BowHeadRadius, MathF.Max(1f, D._targetDist)) * (180f / MathF.PI));
            // A teammate on the line: the draw is held, not loosed into him (stock keeps the button down while the line is not
            // clear, bot/behavior/tf_bot_behavior.cpp:1382-1386), and lowered at the tired limit as any other.
            if (TFSniperBowRule.Ready(charge, D._targetDist) && off <= onAim && !FriendOnLine(aim))
            {
                // Let go: no fire this decide, and the arrow goes at the view the motion has.
                _letGo = true;
                _bowReleases++;
                D.Shots++;
                if (D._targetDist < BowNearUnits && charge < BowRelease)
                    _bowNear++;
                return;
            }

            if (drawn < BowTiredHold)
                D._action = DrawHeld;
        }

        /// <summary>
        /// The trigger held on a drawn charge: a Fire whose letting go is the shot (<see cref="FireStyle.Hold"/> on the first trigger).
        /// The friend gate, a voice line and the eye's wait leave it down (TFDecider.Feel.cs, TFDecider.Voice.cs, the applier).
        /// </summary>
        private static ActionIntent DrawHeld => new(ActionKind.Fire, FireStyle.Hold, 0, 0);

        /// <summary>Whether a teammate the bot sees stands on the line to <paramref name="aim"/>, nearer than the target: the friend gate's test, asked at the release.</summary>
        private bool FriendOnLine(AiVector aim)
        {
            StanceHeight body = TFKnowledge.AimHeight(AimPoint.Head, TFClass.Undefined);
            return D.Feel.FriendGate && TeamRule.IsGameTeam(D._self.Team)
                   && FriendLine.Blocked(D._view, D.OwnEye.Z - D._self.Origin.Z, aim, FireReach, 0f, body.Standing, body.Crouched,
                                         D._target is { } t && t.Unmasked(D._now, D._tick) ? t.Id : default);
        }

        /// <summary>The angle, degrees, between where the bot looks and <paramref name="point"/>.</summary>
        private float OffAim(AiVector eye, AiVector point)
        {
            AiVector d = point - eye;
            float yaw = MathF.Atan2(d.Y, d.X) * (180f / MathF.PI), pitch = -MathF.Atan2(d.Z, d.Length2D) * (180f / MathF.PI);
            float dy = HumanMotion.AngleDelta(D._self.Eye.Yaw, yaw), dp = pitch - D._self.Eye.Pitch;
            return MathF.Sqrt(dy * dy + dp * dp);
        }

        /// <summary>The lowering: a draw held into the tired window, or one whose target is gone for good.</summary>
        private bool BowOneShot()
        {
            float drawn = DrawSeconds;
            if (drawn <= 0f || _letGo || LowerRow < 0 || D._action.Kind == ActionKind.Fire)
                return false;
            // The second trigger with the first still down: stock's LowerBow, the arrow kept (TFBowBehaviour.SecondaryAttack).
            D._action = new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(LowerRow, 0), ++D._oneShot);
            _bowLowerAt = D._now;
            _bowLowered++;
            return true;
        }

        /// <summary>Footwork with the bow in hand: still while drawn (stock), else the band, nearer past its far end.</summary>
        private bool BowFootwork()
        {
            if (BowDrawn)
            {
                D._move = MoveIntent.Hold;
                D._verdict = "drawing";
                return true;
            }

            float dist = D._targetDist;
            float approach = dist > BowBandFar ? 0.8f : dist < BowBandNear ? -0.6f : 0f;
            if (D.Feel.Fight)
                D.FightStep(approach);
            else
                D.Strafe(approach);
            D._verdict = approach > 0f ? "closing (bow)" : approach < 0f ? "backing off (bow)" : "holding range (bow)";
            return true;
        }

        /// <summary>
        /// The bow's targets (round2.md: the guide's four, and Scouts left); a drawn arrow's target held over a rival, unless the
        /// bot was just hit (the rifle's exception: the re-review's B1, a nearer enemy hitting the bot took no turn from the hold).
        /// </summary>
        private float BowBonus(KnownActor target) => (BowDrawn && ReferenceEquals(target, _held) && Seconds(_hurtAt) > 0.5f ? HoldTargetBonus : 0f) + target.ClassBelieved switch
        {
            (int)TFClass.HeavyWeapons => BowHeavyBonus,
            (int)TFClass.Soldier => BowSoldierBonus,
            (int)TFClass.Demoman => BowDemoBonus,
            (int)TFClass.Pyro => BowPyroBonus,
            (int)TFClass.Scout => BowScoutMalus,
            _ => 0f,
        };

        /// <summary>
        /// Once a decide: an arrow went when the bow's own rounds (the clip's one and the quiver, as the HUD shows them) fell, whoever
        /// let the trigger go (the review's L5: the decider's own releases are <c>sniper.bow_releases</c>).
        /// </summary>
        private void ReadBow()
        {
            if (!HoldingBow)
            {
                _bowAmmoSeen = -1;
                return;
            }

            int rounds = Math.Max(0, D._self.Clip) + Math.Max(0, D._self.Reserve);
            if (_bowAmmoSeen >= 0 && rounds < _bowAmmoSeen)
                _arrows += _bowAmmoSeen - rounds;
            _bowAmmoSeen = rounds;
        }

        private void BowCount(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("sniper.arrows", _arrows));
            into.Add(new("sniper.bow_releases", _bowReleases));
            into.Add(new("sniper.bow_draws", _bowDraws));
            into.Add(new("sniper.bow_lowered", _bowLowered));
            into.Add(new("sniper.bow_near", _bowNear));
        }
    }
}
