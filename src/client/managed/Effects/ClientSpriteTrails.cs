using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Effects;

/// <summary>
/// Every env_spritetrail's ribbon, by its entity: the entity hands its
/// parameters over when they change (<see cref="Update"/>), and native's
/// renderable ticks and draws it by handle once a frame (IManagedFxClient's
/// SpriteTrailTick / SpriteTrailDraw), handing in the curtime and the
/// interpolated place it follows. The draw is the beam executor's: the
/// sprite's material bound, one CBeamSegDraw strip.
/// </summary>
public sealed class ClientSpriteTrails(IClientBeamExecutor executor, IEngineHost random) : IClientSpriteTrailHost
{
    private readonly Dictionary<EntityHandle, SpriteTrail> _trails = new();
    private readonly BeamSeg[] _segs = new BeamSeg[SpriteTrail.MaxPoints + 1];

    /// <summary>Ticks and draws since the load (cs_cl_fx_report).</summary>
    public long Ticks { get; private set; }

    public long Draws { get; private set; }

    public int Count => _trails.Count;

    /// <summary>The entity's numbers changed (DT_SpriteTrail's, now its net state).</summary>
    public void Update(EntityHandle owner, in SpriteTrailParams p)
    {
        if (!_trails.TryGetValue(owner, out SpriteTrail? trail))
            _trails[owner] = trail = new SpriteTrail();
        trail.Params = p;
    }

    /// <summary>The entity went.</summary>
    public void Remove(EntityHandle owner) => _trails.Remove(owner);

    public bool Tick(EntityHandle owner, float curTime, in Vector origin, out Vector mins, out Vector maxs)
    {
        mins = maxs = default;
        if (!_trails.TryGetValue(owner, out SpriteTrail? trail))
            return false;

        Ticks++;
        trail.Tick(curTime, origin, random);
        mins = trail.RenderMins;
        maxs = trail.RenderMaxs;
        return true;
    }

    public int Draw(EntityHandle owner, float curTime, in Vector origin)
    {
        if (!_trails.TryGetValue(owner, out SpriteTrail? trail) || !trail.WouldDraw || trail.Params.ModelIndex <= 0)
            return 0;

        // Stock bound the sprite (wireframe when asked), then CBeamSegDraw::Start
        // auto-bound the sprite's own material over it: the ribbon never drew in
        // wireframe, so this binds the sprite's material and nothing else. A
        // sprite with no material for the mode draws nothing and retires
        // nothing, as stock's early return did.
        if (!executor.BindSprite(trail.Params.ModelIndex, 0, trail.Params.RenderMode, wireframe: false))
            return 0;

        int n = trail.Draw(curTime, origin, _segs);
        if (n >= 2)
            executor.Strip(System.Runtime.InteropServices.MemoryMarshal.Cast<BeamSeg, float>(_segs.AsSpan(0, n)));
        Draws++;
        return n;
    }
}
