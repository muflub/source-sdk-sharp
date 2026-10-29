//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_laserdot: the point the RPG paints, stock's CLaserDot
/// (shared/hl2mp/weapon_rpg.cpp).
///
/// <para>
/// The weapon owns it and moves it to the end of its owner's aim every frame
/// (<c>CWeaponRPG::UpdateLaserPosition</c>, the arms' <c>UpdateLaserDot</c>);
/// a missile in flight looks among the dots its own shooter owns for the
/// nearest one that is on, and steers for it (<c>CMissile::SeekThink</c>).
/// The roster is stock's <c>g_LaserDotList</c>. Nothing on this side draws:
/// the client draws the dot whenever the entity exists, on or off, as
/// <c>C_LaserDot::DrawModel</c> does (its TurnOn/TurnOff touch only
/// <c>m_bIsOn</c>).
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvLaserdot, ScriptedEntityKind.Effect)]
[Precache(PrecacheKind.Model, Sprite)]
public sealed class LaserDot : ScriptedEntity
{
    public LaserDot(IEntityRoster<LaserDot> dots)
    {
        _dots = dots;
    }

    private readonly IEntityRoster<LaserDot> _dots;

    /// <summary>RPG_LASER_SPRITE, which the client binds to draw the dot.</summary>
    internal const string Sprite = "sprites/redglow1.vmt";

    [NetState] public LaserDotNetState State = new();

    /// <summary>m_vecSurfaceNormal: the face the aim stopped on.</summary>
    public Vector SurfaceNormal { get; private set; }

    /// <summary>m_hTargetEnt: what the aim stopped on, when it can be hurt.</summary>
    public EntityHandle TargetEntity { get; set; } = EntityHandle.None;

    /// <summary>m_bIsOn. The constructor's true; the RPG turns it off as soon as it is made.</summary>
    public bool IsOn { get; private set; } = true;

    /// <summary>The player whose RPG owns it: m_hOwnerEntity.</summary>
    public EntityHandle OwnerEntity { get; private set; } = EntityHandle.None;

    public override void OnSpawn()
    {
        _dots.Add(this);

        // CLaserDot::Create: no movement, not solid, no shadow, an eight-unit box.
        Entities.SetBounds(Handle, new Vector(-4f, -4f, -4f), new Vector(4f, 4f, 4f));
    }

    public override void OnRemoved() => _dots.Remove(this);

    /// <summary>SetOwnerEntity, told to the client as an entindex.</summary>
    public void OwnedBy(EntityHandle owner)
    {
        OwnerEntity = owner;
        Entities.SetOwner(Handle, owner);
        State.OwnerIndex = owner == EntityHandle.None ? 0 : Entities.GetIndex(owner);
    }

    /// <summary>SetLaserPosition: where the aim stopped and the face it stopped on.</summary>
    public void SetLaserPosition(Vector origin, Vector normal)
    {
        Entities.SetOrigin(Handle, origin);
        SurfaceNormal = normal;
    }

    /// <summary>GetChasePosition: ten units into the face, so a missile meets it rather than skims it.</summary>
    public Vector ChasePosition => ChasePositionFor(Origin, SurfaceNormal);

    /// <summary>GetChasePosition's arithmetic, without an entity.</summary>
    internal static Vector ChasePositionFor(Vector origin, Vector normal) => origin - normal * 10f;

    public void TurnOn() => IsOn = true;

    public void TurnOff() => IsOn = false;

    /// <summary>The dot's position as the engine holds it.</summary>
    public Vector Position => Origin;
}
