using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_physics_events</c> — what the physics event ring is actually
/// delivering, and what managed code does with it.
///
/// <para>
/// <b>THE INSTRUMENT FOR THE DEFECT THIS LANE FIXED, kept because the defect
/// was invisible.</b> Step 3a's ring was drained into a null sink from the day
/// it was built and nothing anywhere said so: the native counter
/// <c>cs_sv_physics_shell</c> printed (<c>drain crossings</c>) proved the bind
/// and not whether managed code ran; this was the other half. The ring, its
/// sink and that command are gone (plan_physics_bones_native_zero 2.3: the
/// physics frame posts its events to the router directly), so its drain count
/// went too (2026-10-12) and the per-kind counts are the responses managed's
/// own callbacks posted.
/// </para>
///
/// <para>
/// <b>And it names the kinds nobody consumes.</b> START_TOUCH, END_TOUCH, WAKE
/// and SLEEP arrive every frame and nothing acts on them. That is a carrier
/// with no consumer — the exact state this lane found the whole ring in — so it
/// is printed rather than left to be rediscovered.
/// </para>
///
/// <para>
/// <c>cs_sv_physics_events reset</c> zeroes the counters, so a reading can be
/// about a known window rather than about the whole process.
/// </para>
/// </summary>
[ConCommand("cs_sv_physics_events",
    Help = "What the physics event ring delivered to managed code: events by kind, "
         + "and what the impact response did with them.\n\tArguments: reset")]
public sealed class PhysicsEventsCommand : IConCommand
{
    private static readonly ImmutableArray<PhysicsEventKind> Kinds =
    [
        PhysicsEventKind.StartTouch,
        PhysicsEventKind.EndTouch,
        PhysicsEventKind.PreCollision,
        PhysicsEventKind.PostCollision,
        PhysicsEventKind.Friction,
        PhysicsEventKind.FluidStart,
        PhysicsEventKind.FluidEnd,
        PhysicsEventKind.EnterTrigger,
        PhysicsEventKind.LeaveTrigger,
        PhysicsEventKind.Wake,
        PhysicsEventKind.Sleep,
        PhysicsEventKind.ConstraintBroken,
        PhysicsEventKind.Update,
        PhysicsEventKind.Stress,
        PhysicsEventKind.ShadowUpdate,
        PhysicsEventKind.Unknown,
    ];

    /// <summary>The kinds the impact response acts on. Everything else is carried and dropped.</summary>
    // PostCollision is the impact response's; Wake and Sleep reach the entity
    // through PhysicsEventRouter (physics stock parity, step 3), FluidStart
    // and FluidEnd the fluid touches (I2), Friction the scrapes (D2), and
    // Stress the player's crush (B5).
    private static bool IsConsumed(PhysicsEventKind kind)
        => kind is PhysicsEventKind.PostCollision or PhysicsEventKind.Wake or PhysicsEventKind.Sleep
                or PhysicsEventKind.Update or PhysicsEventKind.FluidStart or PhysicsEventKind.FluidEnd
                or PhysicsEventKind.Friction or PhysicsEventKind.Stress or PhysicsEventKind.ShadowUpdate;

    private readonly PhysicsCollisionResponse _response;
    private readonly PhysicsEventRouter _router;
    private readonly IEngineConsole _console;

    public PhysicsEventsCommand(PhysicsCollisionResponse response, PhysicsEventRouter router, IEngineConsole console)
    {
        _response = response;
        _router = router;
        _console = console;
    }

    public void Execute(string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            _response.Reset();
            _console.Msg("[SourceSharp] physics events: counters zeroed\n");
            return;
        }

        _console.Msg("[SourceSharp] physics events (managed side of the ring's drain)\n");
        _console.Msg($"  events           {_response.Events}\n");

        foreach (PhysicsEventKind kind in Kinds)
        {
            long n = _response.SeenOfKind(kind);
            if (n == 0)
                continue;

            _console.Msg($"    {kind,-18} {n}{(IsConsumed(kind) ? "" : "   <-- carried, nothing consumes it")}\n");
        }

        _console.Msg($"  responded        {_response.Responded}  (passed all three of VPhysicsCollision's gates)\n");
        _console.Msg($"  updates delivered {_router.UpdatesDelivered}  (OnPhysicsUpdate to awake objects, stock's VPhysicsUpdate)\n");
        _console.Msg($"  screen shakes    {_response.Shakes}\n");
        _console.Msg($"  dust puffs       {_response.Dusts}\n");
        _console.Msg($"  impact sounds    {_response.SoundsQueued}  (queued into the engine's 50 ms batch)\n");
        _console.Msg($"  scrapes started  {_router.Friction.Started}  ({_router.Friction.Active} playing; VPhysicsFriction's looping scrape)\n");
        _console.Msg($"  stress felt      {_router.StressFelt}  (a player's hull under a moving, non-friendly pusher)\n");
        _console.Msg($"  stress crushes   {_router.Impacts.StressCrushes}  (a player squeezed past five body weights, stock's ApplyStressDamage)\n");
        _console.Msg($"  shadow moves     {_router.Shadows.Moves}  ({_router.Shadows.Impulses} impulses; VPhysicsShadowUpdate brought a player to the solver)\n");
        _console.Msg($"  managed posted   {_router.Posted}  (events from managed's own vphysics callbacks, B2)\n");
        _console.Msg($"  entity solvers   {_router.Solver.Created}  ({_router.Solver.Active} active; stuck debris pulled apart, stock's CPhysicsEntitySolver)\n");
    }
}
