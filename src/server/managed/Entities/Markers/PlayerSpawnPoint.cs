using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Entities that are nothing but a position and a name.
///
/// There is a real question about whether these are worth owning at all — this
/// project's own migration plan rules out porting entities that only exist to
/// be looked at, on the grounds that it replaces working code with equivalent
/// code. These are different, and the difference is that game logic REFERS to
/// them: a spawn point is chosen, a teleport destination is arrived at, an
/// anchor is attached to.
///
/// The spawn points are the ones that pay for themselves. Pickups already hunt
/// for info_player_deathmatch by classname through the engine's entity list;
/// owning the class means the managed side has them without asking.
/// </summary>
[ScriptedEntity(EntityNames.InfoPlayerDeathmatch)]
[ScriptedEntity(EntityNames.InfoPlayerStart)]
public sealed class PlayerSpawnPoint : ScriptedEntity
{
    /// <summary>
    /// Set by the map on a spawn point that starts unusable — some maps disable
    /// half of them for a mode that is not this one.
    /// </summary>
    private const int DisabledFlag = 1;

    public bool Enabled => (SpawnFlags & DisabledFlag) == 0;
}
