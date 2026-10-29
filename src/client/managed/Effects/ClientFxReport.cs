using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// What the client's effects did since load (lane c2-fx): dispatches resolved
/// and unclaimed, by name; IEffects calls drawn and suppressed; the effect
/// list's size, draws and refusals, and the near-miss sounds. A console read
/// until the dev API's <c>effects</c> route (plan_client_novalve §4.4) carries
/// the same numbers.
/// </summary>
[ConCommand("cs_cl_fx_report",
    Help = "Client effects since load: dispatches by name, IEffects calls, the client-side effect list, sprites planned")]
public sealed class ClientFxReportCommand(ILog log, ClientEffectDispatch dispatch, ClientEffects effects,
                                          ClientEffectList list, ClientSideEffectFactory factory,
                                          IClientEffectHandlers handlers, ClientSpriteHost sprites,
                                          ClientSpriteTrails trails, ClientEffectPrecache precache,
                                          SmokeFogOverlay smokeFog, SparkEffects sparks,
                                          ParticleViews views, WaterSplashes water, BloodEffects blood,
                                          ExplosionEffects explosions, FxEffects fx,
                                          PoolEmitters pool, ShardEffects shards, ImpactSprays sprays,
                                          ClientDustPuffs puffs, ClientTeParticles teParticles) : IConCommand
{
    public void Execute(string args)
    {
        log.Msg($"[SourceSharp/Client] fx: dispatch {dispatch.Ran} ran, {dispatch.Unclaimed} unclaimed, " +
                $"{handlers.Counts.Values.Sum()} played in C#\n");
        foreach (var (name, runs) in dispatch.RunsByName.OrderBy(p => p.Key, StringComparer.Ordinal))
            log.Msg($"[SourceSharp/Client]   {name} {runs}\n");
        log.Msg($"[SourceSharp/Client] fx: IEffects {effects.Drawn} drawn, {effects.Suppressed} suppressed\n");
        log.Msg($"[SourceSharp/Client] fx: effect list {list.Count} live, {list.Draws} draws, {list.Refused} refused, " +
                $"{factory.WhizSounds} near-miss sound(s)\n");
        log.Msg($"[SourceSharp/Client] fx: sprites {sprites.Planned} planned; trails {trails.Count} live, {trails.Ticks} ticks, {trails.Draws} draws\n");
        foreach (string trail in trails.Describe())
            log.Msg($"[SourceSharp/Client] fx:   trail {trail}\n");
        log.Msg($"[SourceSharp/Client] fx: precache {precache.LevelInits} level start(s), {precache.LevelShutdowns} end(s); smoke fog {smokeFog.Drawn} quad(s)\n");
        var (ftzNow, dazNow) = views.FloatMode.Measure();
        log.Msg($"[SourceSharp/Client] fx: float mode on the particle steps: {views.FloatMode.Samples} sample(s), " +
                $"FTZ {views.FloatMode.FlushToZero}, DAZ {views.FloatMode.DenormalsAreZero}; on this command's thread FTZ {ftzNow} DAZ {dazNow}\n");
        log.Msg($"[SourceSharp/Client] fx: sparks {sparks.Bursts} routine(s); particle views {views.Reads} read(s); " +
                $"water {water.Splashes} splash(es), {water.Ripples} ripple(s); blood {blood.Sprays} spray(s), {blood.Impacts} impact(s); explosions {explosions.Explosions}; " +
                $"fx.cpp {fx.Muzzles} muzzle flash(es), {fx.Smokes} smoke(s); " +
                $"pool emitters {pool.Emitters} -> {pool.Effects} effect(s); shards {shards.Impacts} glass impact(s), {shards.Shatters} pane(s)\n");
        // Step 7a's ledger row, in the shape cs_cl_effects_report printed it.
        log.Msg($"[SourceSharp/Client] fx:   impact blood        {sprays.BloodParticles,8}\n");
        log.Msg($"[SourceSharp/Client] fx:   dust puffs          {puffs.Particles,8}\n");
        log.Msg($"[SourceSharp/Client] fx: te particles {teParticles.Particles} started, {teParticles.QuadsDrawn} quad(s) drawn\n");
    }
}
