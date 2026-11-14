using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Portal;

/// <summary>
/// <c>tf2_descent_teleporter</c>: one end of a town portal (plan_descent_logic WO4, ruling L10).
/// TF2's Engineer teleporter in look only (<c>TF2.Server.Buildings.TFTeleporter</c> is the
/// reference, not a base: no builder, metal, sapping or upgrades): the built model, its arms
/// spinning (the client plays <see cref="PortalNetState.Sequence"/>), the owner's team skin, and
/// TF2's box, solid, so a hero stands on its deck. It takes no damage and answers no use: a hero
/// uses it by standing on it (<see cref="TownPortals"/>), as TF2's.
/// </summary>
[ScriptedEntity(PortalNetState.ClassName, ScriptedEntityKind.Item)]
[Precache(PrecacheKind.Model, PortalLooks.Model)]
[Precache(PrecacheKind.ParticleSystem, "teleported_red")]
[Precache(PrecacheKind.ParticleSystem, "teleported_blue")]
[Precache(PrecacheKind.ParticleSystem, "player_sparkles_red")]
[Precache(PrecacheKind.ParticleSystem, "player_sparkles_blue")]
[Precache(PrecacheKind.ParticleSystem, "teleportedin_red")]
[Precache(PrecacheKind.ParticleSystem, "teleportedin_blue")]
[Precache(PrecacheKind.ParticleSystem, "teleporter_red_charged")]
[Precache(PrecacheKind.ParticleSystem, "teleporter_blue_charged")]
[Precache(PrecacheKind.Sound, EnginePortalWorld.ReadySound)]
[Precache(PrecacheKind.Sound, EnginePortalWorld.SendSound)]
[Precache(PrecacheKind.Sound, EnginePortalWorld.ReceiveSound)]
public sealed class PortalPad(IEngineEntityRender render, IServerSkins skins) : ScriptedEntity
{
    /// <summary>TF2's team skins of the teleporter: red 0, blue 1.</summary>
    public static int SkinOf(int team) => team == BlueTeam ? 1 : 0;

    /// <summary>TF2's blue team (<c>TF_TEAM_BLUE</c>).</summary>
    public const int BlueTeam = 3;

    [NetState] public PortalNetState State = new();

    public override bool SpawnsPhysics => false;

    public override bool TakesDamage => false;

    public override bool TouchEveryTick => false;

    public override UseCaps ObjectCaps => UseCaps.None;

    public override void OnSpawn()
    {
        SetModel(PortalLooks.Model);
        Entities.SetSolidity(Handle, SolidType.BoundingBox, SolidFlags.None);
        Entities.SetBounds(Handle, PortalPlacement.PadMins, PortalPlacement.PadMaxs);
    }

    /// <summary>Stands it at <paramref name="at"/> facing <paramref name="yaw"/>: portal <paramref name="portal"/>'s <paramref name="end"/>, in the team's colour, spinning.</summary>
    internal void Stand(int portal, PortalEnd end, int team, Vector at, float yaw)
    {
        Entities.SetOrigin(Handle, at);
        Entities.SetAngles(Handle, new Vector(0f, yaw, 0f));
        skins.SetSkin(Handle, SkinOf(team));
        int spin = render.LookupSequenceByActivity(Handle, PortalLooks.SpinActivity);
        if (spin >= 0)
            render.PlaySequence(Handle, spin, 1f, true);
        State.Portal = (ushort)Math.Clamp(portal, 0, ushort.MaxValue);
        State.End = (byte)end;
        State.Sequence = (short)spin;
    }
}
