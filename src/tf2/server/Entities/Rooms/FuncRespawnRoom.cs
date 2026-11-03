//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// Where the respawn rooms are, for the rules that ask: stock's
/// <c>PointInRespawnRoom</c> and the per-player touch count
/// (func_respawnroom.cpp). FOR LANE 2.3: a class change is instant while
/// <see cref="InRespawnRoom"/> answers true with <c>sameTeamOnly</c>
/// (<c>CTFPlayer::HandleCommand_JoinClass</c>, tf_player.cpp:6981).
/// </summary>
public interface ITFRespawnRooms
{
    /// <summary>
    /// <c>PointInRespawnRoom( player, player-&gt;WorldSpaceCenter(), sameTeamOnly )</c>:
    /// an active room holds the player's centre and is the player's team's
    /// (or no team's), or the player is touching an active room (of the
    /// player's team when <paramref name="sameTeamOnly"/>).
    /// </summary>
    bool InRespawnRoom(EntityHandle player, bool sameTeamOnly);

    /// <summary><c>PointInRespawnRoom( NULL, point )</c>: an active room holds the point.</summary>
    bool PointInRespawnRoom(Vector point);

    /// <summary>
    /// <c>PointInRespawnRoom( pEntity, vecOrigin, bTouching_SameTeamOnly )</c>
    /// (func_respawnroom.cpp:313-340): an active room holds <paramref name="origin"/>
    /// and is <paramref name="target"/>'s team's (or no team's) — the containment
    /// is gated by the entity, not by where the point stands — or
    /// <paramref name="target"/> touches an active room, of its team only when
    /// <paramref name="touchingSameTeamOnly"/>. <see cref="EntityHandle.None"/>
    /// is stock's NULL: the containment carries no team gate (:325) and the
    /// touch branch needs an entity (:330).
    ///
    /// <para>
    /// Stock's callers: the medigun's charge bonus asks with the weapon's centre
    /// for patient and medic alike (<c>tf_weapon_medigun.cpp:1332-1334</c> —
    /// wired by <c>TFMedigunWeapon.PrimaryAttack</c>; the medic's centre stands
    /// for the weapon's, <c>docs/divergences.md</c>) and the spy's disguise
    /// cloak drain asks for the spy (<c>tf_player_shared.cpp:8228</c> —
    /// <see cref="InRespawnRoom"/> with the player's own centre is that call).
    /// The buildings' placement arms take the NULL form.
    /// </para>
    /// </summary>
    bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly);

    /// <summary><c>CTFPlayerShared::GetRespawnTouchCount</c>: how many rooms the player is touching.</summary>
    int TouchCount(EntityHandle player);

    /// <summary>
    /// <c>PointsCrossRespawnRoomVisualizer(vecStart, vecEnd, nTeamToIgnore)</c>
    /// (func_respawnroom.cpp:346-368): does the segment meet a visualizer,
    /// skipping those of <paramref name="teamToIgnore"/> unless it is
    /// TEAM_UNASSIGNED (0), where nothing is skipped. Stock clips
    /// <c>MASK_ALL</c> against each entity and a fraction under 1 is a
    /// meeting — so a segment that starts inside one meets it.
    ///
    /// <para>
    /// Stock's callers: the fan pusher's blocker test
    /// (<c>tf_pushentity.cpp:432</c>, no <c>tf_pushentity</c> in this build),
    /// the builder's placement (<c>tf_obj.cpp:714</c> — wired by
    /// <c>TFBuilderPlacement.IsValidPlacement</c>), the taunt separation
    /// position (<c>tf_player.cpp:17196</c>) and the high-five partner
    /// (<c>tf_player.cpp:21574</c>) — taunts and high-fives are not in this
    /// build.
    /// </para>
    /// </summary>
    bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0);
}

