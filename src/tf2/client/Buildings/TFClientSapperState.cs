using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared.Player;

namespace TF2.Client.Buildings;

/// <summary>
/// The client's answer to the sapper's use
/// (<see cref="TF2.Shared.ITFSapperState"/>): the sap and the sapper's wound
/// are the server's, so the replay's asks — the placement, the finds, the
/// melee's building hit — answer none. The wrench's building hit is the
/// exception: stock's <c>CTFWrench::Smack</c> runs the swing's trace on BOTH
/// sides (tf_weapon_wrench.cpp:145-199, the CLIENT_DLL arm only notifies
/// haptics), so the replay traces the same swing and predicts the skip bit
/// itself — without it the mode word mispredicts on every swing into a
/// friendly building. The whack's outcome is still the server's: the sound
/// name rides the seam's one-time hand-out, not the bit.
/// </summary>
[SideService(Side.Client, As = [typeof(TF2.Shared.ITFSapperState)])]
public sealed class TFClientSapperState(IEngineWorldQuery world, IClientStandalones standalones,
                                        TFClientPlayerResources resources, IEngineEffects? effects = null)
    : TF2.Shared.ITFSapperState
{
    // The entity indices the clips run against: every index from 1 but the
    // tracer's own, as the arsenal's. Built once per tracer and kept.
    private readonly int[] _candidates = new int[EngineLimits.MaxEdicts - 2];
    private int _builtFor = -1;

    public bool PlaceSapper(int ownerIndex) => false;

    public bool FindSappable(int ownerIndex, out EntityHandle building, out Vector point)
    {
        building = EntityHandle.None;
        point = default;
        return false;
    }

    /// <summary>
    /// The wrench's building hit as the replay sees it
    /// (<c>CTFWrench::Smack</c>, tf_weapon_wrench.cpp:145-199): the same
    /// line-and-entities trace the medigun's replay runs — the world's line
    /// to its stop, then the entities before that stop — over the owner's
    /// eyes and the replayed command's view angles, at the swing's reach of
    /// 70 (:165). A struck building of the owner's team answers
    /// <see cref="TF2.Shared.WrenchSmackResult.Useful"/>: the client cannot
    /// know whether the whack did work, so Useful here means only that a
    /// friendly building was struck — enough for the skip bit both sides set.
    /// </summary>
    public TF2.Shared.WrenchSmackResult WrenchSmack(int ownerIndex, float range)
    {
        if (standalones.GetByIndex(ownerIndex) is not ClientSsPlayer owner)
            return TF2.Shared.WrenchSmackResult.None;

        // The eyes: the owner's standing origin plus the view offset, the
        // networked word. The aim: the replayed command's view angles — the
        // seam's GetEyeAngles answers default on a client.
        Vector eye = owner.State.Origin + owner.State.ViewOffset;
        Vector aim = Angles.Forward(owner.EyeAngles());
        Vector reach = eye + aim * range;

        // Stock's Smack order (tf_weapon_wrench.cpp:170-186): the thin line
        // first — the world's brush to its stop, the entities before that
        // stop — and only when the thin passes stop on nothing does the ±18
        // hull sweep the same stretch. Players ride through the filter as the
        // one ignored index; the named entity is the pick, stock's
        // trace.m_pEnt read the same way.
        var line = world.ClientTraceLine(eye, reach, TraceMask.Solid, TraceFilter.Simple, ownerIndex);
        var entities = world.ClientClipRayToEntities(eye, line.Hit ? line.End : reach, TraceMask.Solid,
                                                     Candidates(ownerIndex));

        ClientBoxTrace hull = default;
        if (!line.Hit && !entities.Hit)
        {
            hull = world.ClientTraceHullEntities(eye, reach,
                new Vector(-SwingHalf, -SwingHalf, -SwingHalf),
                new Vector(SwingHalf, SwingHalf, SwingHalf),
                TraceMask.Solid, ownerIndex, Candidates(ownerIndex));
        }

        int struck = entities.Hit ? entities.EntityIndex
                 : line.Hit ? line.EntityIndex
                 : hull.Hit ? hull.EntityIndex
                 : 0;

        if (struck > 0)
        {
            // OnFriendlyBuildingHit's test: the struck entity is one of the
            // building mirrors, and it wears the owner's team. The team of the
            // building crosses on its own net state; the owner's comes from the
            // scoreboard, as the beam's replay reads it. Whoever else stopped
            // the swing is no friendly building (stock's else arm).
            return standalones.GetByIndex(struck) is TFClientBuilding struckBuilding
                   && struckBuilding.State.Team == TeamOf(ownerIndex)
                ? TF2.Shared.WrenchSmackResult.Useful
                : TF2.Shared.WrenchSmackResult.None;
        }

        // Nothing named: stock's fallback — the nearest friendly building
        // whose centre rides the swing's stretch, the hull's half-width as
        // the give, the thin line's stop shortening the stretch. The engine's
        // answer is the closest entity, not the first enumerated, so the
        // nearest along the aim wins ties.
        float shorten = entities.Hit ? entities.Fraction
                      : line.Hit ? line.Fraction
                      : 1f;

        int bestIndex = 0;
        float bestAlong = 0f;
        foreach (ClientStandaloneEntity entity in standalones.Live())
        {
            if (entity is not TFClientBuilding friendly || friendly.State.Team != TeamOf(ownerIndex))
                continue;

            if (!(effects is { } fx && fx.ClientEntityOrigin(friendly.Handle.EntryIndex, out Vector origin)))
                continue;

            if (PointLineDistance(eye, reach, origin) > SwingHalf)
                continue;

            float along = (origin - eye).Dot(aim);
            if (along <= 0f || shorten * range < along)
                continue;

            if (bestIndex != 0 && along >= bestAlong)
                continue;

            bestIndex = friendly.Handle.EntryIndex;
            bestAlong = along;
        }

        return bestIndex != 0 ? TF2.Shared.WrenchSmackResult.Useful : TF2.Shared.WrenchSmackResult.None;
    }

    /// <summary>The swing's sweep half-width, stock's hull the miss falls back
    /// to (<c>tf_weapon_wrench.cpp:177</c>, the server's <c>SwingHull</c>).</summary>
    private const float SwingHalf = 18f;

    /// <summary>The distance from the point to the segment, its ends included
    /// (the server's pick reads it the same way).</summary>
    private static float PointLineDistance(Vector from, Vector to, Vector point)
    {
        Vector span = to - from;
        float lengthSquared = span.LengthSquared;
        if (lengthSquared < 1f)
            return (point - from).Length;

        float t = Math.Clamp((point - from).Dot(span) / lengthSquared, 0f, 1f);
        return (point - (from + span * t)).Length;
    }

    /// <summary>The sounds are the server's, as stock's under
    /// <c>CDisablePredictionFiltering</c>: the replay adds none of its own.</summary>
    public string? TakeWrenchSound(int ownerIndex) => null;

    public bool MeleeSmack(int ownerIndex, float range, float amount) => false;

    /// <summary>The player's team from the scoreboard, 0 outside its slots.</summary>
    private int TeamOf(int playerIndex)
        => playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? resources.Live?.State.Team[playerIndex] ?? 0
            : 0;

    private ReadOnlySpan<int> Candidates(int tracerIndex)
    {
        if (_builtFor != tracerIndex)
        {
            int count = 0;
            for (int index = 1; index < EngineLimits.MaxEdicts && count < _candidates.Length; index++)
            {
                if (index != tracerIndex)
                    _candidates[count++] = index;
            }

            _builtFor = tracerIndex;
        }

        return _candidates;
    }
}
