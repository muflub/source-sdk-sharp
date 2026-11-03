//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>
/// The scope's server half: the owner's field of view and the zoom-in clock,
/// and the aim dot. Stock sets the FOV in <c>CTFWeaponBaseGun::ZoomIn</c>/
/// <c>ZoomOut</c> (<c>shared/tf/tf_weaponbase_gun.cpp:1046-1090</c>) —
/// <c>TF_WEAPON_ZOOM_FOV</c> 20 in, 0 (the player's own) back out, both over
/// 0.1 s — and reads the clock back in <c>CTFSniperRifle::CanFireCriticalShot</c>
/// (<c>tf_weapon_sniperrifle.cpp:1050-1057</c>): a headshot needs
/// <c>curtime - GetFOVTime()</c> of at least 0.2 s. The dot is stock's
/// <c>CreateSniperDot</c>/<c>DestroySniperDot</c>
/// (tf_weapon_sniperrifle.cpp:922-959): one <c>env_sniperdot</c> per zoomed
/// player, made on the zoom-in, gone on the zoom-out.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFZoomEffects)])]
public sealed class TFZoomEffects(TFZoomTimes times, IEnginePlayers players, IEngineEntities entities,
                                  IEntityDirectory directory, ILog log) : ITFZoomEffects
{
    /// <summary>TF_WEAPON_ZOOM_FOV (tf_shareddefs.h:399).</summary>
    public const int ZoomFov = 20;

    /// <summary>The FOV transition stock asks for, both ways (tf_weaponbase_gun.cpp:1060, 1082).</summary>
    public const float ZoomFovTime = 0.1f;

    private readonly Dictionary<int, EntityHandle> _dots = new();

    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed)
    {
        int playerIndex = attack.Command.OwnerIndex;
        if (zoomed)
        {
            times.Record(playerIndex, attack.Now);
            CreateDot(playerIndex, attack.Now);
        }
        else
        {
            times.Clear(playerIndex);
            DestroyDot(playerIndex);
        }

        int fov = zoomed ? ZoomFov : 0;
        attack.Arms.SetFov(fov, ZoomFovTime);
        log.Msg($"[TF2/Zoom] player {playerIndex} fov {fov} ({(zoomed ? "in" : "out")})\n");
    }

    /// <summary>
    /// <c>CreateSniperDot</c> (tf_weapon_sniperrifle.cpp:922-941): one dot per
    /// player. A dot the owner's death already removed (its think) leaves the
    /// handle stale; an invalid one is replaced rather than reused. The zoom's
   /// <paramref name="now"/> is the dot's charge start — stock's
    /// <c>ResetChargeTime</c> reads the same instant in the constructor the
    /// creation runs (<c>tf_weapon_sniperrifle.h:48</c>).
    /// </summary>
    private void CreateDot(int playerIndex, float now)
    {
        if (_dots.TryGetValue(playerIndex, out var live) && entities.IsValid(live))
            return;

        var handle = entities.Create(SniperDot.MapClass);
        if (!entities.IsValid(handle))
            return;

        // The directory learns the instance from the spawn's callback, so the
        // dress is post-spawn (the sentry rocket's and dropped weapon's order).
        entities.Spawn(handle);

        if (directory.Get(handle) is not SniperDot dot)
            return;

        dot.Begin(players.GetHandle(playerIndex), now);
        _dots[playerIndex] = handle;
    }

    /// <summary><c>DestroySniperDot</c> (tf_weapon_sniperrifle.cpp:946-959).</summary>
    private void DestroyDot(int playerIndex)
    {
        if (_dots.Remove(playerIndex, out var handle) && entities.IsValid(handle))
            entities.Remove(handle);
    }
}

/// <summary>
/// When each player zoomed in — stock's <c>GetFOVTime</c> — for the damage
/// layer's headshot rule: written by <see cref="TFZoomEffects"/> on the
/// owner's zoom, cleared on the zoom-out, so a player who never zoomed has no
/// entry and <see cref="ZoomedAt"/> answers 0.
/// </summary>
[SideService(Side.Server)]
public sealed class TFZoomTimes
{
    private readonly Dictionary<int, float> _zoomedAt = new();

    public void Record(int playerIndex, float now) => _zoomedAt[playerIndex] = now;

    public void Clear(int playerIndex) => _zoomedAt.Remove(playerIndex);

    /// <summary>The time the player last zoomed in; 0 when not zoomed.</summary>
    public float ZoomedAt(int playerIndex) => _zoomedAt.GetValueOrDefault(playerIndex);
}
