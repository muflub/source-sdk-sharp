namespace SourceSharp.Abi;

/// <summary>
/// The owner of a physics object, read from the object's own game data word:
/// the managed twin of physics_gamedata.h's decode, for managed's vphysics
/// callbacks (plan_physics_managed B2).
///
/// <para>
/// A word with the top bit set is one of ours, the packed entity handle plus
/// one (SOURCESHARP_PHYSICS_GAMEDATA_TAG / _BIAS). Zero is no owner, and a
/// word without the tag is an SDK CBaseEntity pointer, which names no managed
/// entity: both answer <see cref="EntityHandle.None"/>, as the native event
/// ring's SS_PHYSEVENT_NO_ENTITY.
/// </para>
/// </summary>
public static class PhysicsGameData
{
    /// <summary>SOURCESHARP_PHYSICS_GAMEDATA_TAG: the top bit of a pointer-sized word.</summary>
    public const ulong Tag = 1UL << 63;

    /// <summary>SOURCESHARP_PHYSICS_GAMEDATA_BIAS.</summary>
    public const ulong Bias = 1;

    /// <summary>
    /// physics_gamedata.h's PhysicsGameDataFromHandle: the tagged word an object
    /// owned by <paramref name="handle"/> carries, or 0 for no owner.
    /// </summary>
    public static nint FromHandle(EntityHandle handle)
        => handle == EntityHandle.None ? 0 : unchecked((nint)(Tag | ((ulong)handle.Value + Bias)));

    public static EntityHandle Owner(nint gameData)
    {
        ulong word = unchecked((ulong)gameData);
        if ((word & Tag) == 0)
            return EntityHandle.None;

        return new EntityHandle(unchecked((uint)((word & ~Tag) - Bias)));
    }
}
