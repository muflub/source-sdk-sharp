using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Level;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Portal;

/// <summary>
/// Where the town's exit pads stand. The town carries no marker for them yet (a named
/// <see cref="MarkerName"/> <c>info_target</c> is asked of the map session): without one they
/// stand in a row beside the heroes' arrival spawns, on the street side, facing them.
/// </summary>
public static class TownPadLayout
{
    /// <summary>The town marker an exit pad row starts at, when the map has one.</summary>
    public const string MarkerName = "tf2_descent_portal";

    /// <summary>How far from the arrival spawns' middle, against their facing, the row stands.</summary>
    public const float FromArrivals = 192f;

    /// <summary>The gap between two pads' middles: two pads' widths.</summary>
    public const float Spacing = 96f;

    /// <summary>
    /// The <paramref name="index"/>th pad (from 0) of a row whose first pad stands at
    /// <paramref name="first"/>, the row running across <paramref name="yaw"/>: 0, then one
    /// side, then the other, a <see cref="Spacing"/> further each time.
    /// </summary>
    public static Vector Spot(Vector first, float yaw, int index)
    {
        float r = yaw * MathF.PI / 180f;
        var across = new Vector(-MathF.Sin(r), MathF.Cos(r), 0f);
        int step = (index + 1) / 2 * (index % 2 == 1 ? 1 : -1);
        return first + across * (step * Spacing);
    }

    /// <summary>The row's first pad beside arrival spawns whose middle is <paramref name="arrivals"/> facing <paramref name="facing"/>: ahead of them, facing back at them.</summary>
    public static (Vector First, float Yaw) BesideArrivals(Vector arrivals, float facing)
    {
        float r = facing * MathF.PI / 180f;
        var forward = new Vector(MathF.Cos(r), MathF.Sin(r), 0f);
        return (arrivals + forward * FromArrivals, (facing + 180f) % 360f);
    }
}

