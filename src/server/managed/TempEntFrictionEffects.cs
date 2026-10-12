using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>The server's g_pEffects: the engine's dust, and a TE_MetalSparks to the point's PVS.</summary>
public sealed class TempEntFrictionEffects(IEngineEffects effects, ITempEnts temps) : IFrictionEffects
{
    public void Dust(Vector point, Vector direction) => effects.DustEffect(point, direction, 1f, 16f);
    public void MetalSparks(Vector point, Vector direction) => temps.MetalSparks(TeFilter.Pvs(point), 0f, point, direction);
}
