//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Ai.TF2.Trees;

public sealed partial class TFDecider
{
    /// <summary>
    /// The Demoman's sticky trap (stock's CTFBotPrepareStickybombTrap, research §4.3, on honest inputs): on the defending
    /// role (or as the Trapper, whatever the role) in a lull, stickies laid at the objective the enemy may take next and on
    /// its enemy side, a few to a spot so one blast cannot clear them; the Trapper then watches it from his own side. The
    /// detonation is the play's own (an enemy in sight by an armed sticky); the Scottish Resistance's in view.
    /// </summary>
    private sealed partial class DemomanPlay
    {
        private int _lastHealth = -1;
        private Tick _hurtAt = new(int.MinValue / 2), _trapLayingAt = new(-1);
        private AiVector _trapSpot, _trapCentre, _trapEnemySide;
        private long _trapLaid, _watchDecides, _inViewTurns;

        /// <summary>Whether the bot plays the Trapper (the archetype's name: a pin or its seed's draw).</summary>
        private bool Trapper => string.Equals(Archetype, TFDemomanClass.Trapper, StringComparison.OrdinalIgnoreCase);

        /// <summary>The stickies a trap holds: the Hybrid's few, the Trapper's all but two of his launcher's.</summary>
        private int TrapSize => Trapper ? Math.Max(TFDemomanClass.HybridTrapStickies, Kit.MaxStickies - 2) : TFDemomanClass.HybridTrapStickies;

        private int TrapPerSpot => Trapper ? 2 : 1;

        /// <summary>The Trapper defends whatever the team dealt (the wiki: "useless in offense"); the rest play the role dealt.</summary>
        public override TFTeamRole Role(TFTeamRole dealt) => Trapper ? TFTeamRole.Defend : dealt;

        /// <summary>Notes a hit since the last decide (health fell): a trap is not laid within a second of one (stock :86).</summary>
        private void NoteHurt()
        {
            // The first decide of a life only records (-1: none yet).
            if (_lastHealth >= 0 && D._self.Health < _lastHealth)
                _hurtAt = D._now;
            _lastHealth = D._self.Health;
        }

        /// <summary>
        /// The objective a trap guards: on a points level the nearest point (within <see cref="TFDemomanClass.TrapReach"/>)
        /// the enemy may capture now that is not theirs; in CTF the bot's own flag at home.
        /// </summary>
        private bool TrapCentre(out AiVector centre)
        {
            centre = default;
            if (D._view.Objectives is not { } objectives)
                return false;
            float best = TFDemomanClass.TrapReach;
            bool found = false;
            foreach (Objective o in objectives.All)
            {
                bool point = o.Kind == ObjectiveKind.Capture && (o.MayAct & D.EnemyBits) != 0 && o.State != ObjectiveState.Locked
                             && (o.OwnerTeam == D._self.Team || !TeamRule.IsGameTeam(o.OwnerTeam));
                bool flag = o.Kind == ObjectiveKind.Carry && o.OwnerTeam == D._self.Team && o.State == ObjectiveState.Home;
                if (!point && !flag)
                    continue;
                float d = D._self.Origin.DistanceTo(o.Position);
                if (d < best)
                    (best, centre, found) = (d, o.Position, true);
            }

            return found;
        }

        /// <summary>The way from <paramref name="centre"/> toward the enemy: to their nearest spawn, flat; with none, from the bot to it.</summary>
        private AiVector EnemySide(AiVector centre)
        {
            float best = float.MaxValue;
            AiVector way = default;
            foreach (TeamSpawn s in D._map.TeamSpawns)
            {
                if (!TeamRule.IsGameTeam(s.Team) || s.Team == D._self.Team)
                    continue;
                float d = centre.DistanceTo(s.Position);
                if (d < best)
                    (best, way) = (d, s.Position - centre);
            }

            if (best == float.MaxValue)
                way = centre - D._self.Origin;
            way = new AiVector(way.X, way.Y, 0f).Normalised();
            return way.LengthSquared < 0.5f ? new AiVector(1f, 0f, 0f) : way;
        }

        /// <summary>The spots a trap may use (the table's seven); the trap's size stops it first.</summary>
        private const int TrapSpots = 7;

