using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of player_manager, and it is deliberately almost empty.
///
/// <para>
/// IT IS NOT OPTIONAL, which is the thing worth writing down. The client
/// publishes a hierarchy class's table only for classnames its own registry
/// knows — <c>StandaloneNetStates.PublishAll(..., _standalones.IsRegistered,
/// _standalones.ClassNames)</c> in <c>ClientModule</c> — because "registering
/// one the server has not is the mismatch that makes a join hang with nothing
/// in any log". Without this class the server would send a type the client had
/// never published, the type id would resolve to no classname, the payload
/// would be refused on its schema hash, and every scoreboard row would read
/// zero with nothing anywhere saying why.
/// </para>
///
/// <para>
/// WHY IT DRAWS NOTHING AND DECODES NOTHING. The consumers of this entity's
/// state are C++ VGUI panels reached through <c>IGameResources</c> —
/// ClientScoreBoardDialog, SpectatorGUI, MapOverview, clientmode_shared,
/// achievementmgr, GameStats — and they ask per player index, per frame, from
/// inside a paint pass. So the reader is native and lives in
/// <c>sourcesharp/game/client/shell_playerresource.cpp</c>, which reads the
/// same blob this state is bound to, at offsets it ASKS C# for by name. There
/// is no managed work to do in between, and inventing some would put a second
/// copy of the state on the client.
/// </para>
///
/// <para>
/// The declaration is here anyway rather than left off. It is what publishes
/// the table, it is what a future managed reader would use, and it is what
/// makes the pairing visible to someone reading this side.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.PlayerManager)]
public sealed class ClientPlayerResource : ClientStandaloneEntity
{
    public ClientPlayerResource(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.PlayerManager;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public PlayerResourceNetState State = new();
}
