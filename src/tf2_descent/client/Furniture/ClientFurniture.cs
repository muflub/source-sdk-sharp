using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Furniture;

/// <summary>
/// The client's <c>tf2_descent_furniture</c> (plan_descent_logic WO3): an
/// ordinary networked entity every client sees, drawn with the model the
/// server sets. It is tracked by <see cref="FurnitureNear"/>, which the HUD
/// reads for a use prompt.
/// </summary>
[ClientStandaloneEntity(FurnitureNetState.ClassName)]
public sealed class ClientFurnitureEntity(IEngineEntityRender render, FurnitureNear near) : ClientStandaloneEntity(render)
{
    public override string ClassName => FurnitureNetState.ClassName;

    [NetState] public FurnitureNetState State = new();

    /// <summary>Whether the server has stopped sending it (its room slept, or it went off).</summary>
    public bool Dormant => Render.IsDormant(Handle);

    public Vector Origin => Entities.GetOrigin(Handle);

    public override void OnSpawn() => near.Track(this);

    public override void OnRemoved() => near.Untrack(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType) => near.Track(this);
}

/// <summary>
/// One piece of furniture near the local hero: its entity, kind, the rules'
/// variant, the name a hero reads (a localisation token), how it is
/// (<see cref="FurnitureFlags"/>: spent, opened, locked, tripped), what is
/// left of it (0 to 255), where it stands and how far it is, and whether
/// +USE does anything there (a shrine or a container).
/// </summary>
public sealed record FurnitureNearEntry(int Entity, FurnitureKindId Kind, string Variant, string NameToken, FurnitureFlags Flags,
                                        int Left, Vector Position, float Distance)
{
    public bool Usable => Kind is FurnitureKindId.Shrine or FurnitureKindId.Container;
}

/// <summary>The furniture near the local hero, for a use prompt on the HUD (WO3: a prompt is UI work; this is its state).</summary>
public interface IFurnitureNear
{
    /// <summary>Every furniture entity the client holds, not dormant, within <paramref name="reach"/> of the local player, nearest first.</summary>
    IReadOnlyList<FurnitureNearEntry> Near(float reach);

    /// <summary>The nearest one +USE answers within <paramref name="reach"/>; null for none.</summary>
    FurnitureNearEntry? NearestUsable(float reach);
}

/// <summary>
/// The furniture entities the client holds, joined to the local player's
/// place (<see cref="IEngineHud.LocalPlayerOrigin"/>). Forgotten when the
/// level ends: the entities go with it, and the service outlives a level.
/// </summary>
[SideService(Side.Client, As = [typeof(IFurnitureNear)])]
public sealed class FurnitureNear(IEngineHud hud) : IFurnitureNear, IClientLevelHook, IOrderedHook
{
    /// <summary>The use reach a prompt asks with: TF2's +USE radius, and a little.</summary>
    public const float UseReach = 96f;

    /// <summary>Beside the mod's other client level hooks (LevelDownloads 10, HudNotices 11, the skill bar 20, the loot 31).</summary>
    public int HookOrder => 33;

    private readonly List<ClientFurnitureEntity> _live = [];

    /// <summary>Furniture entities the client holds, dormant or not.</summary>
    public IReadOnlyList<ClientFurnitureEntity> Live => _live;

    public void Track(ClientFurnitureEntity e)
    {
        if (!_live.Contains(e))
            _live.Add(e);
    }

    public void Untrack(ClientFurnitureEntity e) => _live.Remove(e);

    public IReadOnlyList<FurnitureNearEntry> Near(float reach)
    {
        var near = new List<FurnitureNearEntry>();
        if (hud.LocalPlayerOrigin is not { } here)
            return near;
        foreach (var e in _live)
        {
            if (e.Dormant || FurnitureCatalog.Of(e.State.Entry) is not { } entry)
                continue;
            var at = e.Origin;
            float distance = (at - here).Length;
            if (distance <= reach)
                near.Add(new FurnitureNearEntry(e.Index, entry.Kind, entry.Variant, entry.NameToken, (FurnitureFlags)e.State.Flags, e.State.Left, at, distance));
        }

        near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return near;
    }

    public FurnitureNearEntry? NearestUsable(float reach) => Near(reach).FirstOrDefault(e => e.Usable);

    public void OnLevelShutdown() => _live.Clear();
}

/// <summary><c>tf2_descent_furniture_cl</c>: the furniture entities this client holds and what is near the local hero (dev).</summary>
[ConCommand("tf2_descent_furniture_cl", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_furniture_cl: the furniture entities this client holds, and what is within use reach of the local hero")]
public sealed class ClientFurnitureCommand(FurnitureNear near, ILog log)
{
    public void Execute()
    {
        var sb = new StringBuilder();
        var within = near.Near(FurnitureNear.UseReach);
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Furniture] client holds {near.Live.Count} furniture entities ({near.Live.Count(e => e.Dormant)} dormant), {within.Count} within {FurnitureNear.UseReach:0}\n");
        foreach (var e in near.Near(100000f))
            sb.Append(CultureInfo.InvariantCulture,
                $"[TF2Descent/Furniture]   #{e.Entity} {e.Kind} {e.Variant} {e.NameToken} {e.Flags} left {e.Left} at ({e.Position.X:0} {e.Position.Y:0} {e.Position.Z:0}) {e.Distance:0} away{(e.Usable ? " usable" : "")}\n");
        log.Msg(sb.ToString());
    }
}
