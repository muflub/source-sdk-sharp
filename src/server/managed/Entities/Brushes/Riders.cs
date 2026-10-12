using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Who is standing on a conveyor, and handing them its surface velocity.
///
/// <para>
/// Extracted from <see cref="FuncConveyor"/>. A stationary belt speaks base
/// velocity (<see cref="Ride"/>), the engine's word for "moved by something
/// else", because it has no step of its own to give. A brush that itself
/// moves does not carry through here: its riders are in its push list, as
/// stock's are (<see cref="PhysicsPushedEntities"/>, CPushBlockerEnum's
/// IsStandingOnPusher).
/// </para>
///
/// <para>
/// It runs every tick while the belt works: the thing being fed runs per
/// tick, and feeding it slower delivers kicks instead of a ride. The physics
/// solver ignores base velocity, so a crate on a belt is left behind the same
/// way a crate on a stock conveyor is.
/// </para>
/// </summary>
internal static class Riders
{
    /// <summary>
    /// How far above the top face a rider may be. The movement code decides
    /// somebody is standing on something by tracing two units down, so anything
    /// it would call supported is within two units of the surface.
    /// </summary>
    public const float Reach = 2f;

    /// <summary>
    /// And how far into it: a hull resting on a face sits a hair inside it as
    /// often as a hair above.
    /// </summary>
    public const float Sink = 4f;

    /// <summary>
    /// A mover is a floor, and a floor has a crowd on it rather than a map's
    /// worth of entities. Overflowing this drops arbitrary riders, so it says so.
    /// </summary>
    public const int Max = 64;

    /// <summary>
    /// Standing on a box: feet at its top face, and somewhere over its footprint.
    ///
    /// <para>
    /// Both halves are as loose as the contact they describe. The footprint test
    /// is an overlap rather than a containment, because a player half off the
    /// edge is still standing on it, and the vertical band is the reach of the
    /// movement code's own ground trace, because being carried and being
    /// supported should be the same question answered twice.
    /// </para>
    /// </summary>
    public static bool Riding(Vector centre, Vector half, Vector footMins, Vector footMaxs)
    {
        float top = centre.Z + half.Z;

        if (footMins.Z < top - Sink || footMins.Z > top + Reach)
            return false;

        return footMaxs.X > centre.X - half.X && footMins.X < centre.X + half.X
            && footMaxs.Y > centre.Y - half.Y && footMins.Y < centre.Y + half.Y;
    }

    /// <summary>
    /// One tick of a STATIONARY belt carrying: everyone standing on the box is
    /// handed its surface velocity through base velocity.
    ///
    /// <para>
    /// The belt keeps the velocity channel because it has no step to give —
    /// the brush never moves — and because the channel's vices do not bite
    /// here: the drift that made a train rider outrun the train just moves a
    /// belt rider along a floor that is not going anywhere, and the impulse a
    /// rider keeps on stepping off the end is how a belt has always thrown
    /// things. Renewed every tick, because base velocity is consumed and
    /// cleared per command and a slower renewal delivers kicks instead of a
    /// ride.
    /// </para>
    /// </summary>
    public static void Ride(IEngineWorldQuery world, IEngineNetworking net, IEngineEntities entities,
                            EntityHandle self, Vector origin, Vector velocity, string owner, ILog log)
    {
        var (mins, maxs) = entities.GetBounds(self);

        Vector centre = origin + ((mins + maxs) * 0.5f);
        Vector half = (maxs - mins) * 0.5f;

        Span<EntityHandle> found = stackalloc EntityHandle[Max];
        int count = world.EntitiesInSphere(centre, half.Length + 16f, found);

        if (count == found.Length)
        {
            log.Warning($"[SourceSharp] {owner}: more than {found.Length} entities " +
                        $"overlapping it; some were not carried\n");
        }

        for (int i = 0; i < count; i++)
        {
            if (found[i].Equals(self))
                continue;

            net.EntityGetState(found[i], out MoveType moveType);

            if (moveType != MoveType.Walk && moveType != MoveType.Step)
                continue;

            var (footMins, footMaxs) = entities.GetBounds(found[i]);
            Vector footOrigin = entities.GetOrigin(found[i]);

            if (Riding(centre, half, footOrigin + footMins, footOrigin + footMaxs))
                entities.AddBaseVelocity(found[i], velocity);
        }
    }
}
