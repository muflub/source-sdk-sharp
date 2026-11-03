using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared.Anim;

/// <summary>
/// What a hook is asked about: the player, the frame, and what stands in
/// their hands (the seam's answer — null where nothing names a weapon).
/// </summary>
public readonly record struct TFAnimHookContext(
    int EntityIndex,
    TFClass PlayerClass,
    TFAnimWeapon? Weapon,
    float Now);

/// <summary>
/// A link in the anim chain (plan D10 — <c>docs/plans/plan_tf2_port.md</c>):
/// TF2's files extend the core's machine, and a mod built on TF2 extends
/// TF2's by adding a hook, never by subclassing the layer. The hooks are
/// consulted inside <see cref="TFAnimLayer"/> before its decision, at the
/// two points a mod's <c>CTFPlayerAnimState</c> subclass would have
/// overridden stock's virtuals: the main activity (what stock's
/// <c>CalcMainActivity</c> answer would have been) and a gesture (what
/// stock's <c>RestartGesture</c> would have landed in the slot).
///
/// <para>
/// The first hook that answers wins — an override, like the subclass's
/// early return: the layer's own decision is not consulted after one.
/// Everything unclaimed answers null and the layer decides as stock wrote
/// it. The hooks run on the server and on every client replay, so they
/// decide from their inputs alone.
/// </para>
/// </summary>
public interface ITFAnimHook : IOrderedHook
{
    /// <summary>
    /// The main activity to give the model instead of the gait handlers'
    /// answer; null means the layer decides. Stock had no such arm — this
    /// is the mod's <c>CalcMainActivity</c> override, expressed as a link.
    /// </summary>
    string? OverrideMainActivity(in TFAnimHookContext context) => null;

    /// <summary>
    /// The gesture to land in <paramref name="gestureSlot"/> instead of the
    /// event's claim; null means the layer's choice stands. The mod's
    /// <c>RestartGesture</c> override, expressed as a link.
    /// </summary>
    string? OverrideGesture(in TFAnimHookContext context, int gestureSlot, string gestureActivity) => null;
}

/// <summary>
/// The <see cref="ITFAnimHook"/> chain, ordered by assembly layer and then
/// <see cref="IOrderedHook.HookOrder"/> — the same rule as
/// <c>TFWeaponHookChain</c>: TF2's hooks run before a mod's built on it
/// whatever their numbers. A hook that throws is reported by name and
/// stands aside; the answers after it are still asked.
///
/// <para>
/// TF2 ships no member of its own — stock's anim logic is the layer, not a
/// hook. Members are the mods', and they take their orders from the band
/// the port reserves for them: <c>999_310..999_319</c>, unique for TF2's
/// ordered participants and held by <c>TF2LifecycleOrderTests</c>.
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAnimHookChain(IHooks<ITFAnimHook>? hooks = null, ILog? log = null)
{
    private IReadOnlyList<ITFAnimHook>? _sorted;

    /// <summary>A chain over a fixed list (facts, and composition without a container).</summary>
    public static TFAnimHookChain Of(params ITFAnimHook[] hooks) => new(new FixedHooks(hooks));

    /// <summary>The hooks in the order they are asked.</summary>
    public IReadOnlyList<ITFAnimHook> All => _sorted ??= Sort(hooks?.All ?? []);

    /// <summary>An assembly's layer, the core's rule (<see cref="HookOrder.Layer(Assembly)"/>): its name without the side segments.</summary>
    public static string Layer(Assembly assembly) => HookOrder.Layer(assembly);

    public static IReadOnlyList<ITFAnimHook> Sort(IEnumerable<ITFAnimHook> hooks) =>
        hooks.OrderBy(h => Layer(h.GetType().Assembly), StringComparer.Ordinal)
             .ThenBy(h => h.HookOrder)
             .ToArray();

    /// <summary>The first hook's main-activity answer, else null — the layer decides.</summary>
    public string? MainOverride(in TFAnimHookContext context)
    {
        foreach (var hook in All)
        {
            try
            {
                if (hook.OverrideMainActivity(in context) is { } answer)
                    return answer;
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFAnimHook.OverrideMainActivity), e);
            }
        }

        return null;
    }

    /// <summary>The first hook's gesture answer for the slot, else null — the layer's choice stands.</summary>
    public string? GestureOverride(in TFAnimHookContext context, int gestureSlot, string gestureActivity)
    {
        foreach (var hook in All)
        {
            try
            {
                if (hook.OverrideGesture(in context, gestureSlot, gestureActivity) is { } answer)
                    return answer;
            }
            catch (Exception e)
            {
                Report(hook, nameof(ITFAnimHook.OverrideGesture), e);
            }
        }

        return null;
    }

    private void Report(ITFAnimHook hook, string what, Exception e) =>
        log?.Warning($"[TF2/Anim] ITFAnimHook.{what} on {hook.GetType().FullName} threw: {e.Message}; it stands aside\n");

    private sealed class FixedHooks(ITFAnimHook[] hooks) : IHooks<ITFAnimHook>
    {
        public IReadOnlyList<ITFAnimHook> All => hooks;

        public void Each(string what, Action<ITFAnimHook> call)
        {
            foreach (var hook in hooks)
                call(hook);
        }
    }
}

/// <summary>Registers the <see cref="ITFAnimHook"/> dispatch on each side.</summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAnimHooksRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFAnimHook>();
}