/// <summary>The rooms the directory holds, as <see cref="ITFRespawnRooms"/>.</summary>
[SideService(Side.Server, As = [typeof(ITFRespawnRooms)])]
public sealed class TFRespawnRooms(IEntityDirectory directory, IEngineEntities entities, ITFMapPlayers players)
    : ITFRespawnRooms
{
    private IEnumerable<FuncRespawnRoom> Rooms => directory.OfClass(FuncRespawnRoom.MapClass).OfType<FuncRespawnRoom>();

    private IEnumerable<FuncRespawnRoomVisualizer> Visualizers => directory.OfClass(FuncRespawnRoomVisualizer.MapClass)
                                                                           .OfType<FuncRespawnRoomVisualizer>();

    public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly)
        => PointInRespawnRoom(player, entities.GetWorldSpaceCenter(player), sameTeamOnly);

    public bool PointInRespawnRoom(Vector point) => Rooms.Any(room => room.Active && room.PointIsWithin(point));

    // Stock's touch branch needs an entity: the point-only caller keeps the
    // overload above, which is its PointInRespawnRoom(NULL, point).
    public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly)
    {
        bool hasTarget = target != EntityHandle.None;
        int team = hasTarget ? players.Team(target) : 0;
        foreach (FuncRespawnRoom room in Rooms)
        {
            if (!room.Active)
                continue;

            if (room.PointIsWithin(origin))
            {
                // Stock's NULL pTarget carries no team gate on the containment (:325).
                if (!hasTarget || room.Team == 0 || room.Team == team)
                    return true;
            }
            else if (hasTarget && room.IsTouching(target))
            {
                if (!touchingSameTeamOnly || room.Team == 0 || room.Team == team)
                    return true;
            }
        }

        return false;
    }

    public int TouchCount(EntityHandle player) => Rooms.Count(room => room.IsTouching(player));

    public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0)
    {
        foreach (FuncRespawnRoomVisualizer visualizer in Visualizers)
        {
            // Stock: the skip fires only when the ignore is a real team.
            if (teamToIgnore != 0 && visualizer.Team == teamToIgnore)
                continue;

            if (BrushBox.Crosses(entities, visualizer.Handle, start, end))
                return true;
        }

        return false;
    }
}

/// <summary>
/// func_respawnroom (stock's <c>CFuncRespawnRoom</c>, func_respawnroom.cpp): the
/// volume a team spawns in. Its team (the map's, or the first enabled team
/// spawn inside it at RoundActivate) makes the class change instant there
/// (<see cref="ITFRespawnRooms"/>), and it hands its team and its on/off
/// state to the visualizers that name it.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncRespawnRoom(IGameClock clock, IEntityDirectory directory, ICtfRules rules, ICtfPlayers players)
    : TriggerVolume(clock)
{
    public const string MapClass = "func_respawnroom";

    [ScriptVars] public FuncRespawnRoomVars RoomVars = new();

    [SaveField] private bool _active = true;
    [SaveField] private int _team;
    [SaveField] private int _originalTeam;

    private readonly List<FuncRespawnRoomVisualizer> _visualizers = new();

    /// <summary><c>GetActive</c>.</summary>
    public bool Active => _active;

    /// <summary><c>GetTeamNumber</c>: the map's team, or the one RoundActivate found.</summary>
    public int Team => _team;

    /// <summary>The visualizers that named this room at RoundActivate.</summary>
    public IReadOnlyList<FuncRespawnRoomVisualizer> Visualizers => _visualizers;

    /// <summary>Clients only (<c>AddSpawnFlags( SF_TRIGGER_ALLOW_CLIENTS )</c>).</summary>
    protected override TriggerSpawnflags KindFlags => TriggerSpawnflags.Clients;

    protected override bool Passes(EntityHandle toucher) => PassesFilter(toucher);

    public override void OnSpawn()
    {
        base.OnSpawn();
        _team = RoomVars.TeamNum;
        _active = true;
    }

    /// <summary><c>Activate</c>: the map's team kept for RoundActivate, and on.</summary>
    public override void OnServerActivate()
    {
        _originalTeam = _team;
        SetActive(true);
    }

    /// <summary>
    /// <c>IsTouching</c>: the player is inside. Stock's
    /// <c>IncrementRespawnTouchCount</c> counts every TF player at
    /// <c>StartTouch</c>; here the count is the rooms a player is inside, which
    /// is the same set for a room with no filter.
    /// </summary>
    public bool IsTouching(EntityHandle player) => Inside.Contains(player);

    /// <summary>
    /// <c>PointIsWithin</c>, over the brush's box rather than its planes
    /// (docs/divergences.md): the entity's collision bounds at its origin.
    /// </summary>
    public bool PointIsWithin(Vector point) => BrushBox.Holds(Entities, Handle, point);

    /// <summary>Stock's <c>RespawnRoomTouch</c> is <c>SetTouch</c> (:94): every
    /// touch, not only the entry — a player who comes to carry the flag while
    /// already inside is dropped on the next touch, without leaving.</summary>
    public override bool TouchEveryTick => true;

    /// <summary>The entry is the volume's; the work is every touch.</summary>
    protected override void Fire(EntityHandle toucher) { }

    /// <summary>
    /// <c>RespawnRoomTouch</c> (func_respawnroom.cpp:111-151), every touch of
    /// the overlap: a player of the room's team here lets go of a carried flag —
    /// <c>DropFlag()</c>, the thrown drop, as <c>dropitem</c> gives it. The PD
    /// item, the MvM carried sentry and the rune wait on their modes
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        base.OnTouch(toucher);
        if (Disabled || !Passes(toucher))
            return;

        if (!players.IsPlayer(toucher) || Team != (int)players.Team(toucher))
            return;

        // Players carrying the flag drop it if they try to run into a respawn room.
        rules.CarriedBy(toucher)?.DropFlag(toucher);
    }

    [ScriptedEvent] private void OnSetActive() => SetActive(true);

    [ScriptedEvent] private void OnSetInactive() => SetActive(false);

    [ScriptedEvent] private void OnToggleActive() => SetActive(!_active);

    /// <summary>
    /// <c>InputRoundActivate</c>: a room the map gave no team takes the team of
    /// the first enabled team spawn inside it.
    /// </summary>
    [ScriptedEvent]
    private void OnRoundActivate()
    {
        if (_originalTeam != 0)
            return;

        ChangeTeam(0);
        foreach (InfoPlayerTeamSpawn spawn in directory.OfClass(InfoPlayerTeamSpawn.MapClass).OfType<InfoPlayerTeamSpawn>())
        {
            if (!PointIsWithin(Entities.GetOrigin(spawn.Handle)))
                continue;

            if (!spawn.Disabled && spawn.Team > (int)TFTeam.Spectator)
            {
                ChangeTeam(spawn.Team);
                break;
            }
        }

        if (_team == 0)
            Log.Msg($"Unassigned {MapClass}({Name}) failed to find an {InfoPlayerTeamSpawn.MapClass} within it to use.\n");
    }

    /// <summary><c>ChangeTeam</c>: the room's team, and its visualizers'.</summary>
    public void ChangeTeam(int team)
    {
        _team = team;
        foreach (FuncRespawnRoomVisualizer visualizer in _visualizers)
            visualizer.ChangeTeam(team);
    }

    /// <summary><c>SetActive</c>: on or off, and the visualizers with it.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        foreach (FuncRespawnRoomVisualizer visualizer in _visualizers)
            visualizer.SetActive(active);
    }

    /// <summary><c>AddVisualizer</c>.</summary>
    public void AddVisualizer(FuncRespawnRoomVisualizer visualizer)
    {
        if (!_visualizers.Contains(visualizer))
            _visualizers.Add(visualizer);
    }
}