        /// <summary>The next spot of the trap that wants a sticky, or false when the trap is full.</summary>
        private bool NextTrapSpot(AiVector centre, AiVector side, out AiVector spot)
        {
            spot = default;
            int total = 0;
            for (int i = 0; i < _outCount; i++)
                total += _out[i].At.DistanceTo(centre) <= TFDemomanClass.TrapSpread * 2.5f ? 1 : 0;
            // Only the stickies by the trap's objective count: those by one gone by are stale, and the next lay recycles them.
            if (total >= TrapSize)
                return false;
            for (int k = 0; k < TrapSpots; k++)
            {
                AiVector at = TFDemomanClass.TrapSpotAt(centre, side, k);
                int here = 0;
                for (int i = 0; i < _outCount; i++)
                    here += _out[i].At.DistanceTo(at) <= TFDemomanClass.TrapSpread * 0.6f ? 1 : 0;
                if (here < TrapPerSpot)
                {
                    spot = at;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the bot may lay a trap now (stock's gates): stickies in its kit, the defending role (or the Trapper), not
        /// carrying, no enemy target in sight nor one it knows of within <see cref="TFDemomanClass.TrapQuietUnits"/> (stock :188), not hit
        /// within <see cref="TFDemomanClass.TrapHurtSeconds"/> (:86).
        /// </summary>
        private bool TrapQuiet()
        {
            if (!Kit.Stickies || StickyKind < 0 || D.Carrying || D.Role != TFTeamRole.Defend)
                return false;
            if ((D._now - _hurtAt) * D._tick < TFDemomanClass.TrapHurtSeconds)
                return false;
            // The target is the enemy the bot knows of nearest to mattering: one in sight, or any within the quiet reach.
            return D._target is null || (!D._targetSighted && D._targetDist >= TFDemomanClass.TrapQuietUnits);
        }

        /// <summary>The trap's score: laying it, <see cref="TFDemomanClass.TrapUtility"/>; the Trapper watching a laid one, <see cref="TFDemomanClass.WatchUtility"/>.</summary>
        private float TrapUtility()
        {
            if (!TrapCentre(out AiVector centre))
                return 0f;
            (_trapCentre, _trapEnemySide) = (centre, EnemySide(centre));
            if (TrapQuiet() && NextTrapSpot(_trapCentre, _trapEnemySide, out _trapSpot))
                return TFDemomanClass.TrapUtility;
            return Trapper && Kit.Stickies && !D.Carrying && TrapCount() >= TFDemomanClass.HybridTrapStickies ? TFDemomanClass.WatchUtility : 0f;
        }

        private int TrapCount()
        {
            int n = 0;
            for (int i = 0; i < _outCount; i++)
                n += _out[i].At.DistanceTo(_trapCentre) <= TFDemomanClass.TrapSpread * 2.5f ? 1 : 0;
            return n;
        }

        /// <summary>
        /// The trap's leaf: within <see cref="TFDemomanClass.TrapLayFrom"/> of the spot (and out of its blast) the bot stands and
        /// the hands lay a sticky there; further, it goes to the trap's own side; a full trap the Trapper watches from
        /// <see cref="TFDemomanClass.TrapWatchUnits"/> back on his side, looking at it.
        /// </summary>
        private bool GoTrap()
        {
            AiVector self = D._self.Origin;
            if (TrapQuiet() && NextTrapSpot(_trapCentre, _trapEnemySide, out _trapSpot))
            {
                float d = self.DistanceTo(_trapSpot);
                if (d <= TFDemomanClass.TrapLayFrom && d > TFDemomanClass.SelfSafe + 30f)
                {
                    D._move = MoveIntent.Hold;
                    _trapLayingAt = D._now;
                    D._verdict = "laying a trap";
                    return true;
                }

                // To a place on the bot's own line to the spot (where it came from, so a place it can stand on): far, near
                // enough to lob; on top of it, back out of the sticky's blast. A fixed place behind the objective was in a
                // wall on ctf_2fort, and the defenders stood by their flag walking to it all round.
                AiVector from = new AiVector(self.X - _trapSpot.X, self.Y - _trapSpot.Y, 0f);
                from = from.LengthSquared < 1f ? -_trapEnemySide : from.Normalised();
                float stand = d > TFDemomanClass.TrapLayFrom ? TFDemomanClass.TrapLayFrom * 0.6f : TFDemomanClass.SelfSafe + 120f;
                D.GoTo(_trapSpot + from * stand, MoveSpeed.Run);
                D._verdict = "to lay a trap";
                return true;
            }

            AiVector watch = _trapCentre - _trapEnemySide * TFDemomanClass.TrapWatchUnits;
            _watchDecides++;
            if (self.DistanceTo(watch) > TFDemomanClass.TrapWatchArrive)
                D.GoTo(watch, MoveSpeed.Run);
            else
                D._move = MoveIntent.Hold;
            D._look = new LookIntent(LookKind.Point, _trapCentre + new AiVector(0f, 0f, 16f), ActorId.None, AimPoint.Body, 0f, 0);
            D._verdict = "watching a trap";
            return true;
        }

        private bool LayingTrap => _trapLayingAt == D._now;

        /// <summary>A sticky lobbed at the trap's spot being laid (no shot at nothing: the spot is the objective's own floor).</summary>
        private void StickyAtSpot()
        {
            if (!D.HeldFacts(out WeaponFacts own) || own.ProjectileSpeed <= 0f)
                return;
            AiVector eye = D.OwnEye;
            AiVector aim = _trapSpot + new AiVector(0f, 0f, 4f);
            AiVector point = AimLead.Lob(eye, aim, own.ProjectileSpeed, own.ProjectileGravity) ?? aim;
            D._look = new LookIntent(LookKind.Point, point, ActorId.None, AimPoint.Feet, 0f, 0);
            if (D._self.Clip <= 0 || !LobSettled(point))
                return;
            Fire(aim, eye, own.ProjectileSpeed);
            _trapLaid++;
        }

        /// <summary>
        /// The Scottish Resistance's detonation goes for the stickies in view only (TFProjectileEffects' in-view mode,
        /// stock ModifyPipebombsInView: within the 0.975 dot): the view comes onto <paramref name="at"/> first, and the
        /// detonation goes once it is there. True when the view is on it.
        /// </summary>
        private bool InViewOf(AiVector at)
        {
            if (!Kit.InViewDetonate)
                return true;
            AiVector eye = D.OwnEye;
            AiVector to = at + new AiVector(0f, 0f, 4f) - eye;
            float dist = to.Length;
            if (dist < 1f || VisionMath.Forward(D._self.Eye).Dot(to * (1f / dist)) > TFDemomanClass.InViewDot)
                return true;
            D._look = new LookIntent(LookKind.Point, at, ActorId.None, AimPoint.Feet, 0f, 0);
            D._action = ActionIntent.None;
            _inViewTurns++;
            return false;
        }
    }
}
