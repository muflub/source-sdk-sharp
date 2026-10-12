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
/// trigger_soundscape: a room-shaped soundscape.
///
/// <para>
/// The automatic <c>env_soundscape</c> pass claims a SPHERE with a clear line
/// of sight, which is the wrong shape for most rooms. This volume names an
/// <c>env_soundscape_triggerable</c> and hands it to whoever is standing
/// inside, which is the right shape for exactly the case a sphere gets wrong:
/// an L-shaped corridor, a gallery, anything where "near" and "in" disagree.
/// </para>
///
/// <para>
/// Volumes overlap on purpose, so leaving one puts a player back into whichever
/// they were already in rather than into silence. That stack is kept per player
/// in <see cref="Soundscapes"/>, where stock keeps it on the player entity.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.TriggerSoundscape, ScriptedEntityKind.Brush)]
public sealed class TriggerSoundscape : TriggerVolume
{
    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>What a map may set on this entity. See <see cref="TriggerSoundscapeVars"/>.</summary>
    [ScriptVars] public TriggerSoundscapeVars TriggerSoundscapeVars = new();

    private EnvSoundscape? _soundscape;
    private bool _resolved;

    /// <summary>
    /// Dead players inside the volume last time we looked.
    ///
    /// <para>
    /// They need tracking separately because they are not solid, so the engine
    /// reports no touches for them at all — see <see cref="OnThink"/>.
    /// </para>
    /// </summary>
    private readonly HashSet<EntityHandle> _spectators = new();

    /// <summary>The soundscape this volume hands out. For tests.</summary>
    public EnvSoundscape? Soundscape => _soundscape;

    /// <summary>The system this volume tells when a player walks in.</summary>
    private readonly ISoundscapes _soundscapes;

    public TriggerSoundscape(ISoundscapes soundscapes, IEnginePlayers players, IGameClock clock)
        : base(clock)
    {
        _soundscapes = soundscapes;
        Players = players;
    }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Deferred because it names another map entity, which may not have
        // spawned yet. Stock resolves it in Activate, which is the same moment.
        SetNextThink(0f);
    }

    /// <summary>
    /// A live player walked in. <see cref="TriggerVolume"/> has already decided
    /// they pass the filters.
    /// </summary>
    protected override void Fire(EntityHandle toucher)
    {
        if (_soundscape is not null && Entities.IsPlayer(toucher))
            _soundscapes.EnterTrigger(toucher, _soundscape);
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);

        if (_soundscape is not null && Entities.IsPlayer(toucher))
            _soundscapes.LeaveTrigger(toucher, _soundscape);
    }

    /// <summary>
    /// Catches the players the touch system does not report.
    ///
    /// <para>
    /// A dead player is not solid, so nothing ever touches this volume on their
    /// behalf — and somebody watching the map from inside a room should hear the
    /// room. Stock runs the same sweep at the same cadence and for the same
    /// reason; five ticks a second over the player slots is cheap next to the
    /// traces the automatic pass does.
    /// </para>
    /// </summary>
    public override void OnThink()
    {
        SetNextThink(0.2f);

        if (!_resolved)
        {
            _resolved = true;
            _soundscape = FindByName<EnvSoundscape>(TriggerSoundscapeVars.SoundscapeName);

            if (_soundscape is null)
            {
                Log.Warning($"[SourceSharp] trigger_soundscape '{Name}': "
                             + $"nothing named '{TriggerSoundscapeVars.SoundscapeName}'\n");
            }
        }

        if (_soundscape is null || Disabled)
            return;

        var (mins, maxs) = Entities.GetBounds(Handle);
        Vector origin = Origin;
        Vector worldMins = origin + mins;
        Vector worldMaxs = origin + maxs;

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            var player = Players.GetHandle(slot);

            if (!Entities.IsValid(player))
                continue;

            // Live players are the touch system's business. Doing both would
            // enter the volume twice and leave it once.
            if (Players.IsAlive(player))
            {
                _spectators.Remove(player);
                continue;
            }

            bool inside = Overlaps(player, worldMins, worldMaxs);
            bool wasInside = _spectators.Contains(player);

            if (inside && !wasInside)
            {
                _spectators.Add(player);
                _soundscapes.EnterTrigger(player, _soundscape);
            }
            else if (!inside && wasInside)
            {
                _spectators.Remove(player);
                _soundscapes.LeaveTrigger(player, _soundscape);
            }
        }
    }

    /// <summary>
    /// Whether a player's collision box overlaps this volume's.
    ///
    /// <para>
    /// A box test rather than a point test, which is stock's <c>Intersects</c>:
    /// a spectator drifting so that only their feet are in the room is in the
    /// room, and the point test would put the boundary somewhere the mapper did
    /// not draw it.
    /// </para>
    /// </summary>
    private bool Overlaps(EntityHandle player, Vector mins, Vector maxs)
    {
        var (playerMins, playerMaxs) = Entities.GetBounds(player);

        Vector origin = Entities.GetOrigin(player);
        Vector lo = origin + playerMins;
        Vector hi = origin + playerMaxs;

        return lo.X <= maxs.X && hi.X >= mins.X
            && lo.Y <= maxs.Y && hi.Y >= mins.Y
            && lo.Z <= maxs.Z && hi.Z >= mins.Z;
    }
}

/// <summary>
/// The map keys <see cref="TriggerSoundscape"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerSoundscapeVars
{
    /// <summary>The env_soundscape_triggerable to hand out, by targetname.</summary>
    [VarName("soundscape")]
    public string SoundscapeName { get; set; } = string.Empty;
}
