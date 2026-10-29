//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>masscenteroverride_t (physics.h): where a named entity's mass centre goes when it is made.</summary>
public readonly record struct MassCenterOverride(string EntityName, Vector Center, Vector Axis, MassCenterAlign Align)
{
    /// <summary>SnapToPoint: the mass centre at a world point.</summary>
    public static MassCenterOverride SnapToPoint(string name, Vector pointWS)
        => new(name, pointWS, Vector.Zero, MassCenterAlign.Point);

    /// <summary>SnapToAxis: the mass centre moved onto a world line, along it.</summary>
    public static MassCenterOverride SnapToAxis(string name, Vector axisStartWS, Vector unitAxisDirWS)
        => new(name, axisStartWS, unitAxisDirWS, MassCenterAlign.Axis);
}

/// <summary>masscenteroverride_t::align_type.</summary>
public enum MassCenterAlign
{
    Point = 0,
    Axis = 1,
}

/// <summary>
/// CPhysicsHook's m_massCenterOverrides (physics.cpp): the overrides phys_hinge
/// and info_mass_center leave by name at spawn, each taken by the first
/// physics prop or physbox of that name made after it, and purged with the
/// level.
/// </summary>
public sealed class MassCenterOverrides : IGameLifecycle
{
    /// <summary>DIST_EPSILON: an override nearer the origin than this is no override.</summary>
    public const float DistEpsilon = 0.03125f;

    private readonly List<MassCenterOverride> _overrides = new();

    public int Count => _overrides.Count;

    /// <summary>PhysSetMassCenterOverride: an override with no name is dropped.</summary>
    public void Add(in MassCenterOverride entry)
    {
        if (!string.IsNullOrEmpty(entry.EntityName))
            _overrides.Add(entry);
    }

    /// <summary>
    /// PhysGetMassCenterOverride: the override for <paramref name="entityName"/>,
    /// removed from the list, as the local mass centre of a body at
    /// <paramref name="origin"/> and <paramref name="angles"/> whose solid's
    /// own centre is <paramref name="massCenterLocal"/>. False when there is
    /// none, or when it lands within DIST_EPSILON of the body's origin.
    /// </summary>
    public bool TryTake(string entityName, Vector origin, QAngle angles, Vector massCenterLocal, out Vector massCenterOverride)
    {
        var entityToWorld = Angles.Vectors(angles);
        massCenterOverride = default;
        int index = string.IsNullOrEmpty(entityName) ? -1 : _overrides.FindIndex(o => o.EntityName == entityName);
        if (index < 0)
            return false;

        MassCenterOverride entry = _overrides[index];
        Vector massCenterWS = entry.Center;
        switch (entry.Align)
        {
            case MassCenterAlign.Point:
                massCenterOverride = ITransform(massCenterWS, origin, entityToWorld);
                break;
            case MassCenterAlign.Axis:
            {
                Vector defaultMassCenterWS = Transform(massCenterLocal, origin, entityToWorld);
                massCenterWS += entry.Axis * (defaultMassCenterWS.Dot(entry.Axis) - entry.Axis.Dot(entry.Center));
                massCenterOverride = ITransform(massCenterWS, origin, entityToWorld);
                break;
            }
        }

        // FastRemove: the last entry takes the removed one's place.
        _overrides[index] = _overrides[^1];
        _overrides.RemoveAt(_overrides.Count - 1);

        return massCenterOverride.Length > DistEpsilon;
    }

    // EntityToWorldTransform's columns are forward, left and up.
    private static Vector Transform(Vector local, Vector origin, in Angles.Basis b)
        => origin + b.Forward * local.X - b.Right * local.Y + b.Up * local.Z;

    private static Vector ITransform(Vector world, Vector origin, in Angles.Basis b)
    {
        Vector d = world - origin;
        return new Vector(d.Dot(b.Forward), -d.Dot(b.Right), d.Dot(b.Up));
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.MassCenterOverrides;

    void IGameLifecycle.OnLevelShutdown() => _overrides.Clear();
}
