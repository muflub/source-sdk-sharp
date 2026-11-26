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

public sealed partial class TFDecider
{
    /// <summary>
    /// Whom the Pyro shoves, and why (the user's request of 2026-11-22: push people off objectives, push Übered Medics away
    /// from the one they heal, push people off cliffs; stock's <c>ShouldFireCompressionBlast</c>, tf_bot.cpp:4111-4144, for
    /// the rest). Every candidate is an enemy in sight within <see cref="PyroPlay.ShoveRange"/>. The game's push sends him
    /// along the line from the Pyro to him (its yaw; TFFlameEffects.DeflectPlayer: the aim's pitch with the yaw to the target),
    /// so a shove that must send him a given way waits until the Pyro stands on the far side of him from it ("aligned").
    /// In order:
    /// <list type="number">
    /// <item>Split: an Übered pair, a Medic and the one he heals both invulnerable within the beam's stick range; the
    /// one whose push takes him away from the other.</item>
    /// <item>Objective: an enemy within <see cref="OnPointUnits"/> of a point the Pyro's team owns or a point being captured,
    /// pushed away from its middle; or the enemy carrying the Pyro's team's flag, pushed back from his capture zone (never along
    /// his escape: then no shove at all, and the flame).</item>
    /// <item>Lethal: a push that sends him into a death pit (a killing trigger_hurt, which kills through an Über too).</item>
    /// <item>Über: an invulnerable enemy (stock's "always shove ubers").</item>
    /// <item>Fall: a push over a drop past the safe fall, for its fall damage and distance (TF2's fall damage seldom kills).</item>
    /// <item>Stock's juggle and "pushy" draws, on the target only.</item>
    /// </list>
    /// </summary>
    private sealed partial class PyroPlay
    {
        /// <summary>
        /// The medigun's beam: its range (stock's script <c>Range</c>, 450 u) and the stick range it breaks past
        /// (<c>CWeaponMedigun::GetStickRange</c>, 1.2 x the range, tf_weapon_medigun.cpp:497-500; the port's
        /// TFMedigunWeapon.StickRangeMult). An Übered pair stands within the stick range.
        /// </summary>
        public const float BeamRange = 450f, BeamStick = BeamRange * 1.2f;

        /// <summary>A shove that must send its target a given way waits until the push's flat way and that way agree this much (a cosine).</summary>
        public const float ShoveAlign = 0.3f;

        /// <summary>An enemy within this of a point's middle is pushed off it whichever way the push goes.</summary>
        public const float PointMiddle = 64f;

        /// <summary>
        /// How far an airblast push surely carries a body on the ground: the cray push's 600 u/s (TFAirBlastDefs.AirBlastPower)
        /// gives about 150 u in the air on its 100 u/s minimum lift (AirBlastGroundMinZ, gravity 800, ~0.25 s); landed, the
        /// lost footing (0.5 s at a tenth of the friction, TFMovementRules) slides a passive body about 140 u more before
        /// friction 4 stops it, about 400 u in all. 300 errs short: a body that fights the push travels less.
        /// </summary>
        public const float PushTravel = 300f;

        /// <summary>A drop at least this deep behind an enemy is worth a fall-damage shove: past the safe fall speed (650 u/s, TFDamageDefs.PlayerMaxSafeFallSpeed) at gravity 800, 264 u, and more.</summary>
        public const float LedgeDrop = 270f;

        /// <summary>The ledge test's step along the push's way.</summary>
        public const float LedgeProbeStep = 32f;

        /// <summary>
        /// A lower floor counts as straight past the edge where the walk broke when it begins within this of the edge: at a ledge
        /// the floor below starts about a hull's half width (16 u) from the face; behind a wall there is the wall too, and the
        /// near side's clearance (16 u + the wall + 16 u), so a room behind a wall is not a drop.
        /// </summary>
        public const float LedgeGap = 24f;

        /// <summary>A hurt zone below the edge counts as the pit's floor when its top is at most a body's height over his floor, and its bottom within this below it.</summary>
        public const float PitDepth = 3000f, BodyHeight = 72f;

        /// <summary>A step from one cell to the next along the walk rises or falls at most this (a stair's step, the step-up height).</summary>
        public const float StepRise = 18f;

        private long _capShoves, _uberSplits, _envShoves, _fallShoves;

        /// <summary>Why a shove: the order <see cref="ChooseShove"/> ranks them in.</summary>
        private enum ShoveWhy : byte
        {
            None = 0,
            Split,
            Objective,
            Lethal,
            Uber,
            Fall,
            Pushy,
        }

