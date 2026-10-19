using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's three per-frame entity fetches, built by the client
/// composition and handed to its <c>IEngineEntities</c> binding (P1 run 3).
///
/// <para>
/// They read client-only slots, so they are client code: the shared binding
/// names only <see cref="IInterpolationBlocks"/>, <see cref="IRenderPolicyBlocks"/>
/// and <see cref="IClientReplayEngine"/>, and takes these from the container
/// when it is built (EngineServices.Build picks the longest constructor the
/// container satisfies). Registered before the areas, so the binding and the
/// container hold the same three objects.
/// </para>
/// </summary>
public static class ClientEntityFetches
{
    /// <summary>Registers this side's three, over its own slots. Call before <see cref="ModuleEngine.AddTo"/>.</summary>
    public static void AddTo(ServiceContainer services, ModuleEngine engine)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(engine);

        services.AddInstance<IInterpolationBlocks>(new InterpolationBlocks(engine.Api));
        services.AddInstance<IRenderPolicyBlocks>(new RenderPolicyBlocks(engine.Api));
        services.AddInstance<IClientReplayEngine>(new ClientReplayEngine(engine.Api));

        // The recv tables and schema refusals, for the dev API's nettables
        // route (plan_client_novalve §4.4). A read, not a fetch, but over the
        // same slots and with the same reason to live here: it is unsafe.
        services.AddInstance<IClientRecvTables>(new ClientRecvTables(engine.Api));

        // The rope executor and the view, light and world-hull reads a managed
        // rope makes (plan_client_novalve §2 cluster 6).
        services.AddInstance<IClientRopeExecutor>(new ClientRopeExecutor(engine.Api));
        var ropeExecutor = new ClientRopeExecutor(engine.Api);
        services.AddInstance<IClientRopeExecutor>(ropeExecutor);
        services.AddInstance<IClientComputeLighting>(ropeExecutor);

        // The beam executor and the reads the managed beam system makes
        // (plan_client_novalve §2 cluster 5).
        services.AddInstance<IClientBeamExecutor>(new ClientBeamExecutor(engine.Api));

        // The UI's draw primitives (plan_client_ui U2), for VguiBackend.
        services.AddInstance<IUiSurface>(new ClientUiSurface(engine.Api));
        services.AddInstance<IClientDebugOverlay>(new ClientDebugOverlay(engine.Api));

        // The second cameras' render targets (lane camproto).
        services.AddInstance<IViewTargets>(new ClientViewTargets(engine.Api));

        // The observer's camera (plan_client_novalve C7 item 5).
        services.AddInstance<IObserverCameraSlots>(new ObserverCameraSlots(engine.Api));

        // The net channel and the diagnostics ledgers, for the netgraph and
        // absences routes; one reader over the slots serves both.
        var readers = new ClientNetReaders(engine.Api);
        services.AddInstance<IClientNetChannel>(readers);
        services.AddInstance<IClientDiagnosticsLedgers>(readers);

