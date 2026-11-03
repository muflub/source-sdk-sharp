using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Player;

/// <summary>A TF player wears their class's model (<see cref="TFPlayerSpawns"/>), never cl_playermodel.</summary>
[SideService(Side.Server)]
public sealed class TFPlayerModelOwner : IPlayerModelOwner
{
}
