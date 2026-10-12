using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Removes itself the moment it spawns.
///
/// It exists so a mapper can point something at a place and have nothing be
/// there afterwards. Stock Source does exactly this, and a map relies on the
/// entity being GONE — one that lingered would be found by name searches that
/// expect to find the real thing.
/// </summary>
[ScriptedEntity(EntityNames.InfoNull)]
public sealed class InfoNull : ScriptedEntity
{
    public override void OnSpawn() => Remove();
}