        /// <summary>
        /// The enemy to shove now and why (<see cref="ShoveWhy"/>); <paramref name="inCone"/> whether he is in the push's cone in
        /// front of the view now (the press waits for it; the look turns to him meanwhile).
        /// </summary>
        private ShoveWhy ChooseShove(AiVector eye, AiVector forward, out ActorFacts who, out bool inCone)
        {
            (who, inCone) = (default, false);
            ShoveWhy best = ShoveWhy.None;
            float cone = MathF.Cos(TFAirBlastDefs.AirBlastConeAngle * (MathF.PI / 180f));
            foreach (ref readonly ActorFacts f in D._view.Sighted)
            {
                if (TeamRule.Friends(D._self.Team, f.Team) || D._self.Origin.DistanceTo(f.Origin) > ShoveRange)
                    continue;
                ShoveWhy why = WhyShove(in f);
                if (why == ShoveWhy.None || (best != ShoveWhy.None && why >= best))
                    continue;
                best = why;
                who = f;
                inCone = (f.Origin + new AiVector(0f, 0f, 40f) - eye).Normalised().Dot(forward) >= cone;
            }

            return best;
        }

        /// <summary>Why <paramref name="f"/>, an enemy in reach, is worth a shove (<see cref="ShoveWhy.None"/> for not), by the archetype's rules.</summary>
        private ShoveWhy WhyShove(in ActorFacts f)
        {
            bool uber = (f.Conditions & ActorConditions.Invulnerable) != 0;
            if (uber && SplitsPair(in f))
                return ShoveWhy.Split;
            // The Backburner spends 50 of its 200 on a blast: an Über's pair or an Über only.
            if (IsBackburner)
                return uber ? ShoveWhy.Uber : ShoveWhy.None;
            // Our flag's carrier is shoved only back (review 2, T1): a push along his escape helps him away and holds the
            // flame off a second, so on his heels he is flamed instead.
            if (CarriesOurFlag(in f, out bool sendsBack))
                return sendsBack ? ShoveWhy.Objective : ShoveWhy.None;
            if (OffObjective(in f))
                return ShoveWhy.Objective;
            // A push into a death pit first: a trigger_hurt kills through an Über (TFDamageLayer's stock rule lets its damage
            // through). Then an Über (stock's "always shove ubers"), then a push over a drop for its fall damage.
            LedgeWhy ledge = IsGuard ? LedgeWhy.None : LedgeAhead(in f);
            if (ledge == LedgeWhy.Lethal)
                return ShoveWhy.Lethal;
            if (uber)
                return ShoveWhy.Uber;
            if (ledge == LedgeWhy.Fall)
                return ShoveWhy.Fall;
            if (D._target is null || f.Id != D._target.Id)
                return ShoveWhy.None;
            // The Guard keeps its tank for reflects and the flame (research A2): another enemy only near its charge, and only
            // while half the tank is left.
            if (IsGuard)
                return NearCharge(f.Origin, GuardShoveNearCharge) && D._self.Reserve >= GuardShoveFloor ? ShoveWhy.Pushy : ShoveWhy.None;
            if (MathF.Abs(f.Velocity.Z) > GroundedSpeed)
                return Consistent(0.5f, 0x4A55474C) < 0.5f ? ShoveWhy.Pushy : ShoveWhy.None;
            return Consistent(3f, 0x50555348) < 0.5f ? ShoveWhy.Pushy : ShoveWhy.None;
        }

        /// <summary>The flat way a shove from where the Pyro stands sends <paramref name="at"/>.</summary>
        private AiVector PushWay(AiVector at) => new AiVector(at.X - D._self.Origin.X, at.Y - D._self.Origin.Y, 0f).Normalised();

