using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Client.Content;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Bestiary;

//=============================================================================
// WHAT THE LOCAL HERO KNOWS OF THE ROBOTS (plan_descent_logic UI6): his kill
// counts per template and each lent body as the server let him see it, from
// the bestiary's owner messages, and the IMMUNE his own hits drew. Fed by the
// messages only; forgets everything when a level ends (the server resyncs at
// the next spawn, whose messages arrive before the next level's init). Nothing
// here decides what he may see: the server sent only that.
//=============================================================================

/// <summary>The local hero's bestiary and robot views, as the server last sent them.</summary>
public interface IRobotKnowledge
{
    /// <summary>The server names other robot rows than the client embeds: names and words cannot be read rightly.</summary>
    bool ContentMismatch { get; }

    /// <summary>His kills of the template in robots.json row <paramref name="template"/>; 0 when none were sent.</summary>
    int KillsOf(int template);

    /// <summary>The view of the body in <paramref name="entity"/>, or null when none is held.</summary>
    RobotView? ViewOf(int entity);

    /// <summary>When (the client clock) his last hit on the body in <paramref name="entity"/> drew IMMUNE, and to which damage type; null for none.</summary>
    (float At, int DamageType)? ImmuneOf(int entity);

    /// <summary>Every view held, by entity (the dev print's).</summary>
    IReadOnlyDictionary<int, RobotView> Views { get; }
}

/// <summary>The robot knowledge cache over <c>bestiary</c>, <c>robot_view</c> and <c>robot_immune</c>.</summary>
[SideService(Side.Client, As = [typeof(IRobotKnowledge)])]
public sealed class RobotKnowledgeCache(DescentCatalog catalog, IGameClock clock)
    : IRobotKnowledge, IBestiaryHook, IRobotViewHook, IClientLevelHook, IOrderedHook
{
    /// <summary>TF2 Descent's robot-bar block on the client (UI6): unique among the container's level hooks.</summary>
    public int HookOrder => 1200;

    private readonly Dictionary<int, int> _kills = new();
    private readonly Dictionary<int, RobotView> _views = new();
    private readonly Dictionary<int, (float, int)> _immune = new();

    public bool ContentMismatch { get; private set; }

    public IReadOnlyDictionary<int, RobotView> Views => _views;

    public int KillsOf(int template) => _kills.GetValueOrDefault(template);

    public RobotView? ViewOf(int entity) => _views.GetValueOrDefault(entity);

    public (float At, int DamageType)? ImmuneOf(int entity) => _immune.TryGetValue(entity, out var at) ? at : null;

    public void OnBestiary(BestiaryView bestiary)
    {
        if (!Check(bestiary.Fingerprint))
            return;
        if (bestiary.Phase == BestiaryPhase.Resync)
            _kills.Clear();
        foreach (var (template, kills) in bestiary.Rows)
        {
            if (kills > 0)
                _kills[template] = kills;
            else
                _kills.Remove(template);
        }
    }

    public void OnRobotView(RobotView view)
    {
        if (!Check(view.Fingerprint))
            return;
        if (view.Gone)
        {
            // Only the lend it names: a gone for an old lend never removes the view of the next.
            if (_views.TryGetValue(view.Entity, out var held) && held.Serial == view.Serial)
                _views.Remove(view.Entity);
            _immune.Remove(view.Entity);
            return;
        }
        if (_views.TryGetValue(view.Entity, out var last) && last.Serial != view.Serial)
            _immune.Remove(view.Entity);
        _views[view.Entity] = view;
    }

    public void OnRobotImmune(int entity, int damageType) => _immune[entity] = (clock.Now, damageType);

    // Not at a level's start: the spawn's resync arrives while the client signs on,
    // before the engine's LevelInitPostEntity, and forgetting there lost it.
    public void OnLevelShutdown()
    {
        _kills.Clear();
        _views.Clear();
        _immune.Clear();
        ContentMismatch = false;
    }

    private bool Check(uint fingerprint)
    {
        ContentMismatch = fingerprint != catalog.RobotFingerprint;
        return !ContentMismatch;
    }
}

/// <summary>The client's dispatch of the bestiary's and the automap's messages (UI5, UI6).</summary>
[SideService(Side.Client)]
public sealed class RobotBarsClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IBestiaryHook>();
        registry.AddHooks<IRobotViewHook>();
        registry.AddHooks<IAutomapHook>();
    }
}