        // The managed particle sims' draw executor (lane c2-sims): its
        // lifetime, which managed decides.
        // Wrapped by the managed manager's (ClientModule: BoundParticleExecutor).
        services.AddInstance<INativeParticleExecutor>(new ClientParticleExecutor(engine.Api));
        // The particles shell's started systems, for the particles route (lane c5-particles).
        services.AddInstance<IClientParticleSystems>(new ClientParticleSystems(engine.Api));
        // The ParticleEffect client effect's carry-out (lane c5-particles).
        services.AddInstance<IClientParticleOneShots>(new ClientParticleOneShots(engine.Api));
        // The particle collision's world trace (lane c5-particles).
        services.AddInstance<IParticleWorldTrace>(new ClientParticleWorldTrace(engine.Api));
        var particleTrace = new ClientParticleWorldTrace(engine.Api);
        services.AddInstance<IParticleWorldTrace>(particleTrace);
        services.AddInstance<IParticleMaskTrace>(particleTrace);
        // The view the particle manager draws in, read once per view, and the
        // glows' pixel visibility (lane c5-particles).
        // libss_particles.so's ISSParticleLib, bound directly (plan_particles_so.md).
        var particleLibrary = new ParticleLibrary(engine.Api.Interfaces);
        services.AddInstance<IParticleLibrary>(particleLibrary);
        var particleView = new ClientParticleViewSlots(engine.Api, particleLibrary);
        services.AddInstance<IParticleViewSource>(particleView);
        services.AddInstance<IParticlePixelVisibility>(particleView);
        services.AddInstance<IParticleThrottle>(particleView);
        services.AddInstance(new ParticleViews(particleView));
        // The dust motes' brush test (lane c5-particles).
        services.AddInstance<IClientBrushContents>(new ClientBrushContents(engine.Api));
        services.AddInstance<IClientModelNames>(new ClientModelNames(engine.Api));
        services.AddInstance<IClientPlaneTrace>(new ClientPlaneTrace(engine.Api));
        // point_worldtext's native text renderer (lane c2-sims): its lifetime.
        services.AddInstance<IClientWorldTextMeshes>(new ClientWorldTextMeshes(engine.Api));
        // The client wind's publication (lane c2-sims).
        services.AddInstance<IClientWindPublisher>(new ClientWindPublisher(engine.Api));
        // The material proxies' engine reads (lane c5-proxies): FindVar and
        // FindTexture at Init, the texture, entity and view reads of a bind.
        services.AddInstance<IClientMaterialProxyExecutor>(new ClientMaterialProxyExecutor(engine.Api));
        // The native effect registrations, by walk index, for the managed
        // dispatch table (plan_client_novalve §2 cluster 4).
        services.AddInstance<IClientEffectRegistrations>(new ClientEffectRegistrations(engine.Api));

        // IEffects' FX_* routines and SuppressTE's two answers (cluster 3),
        // for the managed ClientEffects; one object over the slots serves both.
        var fx = new ClientFxSlots(engine.Api);
        services.AddInstance<IFxSuppression>(fx);
        services.AddInstance<IFxImpactOverlay>(fx);

        // The client-side effect list's carry-out (cluster 3).
        services.AddInstance<IFxRender>(new ClientFxRenderSlots(engine.Api));
        services.AddInstance<IFxSpriteGlow>(new ClientFxSpriteGlowSlots(engine.Api));
        services.AddInstance<IFxSpriteDraw>(new ClientFxSpriteDrawSlots(engine.Api));
        services.AddInstance<IFxPrecache>(new ClientFxPrecacheSlots(engine.Api));

        // The client soundscape system's executors (sound cluster 17).
        services.AddInstance<ISoundscapeAudio>(new SoundscapeAudioSlots(engine.Api));
        var emitters = new ClientFxEmitterSlots(engine.Api);
        services.AddInstance<IFxFlashReads>(emitters);
        services.AddInstance<IFxGameSounds>(emitters);
        services.AddInstance<IFxTempEntModels>(emitters);
        // The client weather's engine reads (lane c2-sims).
        services.AddInstance<IClientPrecipitationEngine>(new ClientPrecipitationSlots(engine.Api));
        // The client's physics callbacks' engine reads (lane c6-input).
        var physics = new ClientPhysicsSlots(engine.Api);
        services.AddInstance<IClientImpactSoundWorld>(physics);
        // The level's environment: ClientPhysicsSystem makes it and writes it here.
        var environment = new ClientPhysicsEnvironment();
        services.AddInstance(environment);
        services.AddInstance<IClientPhysicsEngine>(environment);
        services.AddInstance<IClientPhysicsObjectReads>(physics);
        services.AddInstance<IClientSplashCarryOut>(physics);
        // The convar watch and the decided values (C8, IShimConVarsClient).
        services.AddInstance<IClientConVarSlots>(new ClientConVarSlots(engine.Api));
    }
}