/// <summary>The map keys <see cref="FuncRespawnRoom"/> declares (CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncRespawnRoomVars
{
    /// <summary>CBaseEntity::KeyValue's TeamNum: the team this room is for, 0 to derive it.</summary>
    public int TeamNum { get; set; }
}

/// <summary>
/// <c>CBaseEntity::PointIsWithin</c> as the seams can answer it: inside the
/// entity's collision box at its origin, not inside the brush's planes
/// (docs/divergences.md).
/// </summary>
internal static class BrushBox
{
    public static bool Holds(IEngineEntities entities, EntityHandle brush, Vector point)
    {
        Vector origin = entities.GetOrigin(brush);
        (Vector mins, Vector maxs) = entities.GetBounds(brush);
        Vector lo = origin + mins;
        Vector hi = origin + maxs;
        return point.X >= lo.X && point.X <= hi.X
            && point.Y >= lo.Y && point.Y <= hi.Y
            && point.Z >= lo.Z && point.Z <= hi.Z;
    }

    /// <summary>
    /// <c>ClipRayToEntity</c>'s fraction &lt; 1 for a box brush
    /// (func_respawnroom.cpp:359-364): the segment from start to end meets the
    /// entity's box — the start inside it meets it at fraction 0 (stock's
    /// start-solid), meeting it only at the far end is fraction 1 and not a
    /// meeting. Slabs, per axis; a flat axis constrains nothing while its
    /// start stands between the planes.
    /// </summary>
    public static bool Crosses(IEngineEntities entities, EntityHandle brush, Vector start, Vector end)
    {
        Vector origin = entities.GetOrigin(brush);
        (Vector mins, Vector maxs) = entities.GetBounds(brush);
        Vector lo = origin + mins;
        Vector hi = origin + maxs;

        float tEnter = 0f, tExit = 1f;
        return Meets(start.X, end.X, lo.X, hi.X, ref tEnter, ref tExit)
            && Meets(start.Y, end.Y, lo.Y, hi.Y, ref tEnter, ref tExit)
            && Meets(start.Z, end.Z, lo.Z, hi.Z, ref tEnter, ref tExit)
            && tExit >= 0f && tEnter < 1f;

        static bool Meets(float from, float to, float lo, float hi, ref float tEnter, ref float tExit)
        {
            float delta = to - from;
            if (delta == 0f)
                return from >= lo && from <= hi;

            float t0 = (lo - from) / delta;
            float t1 = (hi - from) / delta;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            if (t0 > tEnter)
                tEnter = t0;
            if (t1 < tExit)
                tExit = t1;
            return tEnter <= tExit;
        }
    }
}