/// <summary>The engine's <see cref="IPortalWorld"/>: the heroes and their keys, traces, the pad entities, the effects.</summary>
[SideService(Side.Server, As = [typeof(IPortalWorld)])]
public sealed class EnginePortalWorld(HostService host, ILevelData level, HeroKeys keys, PlayerService players, Heroes heroes,
                                      IEngineEntities entities, IEnginePlayers enginePlayers, IEnginePlayerMovement movement,
                                      IEngineWorldQuery world,
                                      IEntityDirectory directory, IEnginePrecache precache, IEngineSound sound, ITempEnts temps,
                                      ClientMessages messages) : IPortalWorld, IPortalGround
{
    /// <summary>TF2's teleporter sounds (tf_obj_teleporter.cpp's precache list).</summary>
    public const string ReadySound = "Building_Teleporter.Ready", SendSound = "Building_Teleporter.Send", ReceiveSound = "Building_Teleporter.Receive";

    /// <summary><c>PARTICLE_DISPATCH_FROM_ENTITY</c>: the record names an entity.</summary>
    private const int FromEntity = 1 << 0;

    /// <summary><c>PATTACH_ABSORIGIN</c> and <c>PATTACH_ABSORIGIN_FOLLOW</c>.</summary>
    private const int AttachOrigin = 0, AttachFollow = 1;

    private readonly Dictionary<int, EntityHandle> _pads = [];

    public PortalHere Here(string map)
    {
        bool isLevel = LevelNames.IsLevel(map);
        bool hosted = host.Mode == HostingMode.Hosted;
        int depth = hosted && host.Boot is { } boot ? boot.Depth : LevelNames.Depth(map);
        ulong? seed = level.Current?.Seed ?? LevelNames.Seed(map);
        return new PortalHere(map, isLevel, depth, seed, hosted ? host.Boot?.LevelId ?? "" : "", !hosted || host.Live);
    }

    public string? HeroOf(int player) => keys.Of(player);

    public string? CharacterOf(int player) =>
        host.Mode == HostingMode.Hosted && players.ByEntity(player) is { CharacterId.Length: > 0 } entry ? entry.CharacterId : null;

    public IReadOnlyList<int> Heroes() => [.. heroes.All.Where(h => h.PlayerIndex > 0).Select(h => h.PlayerIndex).Order()];

    private EntityHandle Player(int player) => heroes.Of(player)?.Player ?? EntityHandle.None;

    public bool IsAlive(int player) => Player(player) is var p && p != EntityHandle.None && enginePlayers.IsAlive(p);

    public Vector? Feet(int player) => Player(player) is var p && p != EntityHandle.None && entities.IsValid(p) ? entities.GetOrigin(p) : null;

    public int Team(int player) => Player(player) is var p && p != EntityHandle.None ? enginePlayers.GetTeam(p) : 0;

    public PortalPlace Place(int player)
    {
        EntityHandle p = Player(player);
        if (p == EntityHandle.None)
            return PortalPlace.Refused(PortalPlacement.NoRoom);
        return PortalPlacement.Place(entities.GetOrigin(p), entities.GetEyeAngles(p).Y, this);
    }

    public TraceResult Line(Vector from, Vector to) => world.TraceLine(from, to, TraceMask.Brushes);

    public bool BoxClear(Vector at, Vector mins, Vector maxs) => world.TraceHull(at, at, mins, maxs, TraceMask.Shot) is { Blocked: false, StartSolid: false };

    public (Vector At, float Yaw)? TownSpot(int index)
    {
        (Vector First, float Yaw)? row = Marker() ?? BesideArrivals();
        if (row is not { } r)
            return null;
        Vector spot = TownPadLayout.Spot(r.First, r.Yaw, index);
        TraceResult down = world.TraceLine(spot + new Vector(0f, 0f, 64f), spot - new Vector(0f, 0f, 512f), TraceMask.Brushes);
        return (down.Hit ? down.End : spot, r.Yaw);
    }

    private (Vector, float)? Marker()
    {
        var marker = entities.FindAllByClassname("info_target").FirstOrDefault(e => entities.GetName(e) == TownPadLayout.MarkerName);
        return marker == EntityHandle.None ? null : (entities.GetOrigin(marker), entities.GetAngles(marker).Y);
    }

    private (Vector, float)? BesideArrivals()
    {
        var spots = entities.FindAllByClassname(EntityNames.InfoPlayerDeathmatch)
            .Where(e => entities.GetName(e) == ArrivalSpawns.ArrivalName).ToList();
        if (spots.Count == 0)
            return null;
        var middle = new Vector(spots.Average(s => entities.GetOrigin(s).X), spots.Average(s => entities.GetOrigin(s).Y),
                                spots.Average(s => entities.GetOrigin(s).Z));
        return TownPadLayout.BesideArrivals(middle, entities.GetAngles(spots[0]).Y);
    }

    public int MakePad(PortalRecord portal, PortalEnd end, Vector at, float yaw)
    {
        // A pad of an earlier map went with it: forget its handle.
        foreach (int gone in _pads.Where(kv => !entities.IsValid(kv.Value)).Select(kv => kv.Key).ToList())
            _pads.Remove(gone);

        EntityHandle h = entities.Create(PortalNetState.ClassName);
        if (h == EntityHandle.None)
            return 0;
        entities.Spawn(h);
        if (directory.Get(h) is not PortalPad pad)
        {
            entities.Remove(h);
            return 0;
        }
        pad.Stand(portal.Id, end, portal.Team, at, yaw);
        int index = entities.GetIndex(h);
        _pads[index] = h;
        Particle(portal.Team == PortalPad.BlueTeam ? "teleporter_blue_charged" : "teleporter_red_charged", at, h, AttachFollow);
        return index;
    }

    public void RemovePad(int pad)
    {
        if (_pads.Remove(pad, out EntityHandle h) && entities.IsValid(h))
            entities.Remove(h);
    }

    public void SetOn(int player, Vector at, float yaw)
    {
        EntityHandle p = Player(player);
        if (p == EntityHandle.None)
            return;
        entities.Teleport(p, at, new Vector(0f, yaw, 0f), zeroVelocity: true);
        movement.SnapEyeAngles(p, new Vector(0f, yaw, 0f));
    }

    public void Show(PortalShow show, int pad, int player)
    {
        EntityHandle p = Player(player);
        EntityHandle h = _pads.TryGetValue(pad, out var made) && entities.IsValid(made) ? made : EntityHandle.None;
        Vector at = h != EntityHandle.None ? entities.GetOrigin(h) : p != EntityHandle.None ? entities.GetOrigin(p) : default;
        string team = p != EntityHandle.None && enginePlayers.GetTeam(p) == PortalPad.BlueTeam ? "blue" : "red";
        EntityHandle voice = h != EntityHandle.None ? h : p;
        switch (show)
        {
            case PortalShow.Built:
                if (voice != EntityHandle.None)
                    sound.EntityEmitSound(voice, ReadySound);
                break;
            case PortalShow.Departed:
                if (voice != EntityHandle.None)
                    sound.EntityEmitSound(voice, SendSound);
                Particle($"teleported_{team}", at, EntityHandle.None, AttachOrigin);
                if (p != EntityHandle.None)
                    Particle($"player_sparkles_{team}", at, p, AttachOrigin);
                break;
            case PortalShow.Arrived:
                if (voice != EntityHandle.None)
                    sound.EntityEmitSound(voice, ReceiveSound);
                Particle($"teleportedin_{team}", p != EntityHandle.None ? entities.GetOrigin(p) : at, EntityHandle.None, AttachOrigin);
                break;
        }
    }

    /// <summary>A TF2 particle system by name: at a spot, or riding an entity (the way TFTeleporter's show dispatches).</summary>
    private void Particle(string name, Vector at, EntityHandle on, int attach)
    {
        TeEffectData record = on == EntityHandle.None
            ? new() { Scale = 1f, HitBox = precache.ParticleSystemIndex(name), Origin = at }
            : new() { Scale = 1f, HitBox = precache.ParticleSystemIndex(name), Flags = FromEntity, EntIndex = entities.GetIndex(on), DamageType = attach, Origin = at };
        temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in record);
    }

    public void Notice(int player, string token, string arg = "") => messages.Notice(player, token, arg);
}
