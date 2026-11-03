using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;

namespace TF2.Client;

/// <summary>
/// Loads the client's <see cref="TFClassTable"/> as the first level comes
/// up; a read before then loads it on the spot.
/// </summary>
[SideService(Side.Client)]
public sealed class TFClassTableLoader(TFClassTable table) : IClientLevelHook, IOrderedHook
{
    /// <inheritdoc cref="TFClassTable.LoadOrder"/>
    public int HookOrder => TFClassTable.LoadOrder;

    void IClientLevelHook.OnLevelInit(string mapName) => table.Load();
}