        /// <summary>
        /// Whether <paramref name="f"/>, invulnerable, is half of an Übered pair (a Medic and his patient, both invulnerable,
        /// within <see cref="BeamStick"/>: every player sees both glow and the beam between them) and a push from here takes him
        /// away from the other.
        /// </summary>
        private bool SplitsPair(in ActorFacts f)
        {
            foreach (ref readonly ActorFacts g in D._view.Sighted)
            {
                if (g.Id == f.Id || TeamRule.Friends(D._self.Team, g.Team) || (g.Conditions & ActorConditions.Invulnerable) == 0
                    || ((TFClass)f.Class != TFClass.Medic && (TFClass)g.Class != TFClass.Medic) || f.Origin.DistanceTo(g.Origin) > BeamStick)
                    continue;
                AiVector apart = new AiVector(f.Origin.X - g.Origin.X, f.Origin.Y - g.Origin.Y, 0f).Normalised();
                if (PushWay(f.Origin).Dot(apart) >= ShoveAlign)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="f"/> stands on an objective the Pyro would push him off: within <see cref="OnPointUnits"/> of a
        /// capture point its team owns or one being captured (the HUD shows both), pushed away from its middle (or anyhow within
        /// <see cref="PointMiddle"/> of it); or he carries the Pyro's team's flag (the HUD names the carrier).
        /// </summary>
        private bool OffObjective(in ActorFacts f)
        {
            if (D._view.Objectives is not { } objectives)
                return false;
            foreach (Objective o in objectives.All)
            {
                if (o.Kind != ObjectiveKind.Capture || o.State == ObjectiveState.Locked || f.Origin.DistanceTo(o.Position) > OnPointUnits)
                    continue;
                if (o.OwnerTeam != D._self.Team && o.State != ObjectiveState.Capturing)
                    continue;
                AiVector off = new(f.Origin.X - o.Position.X, f.Origin.Y - o.Position.Y, 0f);
                if (off.Length <= PointMiddle || PushWay(f.Origin).Dot(off.Normalised()) >= ShoveAlign)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="f"/> carries the Pyro's team's flag (the HUD names the carrier), and
        /// <paramref name="sendsBack"/> whether a push from here sends him back: away from his team's capture zone when the HUD
        /// shows it, else against his own run.
        /// </summary>
        private bool CarriesOurFlag(in ActorFacts f, out bool sendsBack)
        {
            sendsBack = false;
            if (D._view.Objectives is not { } objectives)
                return false;
            bool carries = false;
            AiVector? zone = null;
            foreach (Objective o in objectives.All)
            {
                if (o.Kind == ObjectiveKind.Carry && o.OwnerTeam == D._self.Team && o.State == ObjectiveState.Carried && o.CarrierSlot == f.Id.Slot)
                    carries = true;
                else if (o.Kind == ObjectiveKind.Deliver && o.OwnerTeam == f.Team)
                    zone = o.Position;
            }

            if (!carries)
                return false;
            AiVector way = PushWay(f.Origin);
            if (zone is { } z)
                sendsBack = way.Dot(new AiVector(f.Origin.X - z.X, f.Origin.Y - z.Y, 0f).Normalised()) >= ShoveAlign;
            else
            {
                var run = new AiVector(f.Velocity.X, f.Velocity.Y, 0f);
                sendsBack = run.Length > GroundedSpeed && way.Dot(run.Normalised()) <= -ShoveAlign;
            }

            return true;
        }

        /// <summary>What a push sends an enemy into (<see cref="LedgeAhead"/>).</summary>
        private enum LedgeWhy : byte
        {
            None = 0,

            /// <summary>Over a drop of <see cref="LedgeDrop"/> or more: fall damage and distance, seldom a kill (TF2's fall damage, 5 x speed / 300 of a hundred health, kills only the hurt).</summary>
            Fall,

            /// <summary>Into a hurt zone that kills (a death pit, <see cref="ZoneKind.Hurt"/>).</summary>
            Lethal,
        }

        /// <summary>
        /// Where a push from here sends <paramref name="f"/>, within <see cref="PushTravel"/>: the walk follows the nav graph's
        /// Walk and Door edges from his cell along the push's way (a stair is walked up, never read through), and a hurt zone
        /// on the way at his feet is <see cref="LedgeWhy.Lethal"/>. Where the walk breaks (no edge to a cell under the next
        /// step): a hurt zone in the column past the edge, its top no higher than his body and within <see cref="PitDepth"/>
        /// below, is <see cref="LedgeWhy.Lethal"/>; a floor <see cref="LedgeDrop"/> or more below that begins within
        /// <see cref="LedgeGap"/> of the edge is <see cref="LedgeWhy.Fall"/>; anything else (a wall, a room behind it, a gap the
        /// graph cannot vouch for) is none.
        /// </summary>
        private LedgeWhy LedgeAhead(in ActorFacts f)
        {
            if (D._graph is not { } graph)
                return LedgeWhy.None;
            CellId here = graph.FindCell(f.Origin + new AiVector(0f, 0f, 8f));
            if (!here.IsValid)
                return LedgeWhy.None;
            int cur = here.Index;
            float floor = graph.Cell(cur).FloorZ;
            AiVector way = PushWay(f.Origin);
            int steps = (int)MathF.Ceiling(PushTravel / LedgeProbeStep);
            for (int i = 1; i <= steps; i++)
            {
                AiVector p = f.Origin + way * MathF.Min(i * LedgeProbeStep, PushTravel);
                if (InHurt(p.X, p.Y, floor + 16f, floor + 16f))
                    return LedgeWhy.Lethal;
                if (Holds(graph.Cell(cur), p))
                    continue;
                int next = WalkOn(graph, cur, p, floor);
                if (next >= 0)
                {
                    (cur, floor) = (next, graph.Cell(next).FloorZ);
                    continue;
                }

                // The walk breaks: what lies straight past the cell's edge.
                AiVector edge = f.Origin + way * ExitAlong(graph.Cell(cur), f.Origin, way);
                AiVector past = edge + way * LedgeGap;
                if (InHurt(past.X, past.Y, floor - PitDepth, floor + BodyHeight))
                    return LedgeWhy.Lethal;
                CellId under = graph.FindCell(new AiVector(past.X, past.Y, floor + 8f));
                return under.IsValid && floor - graph.Cell(under).FloorZ >= LedgeDrop ? LedgeWhy.Fall : LedgeWhy.None;
            }

            return LedgeWhy.None;
        }

        /// <summary>The cell a Walk or Door edge from <paramref name="cell"/> leads to that holds <paramref name="p"/>, a step's rise from <paramref name="floor"/>; -1 for none.</summary>
        private static int WalkOn(NavGraph graph, int cell, AiVector p, float floor)
        {
            foreach (NavEdge e in graph.EdgesFrom(cell))
            {
                if (e.Type is not (NavEdgeType.Walk or NavEdgeType.Door))
                    continue;
                NavCell c = graph.Cell(e.To);
                if (Holds(c, p) && MathF.Abs(c.FloorZ - floor) <= StepRise)
                    return e.To;
            }

            return -1;
        }

        private static bool Holds(in NavCell c, AiVector p) => p.X >= c.MinX && p.X < c.MaxX && p.Y >= c.MinY && p.Y < c.MaxY;

        /// <summary>How far along <paramref name="way"/> from <paramref name="from"/> the line leaves cell <paramref name="c"/>'s footprint.</summary>
        private static float ExitAlong(in NavCell c, AiVector from, AiVector way)
        {
            float t = float.MaxValue;
            if (way.X > 1e-4f)
                t = Math.Min(t, (c.MaxX - from.X) / way.X);
            else if (way.X < -1e-4f)
                t = Math.Min(t, (c.MinX - from.X) / way.X);
            if (way.Y > 1e-4f)
                t = Math.Min(t, (c.MaxY - from.Y) / way.Y);
            else if (way.Y < -1e-4f)
                t = Math.Min(t, (c.MinY - from.Y) / way.Y);
            return t == float.MaxValue ? 0f : Math.Max(0f, t);
        }

        /// <summary>Whether a hurt zone of the map (<see cref="ZoneKind.Hurt"/>) holds the column at (<paramref name="x"/>, <paramref name="y"/>) anywhere between <paramref name="low"/> and <paramref name="high"/>.</summary>
        private bool InHurt(float x, float y, float low, float high)
        {
            foreach (TeamZone z in D._map.TeamZones)
            {
                if (z.Kind == ZoneKind.Hurt && x >= z.Mins.X && x <= z.Maxs.X && y >= z.Mins.Y && y <= z.Maxs.Y && z.Maxs.Z >= low && z.Mins.Z <= high)
                    return true;
            }

            return false;
        }

        /// <summary>Counts a pressed shove by its reason.</summary>
        private void CountShove(ShoveWhy why)
        {
            _shoves++;
            _uberShoves += why == ShoveWhy.Uber ? 1 : 0;
            _uberSplits += why == ShoveWhy.Split ? 1 : 0;
            _capShoves += why == ShoveWhy.Objective ? 1 : 0;
            _envShoves += why == ShoveWhy.Lethal ? 1 : 0;
            _fallShoves += why == ShoveWhy.Fall ? 1 : 0;
        }

        private void CountShoves(List<KeyValuePair<string, long>> into)
        {
            into.Add(new("pyro.cap_shoves", _capShoves));
            into.Add(new("pyro.uber_splits", _uberSplits));
            into.Add(new("pyro.env_shoves", _envShoves));
            into.Add(new("pyro.fall_shoves", _fallShoves));
        }
    }
}
