//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Buildings;
using TF2.Shared;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// Where buildings may not go: stock's <c>PointInNoBuild</c>
/// (func_no_build.cpp). FOR LANE 2.10: a building placed at
/// <c>origin</c> is refused while this answers true.
/// </summary>
public interface ITFNoBuildAreas
{
    /// <summary>
    /// An active func_nobuild holds <paramref name="origin"/>, refuses
    /// <paramref name="type"/>, and is for <paramref name="team"/> (or no team).
    /// </summary>
    bool PointInNoBuild(Vector origin, TFObjectType type, int team);
}

/// <summary>The directory's func_nobuild entities, as <see cref="ITFNoBuildAreas"/>.</summary>
[SideService(Side.Server, As = [typeof(ITFNoBuildAreas)])]
public sealed class TFNoBuildAreas(IEntityDirectory directory) : ITFNoBuildAreas
{
    public bool PointInNoBuild(Vector origin, TFObjectType type, int team)
    {
        foreach (FuncNoBuild area in directory.OfClass(FuncNoBuild.MapClass).OfType<FuncNoBuild>())
        {
            if (area.Active && area.PointIsWithin(origin) && area.PreventsBuildOf(type))
            {
                // See if this ent's on the team we're set to deny
                if (area.Team == 0 || area.Team == team)
                    return true;
            }
        }

        return false; // Building should be ok.
    }
}

/// <summary>
/// func_nobuild (stock's <c>CFuncNoBuild</c>, func_no_build.cpp): a volume no
/// building of its denied kinds may be put in, for its team or everyone; one
/// that carries <c>DestroyBuildings</c clears the buildings standing in it
/// when it turns on.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class FuncNoBuild(ITFBuildings buildings, IEngineGameEvents events, IEnginePlayers players) : ScriptedEntity
{
    public const string MapClass = "func_nobuild";

    /// <summary>The removal event the <c>DestroyBuildings</c> sweep fires.</summary>
    internal const string ObjectRemovedEvent = "object_removed";

    [ScriptVars] public FuncNoBuildVars Vars = new();

    [SaveField] private bool _disabled;

    /// <summary><c>GetActive</c>.</summary>
    public bool Active => !_disabled;

    /// <summary><c>GetTeamNumber</c>: the team it denies, 0 for everyone.</summary>
    public int Team => Vars.TeamNum;

    /// <summary>Not a wall and not drawn: the volume is only asked about.</summary>
    public override void OnSpawn()
    {
        _disabled = Vars.StartDisabled;
        SetSolidity(BrushSolidity.None);
        SetVisible(false);
    }

    [ScriptedEvent] private void OnSetActive() => SetActive(true);

    [ScriptedEvent] private void OnSetInactive() => SetActive(false);

    [ScriptedEvent] private void OnToggleActive() => SetActive(_disabled);

    // CBaseTrigger's own switch, the same flag.
    [ScriptedEvent] private void OnEnable() => SetActive(true);

    [ScriptedEvent] private void OnDisable() => SetActive(false);

    [ScriptedEvent] private void OnToggle() => SetActive(_disabled);

    /// <summary>
    /// <c>CFuncNoBuild::SetActive</c> (func_no_build.cpp:148-181): the flag,
    /// and then — on activation, when the map asked for it — every standing
    /// building of the denied team whose build origin or centre of mass is
    /// inside the brush is removed with an <c>object_removed</c> and
    /// detonated. Stock's map-placed objects had their builder cleared
    /// (<c>InitializeMapPlacedObject</c>, tf_obj.cpp:3585), so the
    /// builder-less are the map's and stay.
    /// </summary>
    public void SetActive(bool active)
    {
        _disabled = !active;

        if (!active || !Vars.DestroyBuildingsOnActive)
            return;

        int denyTeam = Team;

        // destroy any objects touching when activated
        foreach (TFBaseObject obj in buildings.OwnedBy().ToList())
        {
            if (denyTeam != 0 && denyTeam != obj.Team)
                continue;

            if (obj.Builder == EntityHandle.None)
                continue;

            if (!PointIsWithin(Entities.GetOrigin(obj.Handle))
                && !PointIsWithin(Entities.GetWorldSpaceCenter(obj.Handle)))
                continue;

            // this is separate from the object_destroyed event, which does
            // not get sent when we remove the objects from the world
            IntPtr removed = events.EventCreate(ObjectRemovedEvent);
            if (removed != IntPtr.Zero)
            {
                // user ID of the object owner
                events.EventSetInt(removed, "userid",
                                   obj.Builder != EntityHandle.None && Entities.IsPlayer(obj.Builder)
                                       ? players.GetUserId(obj.Builder)
                                       : -1);
                events.EventSetInt(removed, "objecttype", (int)obj.Type); // type of object removed
                events.EventSetInt(removed, "index", obj.Handle.EntryIndex); // index of the object removed
                events.EventFire(removed);
            }

            obj.DetonateObject();
        }
    }

    /// <summary><c>PreventsBuildOf</c>: every kind but those the map allowed.</summary>
    public bool PreventsBuildOf(TFObjectType type)
    {
        if (type == TFObjectType.Sentrygun && Vars.AllowSentry)
            return false;

        if (type == TFObjectType.Dispenser && Vars.AllowDispenser)
            return false;

        if (type == TFObjectType.Teleporter && Vars.AllowTeleporters)
            return false;

        return true;
    }

    /// <summary><c>PointIsWithin</c>, over the brush's box (docs/divergences.md, as func_respawnroom's).</summary>
    public bool PointIsWithin(Vector point) => BrushBox.Holds(Entities, Handle, point);
}

/// <summary>The map keys <see cref="FuncNoBuild"/> declares (func_no_build.cpp's datadesc, CBaseTrigger's StartDisabled and CBaseEntity's TeamNum).</summary>
[ScriptVars]
public partial class FuncNoBuildVars
{
    public bool StartDisabled { get; set; }

    public bool AllowSentry { get; set; }

    public bool AllowDispenser { get; set; }

    public bool AllowTeleporters { get; set; }

    [VarName("DestroyBuildings")] public bool DestroyBuildingsOnActive { get; set; }

    public int TeamNum { get; set; }
}
