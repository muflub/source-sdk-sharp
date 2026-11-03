using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Combat;

/// <summary>A TF headshot is a crit where the weapon says so (TFDamageLayer), never a multiplier.</summary>
[SideService(Side.Server)]
public sealed class TFHitGroupDamageOwner : IHitGroupDamageOwner
{
}
