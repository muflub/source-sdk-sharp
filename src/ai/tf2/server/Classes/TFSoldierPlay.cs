//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Soldier's play (plan_bots_teams_tf2 C.3, P7; research p7-research/soldier.md §4.3, the review's fixes): the
    /// rocket jump on the general play, and the hands by the bot's own kit, whatever launcher, secondary and melee it
    /// carries:
    /// <list type="bullet">
    /// <item><b>Point blank.</b> A target inside the launcher's own blast (<see cref="SelfBlastUnits"/>) is not held fire
    /// at: the secondary gun if it is loaded, else the melee weapon at its reach (research mistake 1).</item>
    /// <item><b>Dry.</b> A launcher empty with the target inside stock's <see cref="SoldierDrySwap"/>
    /// (<c>tf_bot.cpp:3377-3390</c>): the secondary gun if loaded, else the melee weapon when the target is within
    /// <see cref="SoldierMeleeSwap"/>, else the tree backs off to load. A banner or the Gunboats is no gun.</item>
    /// <item><b>Back.</b> The secondary gun gives way to the launcher once the target is past <see cref="FightNear"/> and the
    /// gun has been out <see cref="SoldierGunSeconds"/>, or at once when the gun is dry; the melee weapon past
    /// <see cref="SoldierMeleeLeave"/>. A weapon's clip is remembered from when it was last in hand (a holstered weapon
    /// does not reload).</item>
    /// <item><b>Banners.</b> A full rage meter (<see cref="TFSoldierMeters.Rage"/>) is popped, never carrying the flag
    /// (stock's gate, <c>tf_bot.cpp:4567</c>), with a teammate in sight within the buff's reach and an enemy in sight
    /// within stock's desired attack range, or full for <see cref="SoldierBannerHoldSeconds"/> with a teammate near. The press
    /// goes through the friend gate; a banner that does not pop in <see cref="SoldierBannerPressSeconds"/> gives way to the
    /// launcher.</item>
    /// <item><b>The Direct Hit</b> (its 44 u blast misses at the feet): a grounded target is shot at the body, but one
    /// level with the bot within <see cref="SoldierDirectFeet"/>.</item>
    /// <item><b>Sentries.</b> An enemy sentry remembered and recent, with the launcher and its health over half: shelled
    /// from between 1.1 and 1.5 times its range (stock <c>tf_bot_destroy_enemy_sentry.cpp:244,304,325</c>).</item>
    /// <item><b>Targets.</b> A Sniper or an Engineer is worth more, a Scout far off less (research [W2]'s order).</item>
    /// <item><b>The archetypes</b> (research §4.3 C, D, E): a roamer jumps to a lower floor, leaves for health later, goes
    /// ahead of its team and hunts the Medic and the Sniper; a banner Soldier stays with its team; a Direct Hit
    /// Soldier holds a longer band, prizes a target in the air and weighs a sentry to shell more.</item>
    /// </list>
    /// </summary>
    private sealed class SoldierPlay(TFDecider d) : ClassPlay(d, TFClass.Soldier)
    {
        private readonly Dictionary<int, int> _clips = [];
        private Tick _gunSince, _fullSince, _foughtAt, _bannerOutSince, _bannerRestUntil;
        private bool _wasFull, _bannerOut;
        private float _lastRage = -1f;
        private int _reflectHolds, _pointBlankSwaps, _drySwaps, _meleeSwaps, _bannerPops, _sentryShots, _shellDecides, _shellGiveUps;

        /// <summary>The Soldier's rocket jump (TFDecider.Soldier.cs, TFDecider.Jump.cs): the Soldier's alone.</summary>
        public override void OneShots() => D.RocketJump();

        public override void Died()
        {
            _clips.Clear();
            _wasFull = _bannerOut = false;
            _lastRage = -1f;
        }

        public override void Count(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("soldier.point_blank_swaps", _pointBlankSwaps));
            into.Add(new("soldier.reflect_holds", _reflectHolds));
            into.Add(new("soldier.dry_swaps", _drySwaps));
            into.Add(new("soldier.melee_swaps", _meleeSwaps));
            into.Add(new("soldier.banner_pops", _bannerPops));
            into.Add(new("soldier.sentry_shots", _sentryShots));
            into.Add(new("soldier.shell_decides", _shellDecides));
            into.Add(new("soldier.shell_give_ups", _shellGiveUps));
            into.Add(new("soldier.rocket_jumps", D.RocketJumps));
        }

        // ------------------------------------------------------------------ the kit

        private int LauncherKind => D.PrimaryKind;

        /// <summary>The secondary when it is a gun (a shotgun); -1 for a banner, the Gunboats (worn), none.</summary>
        private int GunKind(TFHud kit)
            => kit.TryWielded(TFLoadoutSlot.Secondary, out TFKitItem item) && !IsBanner(item) ? item.WeaponKind : -1;

        private static int MeleeKind(TFHud kit) => kit.TryWielded(TFLoadoutSlot.Melee, out TFKitItem item) ? item.WeaponKind : -1;

        private static bool IsBanner(TFKitItem item) => item.ItemClass == BannerClass;

        private static bool Banner(TFHud kit, out TFKitItem banner) => kit.TryWielded(TFLoadoutSlot.Secondary, out banner) && IsBanner(banner);

        private static bool LauncherHas(TFHud kit, string attribute) => kit.TryWielded(TFLoadoutSlot.Primary, out TFKitItem item) && item.Has(attribute);

        private static bool DirectHit(TFHud kit)
            => kit.TryWielded(TFLoadoutSlot.Primary, out TFKitItem item) && item.ItemClass == DirectHitClass;

        private int ClipOf(int kind) => kind == D._self.ActiveWeapon ? D._self.Clip : _clips.TryGetValue(kind, out int c) ? c : 1;

        // ------------------------------------------------------------------ the hands

        public override void Hands()
        {
            if (D.Kit is not { } kit)
            {
                D.GeneralHands();
                return;
            }

            int active = D._self.ActiveWeapon;
            if (D._targetSighted)
                _foughtAt = D._now;
            else if (_clips.Count > 0 && (D._now - _foughtAt) * D._tick > SoldierClipMemorySeconds)
                _clips.Clear(); // a long lull: a cabinet or a respawn may have filled what was empty
            _clips[active] = D._self.Clip;
            if (D._action.Kind != ActionKind.None || D.Busy || D._now < D._handsAt)
                return;
            int launcher = LauncherKind, gun = GunKind(kit), melee = MeleeKind(kit);
            if (Banner(kit, out TFKitItem held) && held.WeaponKind == active && !BannerFull(kit))
            {
                // A banner out with nothing to pop (spent, or the pop came to nothing): back to the launcher.
                if (launcher >= 0)
                    D.Select(launcher);
                return;
            }

            if (D._targetSighted)
            {
                FightHands(kit, active, launcher, gun, melee);
                return;
            }

            if (launcher >= 0 && active != launcher && !(Banner(kit, out TFKitItem b) && b.WeaponKind == active))
            {
                D.Select(launcher);
                return;
            }

            // A lull's reload.
            if (D.HeldFacts(out WeaponFacts own) && own.HasClip && D._self.Clip < own.ClipSize && D._self.Reserve > 0
                && (D._self.Flags & SelfFlags.Reloading) == 0)
            {
                D._action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++D._oneShot);
                D._handsAt = D._now + (int)(SwitchSeconds / D._tick);
            }
        }

        private void FightHands(TFHud kit, int active, int launcher, int gun, int melee)
        {
            float dist = D._targetDist;
            if (active == launcher)
            {
                bool dry = D._self.Clip <= 0;
                bool near = dist < SelfBlastUnits || PyroCanReflect();
                if (!near && !(dry && dist < SoldierDrySwap))
                    return;
                if (gun >= 0 && ClipOf(gun) > 0)
                {
                    SelectGun(gun);
                    if (near)
                        _pointBlankSwaps++;
                    else
                        _drySwaps++;
                    return;
                }

                if (melee >= 0 && dist < SoldierMeleeSwap)
                {
                    D.Select(melee);
                    _meleeSwaps++;
                }

                return;
            }

            if (active == gun && gun >= 0)
            {
                if (D._self.Clip <= 0)
                {
                    if (melee >= 0 && dist < SoldierMeleeSwap)
                    {
                        D.Select(melee);
                        _meleeSwaps++;
                    }
                    else if (launcher >= 0)
                        D.Select(launcher);
                    return;
                }

                if (launcher >= 0 && dist > FightNear && ClipOf(launcher) > 0 && (D._now - _gunSince) * D._tick >= SoldierGunSeconds)
                    D.Select(launcher);
                return;
            }

            if (active == melee && melee >= 0)
            {
                if (dist <= SoldierMeleeLeave)
                    return;
                if (launcher >= 0 && ClipOf(launcher) > 0)
                    D.Select(launcher);
                else if (gun >= 0 && ClipOf(gun) > 0)
                    SelectGun(gun);
                else if (launcher >= 0)
                    D.Select(launcher);
                return;
            }

            // Something else in hand (a banner with nothing to pop): the launcher.
            if (launcher >= 0 && !(Banner(kit, out TFKitItem b) && b.WeaponKind == active))
                D.Select(launcher);
        }

        private void SelectGun(int gun)
        {
            D.Select(gun);
            _gunSince = D._now;
        }

        // ------------------------------------------------------------------ the Pyro

        /// <summary>
        /// Whether the target is a Pyro, as the bot's client draws it, facing the bot within <see cref="PyroFacingDegrees"/> and
        /// near enough to send a rocket back (research §4.3: its deflection reach, 128 u, <c>TFFlameEffects.cs:631</c>, and a
        /// rocket's flight at 1,100 u/s over one reaction, 256 u).
        /// </summary>
        private bool PyroCanReflect()
        {
            if (!D._targetSighted || D._target is null || D._targetDist > PyroReflectReach || !D._view.TryActor(D._target.Id, out ActorFacts f)
                || f.Class != (int)TFClass.Pyro)
                return false;
            float yaw = f.Facing.Yaw * (MathF.PI / 180f);
            var facing = new AiVector(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
            AiVector toBot = new AiVector(D._self.Origin.X - f.Origin.X, D._self.Origin.Y - f.Origin.Y, 0f).Normalised();
            return facing.Dot(toBot) >= MathF.Cos(PyroFacingDegrees * (MathF.PI / 180f));
        }

        /// <summary>With the launcher out and a Pyro able to reflect it, the bot backs off past its reach; else the general footwork.</summary>
        public override bool FightInSight()
        {
            if (D._self.ActiveWeapon != LauncherKind)
                return D.FightAtRange();
            if (!PyroCanReflect())
                return Is(TFSoldierClass.DirectHitter) ? DirectHitBand() : D.FightAtRange();
            if (D.Feel.Fight)
                D.FightStep(-0.6f);
            else
                D.Strafe(-0.6f);
            D._verdict = "backing off a Pyro";
            return true;
        }

        /// <summary>A Direct Hit holds a longer band (research §4.3 E: 500 to 1,500, stock's desired range 1,250): its rockets fly 1,980 u/s and its blast is small.</summary>
        private bool DirectHitBand()
        {
            float approach = D._targetDist > DirectFar ? 0.8f : D._targetDist < DirectNear ? -0.6f : 0f;
            if (D.Feel.Fight)
                D.FightStep(approach);
            else
                D.Strafe(approach);
            D._verdict = approach > 0f ? "closing" : approach < 0f ? "backing off" : "holding range";
            return true;
        }

        // ------------------------------------------------------------------ banners

        private static bool BannerFull(TFHud kit) => kit.Meter(TFSoldierMeters.Rage, 0f) >= RageFull;

        /// <summary>Whether the bot's banner is due to be popped now, and its weapon kind (research §4.3 D; stock's full-meter and flag gate).</summary>
        private bool BannerDue(TFHud kit, out int banner)
        {
            banner = -1;
            if (!Banner(kit, out TFKitItem item) || item.WeaponKind < 0)
            {
                _wasFull = false;
                return false;
            }

            banner = item.WeaponKind;
            if (D._now < _bannerRestUntil)
                return false;
            bool full = BannerFull(kit);
            if (full && !_wasFull)
                _fullSince = D._now;
            _wasFull = full;
            if (!full || D.Carrying)
                return false;
            bool mate = MateWithin(BannerReach);
            if (!mate)
                return false;
            bool enemy = D._targetSighted && D._targetDist <= SoldierAttackRange;
            bool waited = (D._now - _fullSince) * D._tick >= SoldierBannerHoldSeconds;
            return enemy || waited;
        }

        /// <summary>Whether a teammate the bot sees stands within <paramref name="reach"/> of it.</summary>
        private bool MateWithin(float reach)
        {
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (f.Id != D._bot && TeamRule.Friends(D._self.Team, f.Team) && D._self.Origin.DistanceTo(f.Origin) <= reach)
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the trigger

        public override void Engage()
        {
            if (D.Kit is not { } kit)
            {
                D.GeneralEngage();
                return;
            }

            int active = D._self.ActiveWeapon;
            CountPops(kit);
            if (Banner(kit, out TFKitItem banner) && banner.WeaponKind == active)
            {
                if (!_bannerOut)
                    (_bannerOut, _bannerOutSince) = (true, D._now);
                if (BannerFull(kit) && (D._now - _bannerOutSince) * D._tick > SoldierBannerPressSeconds)
                {
                    // Out and pressed this long with the meter still full: the pop is not happening; the launcher, and no
                    // banner again for a while (a fight is not stood through with the banner in hand).
                    _bannerRestUntil = D._now + (int)(SoldierBannerRestSeconds / D._tick);
                    if (LauncherKind >= 0)
                        D.Select(LauncherKind);
                    return;
                }

                // The banner is out to be popped: the trigger deploys it, and the game hands the launcher back. The press is
                // no shot at anyone, so the friend gate lets it go with a teammate in the view's line (the banner is due
                // exactly when one is near).
                if (BannerFull(kit) && !D.Busy)
                {
                    D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
                    D._friendFire = true;
                }

                return;
            }

            _bannerOut = false;
            // A banner due is taken out before any shot: the fire would keep the hands from it for the whole fight.
            if (!D.Busy && D._now >= D._handsAt && BannerDue(kit, out int due) && active != due)
            {
                D.Select(due);
                return;
            }

            D.GeneralEngage();
            if (!D._targetSighted || D._target is null || D.Busy)
            {
                Shell();
                return;
            }

            if (active != LauncherKind)
                return;
            if (PyroCanReflect())
            {
                // A rocket into a Pyro's airblast comes back (research mistake 2): hold it.
                if (D._action.Kind == ActionKind.Fire)
                    D._action = ActionIntent.None;
                _reflectHolds++;
                return;
            }

            if (DirectHit(kit))
                DirectHitAim();
        }

        /// <summary>A pop is the game's deploy, seen as the meter falling from full while the bot lives (it drains over the buff's 10 s).</summary>
        private void CountPops(TFHud kit)
        {
            float rage = kit.Meter(TFSoldierMeters.Rage, -1f);
            if (rage < 0f)
                return;
            if (_lastRage >= RageFull && rage < RageFull)
                _bannerPops++;
            _lastRage = rage;
        }

        /// <summary>The Direct Hit's aim: the body of a grounded target, unless it stands level with the bot and near (its blast is 44 u: a feet shot misses).</summary>
        private void DirectHitAim()
        {
            if (D._look.Kind != LookKind.Actor || D._look.Aim != AimPoint.Feet)
                return;
            if (D._targetDist < SoldierDirectFeet && MathF.Abs(D._targetPos.Z - D._self.Origin.Z) < SoldierDirectLevel)
                return;
            float rise = TFKnowledge.AimHeight(AimPoint.Body, TFClass.Undefined).Standing - TFKnowledge.FeetAim;
            D._look = D._look with { Point = D._look.Point + new AiVector(0f, 0f, rise), Aim = AimPoint.Body };
        }

        // ------------------------------------------------------------------ sentries

        private int _shellId = int.MinValue, _skipId = int.MinValue;
        private Tick _shellSince, _skipUntil;

        /// <summary>
        /// The sentry to shell (the review's C2): the nearest the bot saw itself (a teammate's call places none exactly), within
        /// <see cref="SoldierShellMemorySeconds"/> and <see cref="SoldierShellReach"/>, not one it gave up on; and only one that
        /// covers the bot's objective (within its range of the point it goes for, or the flag): an objective the sentry does
        /// not guard is gone for instead.
        /// </summary>
        private bool ShellTarget(out SeenSentry sentry)
        {
            sentry = default;
            float best = float.MaxValue;
            bool objective = ObjectiveAt(out AiVector goal);
            foreach (SeenSentry s in D.Sentries)
            {
                if (s.Id < 0 || (D._now - s.Last) * D._tick > SoldierShellMemorySeconds || (s.Id == _skipId && D._now < _skipUntil))
                    continue;
                float dist = D._self.Origin.DistanceTo(s.At);
                if (dist > SoldierShellReach || (objective && goal.DistanceTo(s.At) > TFKnowledge.SentryRange + SentryMargin))
                    continue;
                if (dist < best)
                    (best, sentry) = (dist, s);
            }

            return best < float.MaxValue;
        }

        /// <summary>Where the bot's objective is: the point it goes for, else the enemy's flag; false for none known.</summary>
        private bool ObjectiveAt(out AiVector at)
        {
            at = D._pointsGoal is { } point ? point.Position : D._enemyFlag is { } flag ? flag.Position : default;
            return D._pointsGoal is not null || D._enemyFlag is not null;
        }

        /// <summary>Gives the sentry being shelled up for <see cref="SoldierShellMemorySeconds"/>.</summary>
        private void GiveUp(SeenSentry sentry, string why)
        {
            (_skipId, _skipUntil, _shellId) = (sentry.Id, D._now + (int)(SoldierShellMemorySeconds / D._tick), int.MinValue);
            _shellGiveUps++;
            D._verdict = why;
        }

        /// <summary>Whether the bot is fit to shell a sentry: a launcher in its kit (read live: with none, a replay, the general play's edge), health over half, nothing carried.</summary>
        private bool MayShell => D.Kit is not null && LauncherKind >= 0 && D.HealthShare > SoldierShellHealth && !D.Carrying;

        public override float PlaceUtility() => MayShell && ShellTarget(out _) ? SoldierShellUtility : 0f;

        public override bool GoPlace()
        {
            if (!MayShell || !ShellTarget(out SeenSentry sentry))
                return false;
            if (sentry.Id != _shellId)
                (_shellId, _shellSince) = (sentry.Id, D._now);
            if ((D._now - _shellSince) * D._tick > SoldierShellGiveUpSeconds)
            {
                // Stock's research note: rockets from 1,200 u do not kill a tended nest; past this the objective is worth more.
                GiveUp(sentry, "gave up on a sentry");
                return false;
            }

            float range = TFKnowledge.SentryRange, dist = D._self.Origin.DistanceTo(sentry.At);
            _shellDecides++;
            bool inBand = dist >= range * SoldierShellNear && dist <= range * SoldierShellFar;
            if (inBand && SentryInView(sentry, out _))
            {
                // At a shelling spot with the sentry in view: hold here, the trigger the hands'.
                D._move = MoveIntent.Hold;
                D._verdict = "shelling a sentry";
                return true;
            }

            // Along the line from the sentry to the bot, at the middle of the band.
            AiVector away = new AiVector(D._self.Origin.X - sentry.At.X, D._self.Origin.Y - sentry.At.Y, 0f).Normalised();
            if (away.Length < 0.5f)
                away = new AiVector(1f, 0f, 0f);
            AiVector spot = sentry.At + away * (range * (SoldierShellNear + SoldierShellFar) * 0.5f);
            if (inBand && D._self.Origin.DistanceTo(spot) <= ArriveUnits)
            {
                // At the spot with no sight of the sentry: no line of fire from here (stock's IsLineOfFireClear for its spot,
                // tf_bot_destroy_enemy_sentry.cpp:242, which the bot can only learn by looking).
                GiveUp(sentry, "no line of fire to a sentry");
                return false;
            }

            D.GoTo(spot, MoveSpeed.Run);
            D._verdict = "to shell a sentry";
            return true;
        }

        /// <summary>Whether the view shows the remembered sentry (an enemy sentry within a short way of where it was remembered).</summary>
        private bool SentryInView(SeenSentry sentry, out AiVector at)
        {
            at = default;
            foreach (ref readonly ThingFacts t in D._view.Things)
            {
                if (t.Kind == ThingKind.Building && (TFThingType)t.Type == TFThingType.Sentry && t.Team != D._self.Team
                    && TeamRule.IsGameTeam(t.Team) && t.Origin.DistanceTo(sentry.At) < 64f)
                {
                    at = t.Origin;
                    return true;
                }
            }

            return false;
        }

        /// <summary>With no player in sight: a sentry in view within the band is fired at, at its body, the launcher loaded.</summary>
        private void Shell()
        {
            if (D._targetSighted || D.Busy || !MayShell || !D.HoldingLauncher || D._self.Clip <= 0 || !ShellTarget(out SeenSentry sentry)
                || !SentryInView(sentry, out AiVector at))
                return;
            float dist = D._self.Origin.DistanceTo(at);
            if (dist < TFKnowledge.SentryRange * SoldierShellNear || dist > FireReach)
                return;
            var body = new AiVector(at.X, at.Y, at.Z + SentryBody);
            D._look = new LookIntent(LookKind.Point, body, ActorId.None, AimPoint.Body, 0f, 0);
            D._action = new ActionIntent(ActionKind.Fire, FireStyle.Spray, 0, 0);
            _sentryShots++;
        }

        // ------------------------------------------------------------------ targets

        public override float TargetBonus(KnownActor target, bool sighted, float distance)
        {
            float bonus = (TFClass)target.ClassBelieved switch
            {
                TFClass.Sniper => SniperBonus + (Is(TFSoldierClass.Roamer) ? RoamerSniperBonus : 0f),
                TFClass.Medic => Is(TFSoldierClass.Roamer) ? RoamerMedicBonus : 0f,
                TFClass.Engineer => EngineerBonus,
                TFClass.Scout when distance > SoldierScoutChase => -ScoutFarDiscount,
                _ => 0f,
            };
            // The Direct Hit's airshot: a target in the air (research §4.3 E).
            if (Is(TFSoldierClass.DirectHitter) && sighted && MathF.Abs(target.LastSeenVel.Z) > AirborneSpeed)
                bonus += AirshotBonus;
            return bonus;
        }

        // ------------------------------------------------------------------ the archetypes (research §4.3 C, D, E)

        /// <summary>Whether the bot plays <paramref name="archetype"/>: only with its kit read live (with none, a replay or a fact with no HUD, the general play as before P7).</summary>
        private bool Is(string archetype) => D.Kit is not null && string.Equals(Archetype, archetype, StringComparison.OrdinalIgnoreCase);

        /// <summary>The health a jump leaves the bot with at least: a roamer's is lower (research §4.3 C), every other's the floor.</summary>
        public float JumpFloor => Is(TFSoldierClass.Roamer) ? RoamerJumpFloor : RocketJumpHealthFloor;

        /// <summary>A roamer leaves for health later (research §4.3 C: 35 %, 30 % with the Black Box's heal on a hit).</summary>
        public override float HurtShare
            => !Is(TFSoldierClass.Roamer) ? TFDecider.HurtShare
               : D.Kit is { } kit && LauncherHas(kit, BlackBoxHeal) ? RoamerBlackBoxShare : RoamerHurtShare;

        /// <summary>The archetype's factor on a root branch (<see cref="TFSoldierClass.BranchFactor"/>), with its kit read live.</summary>
        public override float Weigh(string branch) => D.Kit is null ? 1f : TFSoldierClass.BranchFactor(Archetype, branch);
    }

    /// <summary>The kit's item classes and attributes the Soldier's hands read.</summary>
    internal const string BannerClass = "tf_weapon_buff_item", DirectHitClass = "tf_weapon_rocketlauncher_directhit";

    /// <summary>A full rage meter (<c>TFRageMeter.FullMeter</c>) and a banner's reach (<c>TFRageMeter.BuffRadius</c>).</summary>
    public const float RageFull = 100f, BannerReach = 450f;

    /// <summary>Stock's dry swap range (<c>closeSoldierRange</c>, tf_bot.cpp:3384) and the melee's: inside the first a dry launcher is swapped for the gun, inside the second for the melee weapon.</summary>
    public const float SoldierDrySwap = 500f, SoldierMeleeSwap = 150f, SoldierMeleeLeave = 220f;

    /// <summary>How long the secondary gun stays out before the launcher comes back with the target past <see cref="FightNear"/>.</summary>
    public const float SoldierGunSeconds = 1.5f;

    /// <summary>A holstered weapon's clip is remembered from when it was last in hand for this long out of a fight.</summary>
    public const float SoldierClipMemorySeconds = 20f;

    /// <summary>Stock's desired attack range for a Soldier (tf_bot.cpp:3207-3210): an enemy within it makes a banner worth popping.</summary>
    public const float SoldierAttackRange = 1250f;

    /// <summary>A full banner with a teammate near is popped after this long whatever is in sight.</summary>
    public const float SoldierBannerHoldSeconds = 30f;

    /// <summary>A banner out this long with the meter still full is given up for the launcher (its deploy and a press take well under it), and not taken out again for this long.</summary>
    public const float SoldierBannerPressSeconds = 1.5f, SoldierBannerRestSeconds = 10f;

    /// <summary>The Direct Hit aims at the feet only of a target within this and within this height of the bot.</summary>
    public const float SoldierDirectFeet = 400f, SoldierDirectLevel = 30f;

    /// <summary>Sentry shelling: the band as a share of the sentry's range (stock 1.1-1.5), a sentry remembered this long, the health share over which, the branch's score (under the points' 0.50 until a run against an Engineer's nest shows it pays: the review's C2), and the sentry's body's height.</summary>
    public const float SoldierShellNear = 1.1f, SoldierShellFar = 1.5f, SoldierShellMemorySeconds = 45f, SoldierShellHealth = 0.5f, SoldierShellUtility = 0.45f,
                       SentryBody = 32f;

    /// <summary>Sentry shelling's reach (stock's most a Soldier attacks from, tf_bot.cpp:3164-3166) and how long one sentry is shelled before the bot gives it up (research §4.3: 15 s).</summary>
    public const float SoldierShellReach = 3000f, SoldierShellGiveUpSeconds = 15f;

    /// <summary>A Pyro facing the bot within this, and within this reach (its deflection 128 u and a rocket's 256 u of flight over a reaction), would send a rocket back.</summary>
    public const float PyroFacingDegrees = 30f, PyroReflectReach = 384f;

    /// <summary>The Soldier's target bonuses (research [W2]'s order): a Sniper, an Engineer; a Scout past this far is worth less (don't chase it).</summary>
    public const float SniperBonus = 0.25f, EngineerBonus = 0.2f, ScoutFarDiscount = 0.15f, SoldierScoutChase = 600f;

    /// <summary>The Black Box's heal on a hit, which lets a roamer stay in longer.</summary>
    internal const string BlackBoxHeal = "add_health_on_radius_damage";

    /// <summary>The roamer's numbers (research §4.3 C): its jump floor, its health shares, its say in the team and fight branches, its extra on a Medic and a Sniper (over the general Medic bonus and the Soldier's Sniper bonus).</summary>
    public const float RoamerJumpFloor = 70f, RoamerHurtShare = 0.35f, RoamerBlackBoxShare = 0.3f, RoamerTeamWeight = 0.85f,
                       RoamerMedicBonus = 0.25f, RoamerSniperBonus = 0.15f;

    /// <summary>The banner Soldier's say in the team branch (research §4.3 D: it stays with its team, where its rage fills and its pop reaches them).</summary>
    public const float BannerTeamWeight = 1.3f;

    /// <summary>The Direct Hit's band, its weight on shelling a sentry, its airshot bonus and the vertical speed of a target in the air (research §4.3 E).</summary>
    public const float DirectNear = 500f, DirectFar = 1500f, DirectShellWeight = 1.1f, AirshotBonus = 0.3f, AirborneSpeed = 200f;
}
