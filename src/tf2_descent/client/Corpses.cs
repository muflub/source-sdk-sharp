using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client;

/// <summary>
/// The client's half of <c>tf2_descent_corpse</c> (plan_descent_logic WO5):
/// every client is sent every hero's corpse, drawn with the model the server
/// set (a TF2 gravestone), and read through <see cref="ICorpseState"/>.
/// </summary>
[ClientStandaloneEntity(CorpseNetState.ClassName)]
public sealed class ClientCorpseEntity(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    public override string ClassName => CorpseNetState.ClassName;

    [NetState] public CorpseNetState State = new();

    public Vector Origin => Entities.GetOrigin(Handle);
}

/// <summary>A hero's corpse as a client knows it: whose, where, how many items it holds (-1 unknown), hardcore.</summary>
public sealed record CorpseView(int OwnerIndex, Vector At, int Items, bool Hardcore);

/// <summary>
/// Where the heroes' corpses lie, for the automap's corpse marker
/// (<c>DescentMapMarker.Corpse</c>) and the HUD: the local hero's own, and
/// every one. A corpse is the level's entity, so a new level starts with none.
/// </summary>
public interface ICorpseState
{
    /// <summary>The local hero's corpse, or null.</summary>
    CorpseView? Local();

    /// <summary>Every corpse the client holds, by owner.</summary>
    IReadOnlyList<CorpseView> All();
}

/// <summary>
/// <see cref="ICorpseState"/> over the live <c>tf2_descent_corpse</c> entities,
/// read at each ask and never cached: a level change takes the entities, and
/// with them every answer (nothing outlives the level, client-state-outlives-level).
/// </summary>
[SideService(Side.Client, As = [typeof(ICorpseState)])]
public sealed class CorpseStates(IClientStandalones standalones, IEngineHud hud) : ICorpseState
{
    public CorpseView? Local()
    {
        int local = hud.LocalPlayerIndex;
        return local <= 0 ? null : All().FirstOrDefault(c => c.OwnerIndex == local);
    }

    public IReadOnlyList<CorpseView> All() =>
        standalones.OfClass(CorpseNetState.ClassName)
            .OfType<ClientCorpseEntity>()
            .Where(c => c.State.IsLive && c.State.OwnerIndex > 0)
            .Select(c => new CorpseView(c.State.OwnerIndex, c.Origin, c.State.Items, c.State.Hardcore))
            .OrderBy(c => c.OwnerIndex)
            .ToList();
}
