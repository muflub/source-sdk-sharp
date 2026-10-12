using SourceSharp.Abi;
using SourceSharp.Host.Sdk;

namespace TF2.Descent.Server;

/// <summary>
/// This instance's life in the hosting service (spec §3-4): start the SDK at
/// server start, Booting at every level, MapReady when the level is up, and
/// quit loudly on a refusal that retrying cannot fix.
/// </summary>
[SideService(Side.Server)]
public sealed class HostService(
    IHostLink link,
    IBoundPort port,
    IFrameScheduler scheduler,
    IServerConsole console,
    IHooks<IHostBootHook> boot,
    ILog log) : IGameLifecycle
{
    public int LifecycleOrder => ModStages.First + 10;

    /// <summary>How this server runs; decided at start.</summary>
    public HostingMode? Mode { get; private set; }

    /// <summary>What Booting answered for the current level, or null before it has.</summary>
    public BootInfo? Boot { get; private set; }

    /// <summary>Set once MapReady has been accepted for the current level.</summary>
    public bool Live { get; private set; }

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public void OnPostInit()
    {
        try
        {
            Mode = link.Mode();
        }
        catch (InvalidOperationException e)
        {
            Fail(e.Message);
            return;
        }

        if (Mode == HostingMode.Unhosted)
        {
            log.Msg("[TF2Descent] running unhosted (-unhosted): no hosting service, for local testing only\n");
            return;
        }

        link.Start();
    }

    public void OnLevelInit(string mapName)
    {
        Boot = null;
        Live = false;
        if (Mode == HostingMode.Hosted)
            _ = Guard("Booting", () => BootLevel(mapName));
    }

    public void OnLevelReady(string mapName)
    {
        if (Mode == HostingMode.Hosted)
            _ = Guard("MapReady", () => MapReady(mapName));
    }

    public void OnShutdown() => link.Dispose();

    /// <summary>Booting, retried while the service cannot be reached or is not ready for us.</summary>
    private async Task BootLevel(string map)
    {
        while (true)
        {
            HostResult<BootInfo> result;
            try
            {
                result = await link.Booting(map);
            }
            catch (HostConfigException e)
            {
                Fail($"the host SDK could not start: {e.Message}");
                return;
            }

            if (result.Ok)
            {
                Boot = result.Value;
                log.Msg($"[TF2Descent] booted: {Boot.InstanceKind} depth {Boot.Depth}, module {Boot.Module}\n");
                boot.Each(nameof(IHostBootHook.OnBooted), h => h.OnBooted(Boot));
                return;
            }

            if (result.Refusal!.Error is HostError.Unreachable or HostError.Rejected)
            {
                await scheduler.Delay(RetryDelay);
                continue;
            }

            Fail($"Booting was refused: {Describe(result.Refusal)}");
            return;
        }
    }

    /// <summary>MapReady with the port the socket bound, retried only while unreachable.</summary>
    private async Task MapReady(string map)
    {
        // Booting first: MapReady before the service knows this level is a refusal.
        while (Boot is null)
            await scheduler.Delay(RetryDelay);

        if (port.Read() is not { } bound)
        {
            Fail("the server's bound UDP port cannot be told; refusing to guess one for MapReady");
            return;
        }

        while (true)
        {
            var result = await link.MapReady(bound, map);
            if (result.Ok)
            {
                Live = true;
                log.Msg($"[TF2Descent] live on port {bound}\n");
                boot.Each(nameof(IHostBootHook.OnLive), h => h.OnLive());
                return;
            }

            if (result.Refusal!.Error is HostError.Unreachable)
            {
                await scheduler.Delay(RetryDelay);
                continue;
            }

            Fail($"MapReady was refused: {Describe(result.Refusal)}");
            return;
        }
    }

    /// <summary>
    /// An awaited flow that nobody awaits: anything it throws fails the server
    /// loudly, here, on the game thread its continuations run on.
    /// </summary>
    private async Task Guard(string what, Func<Task> flow)
    {
        try
        {
            await flow();
        }
        catch (Exception e)
        {
            Fail($"{what} threw: {e}");
        }
    }

    private static string Describe(HostRefusal r) => $"{r.Error} ({r.Reason}{(string.IsNullOrEmpty(r.Detail) ? "" : ": " + r.Detail)})";

    /// <summary>Loud, then gone: a silent stall looks exactly like a hang (spec §4).</summary>
    private void Fail(string why)
    {
        log.Warning($"[TF2Descent] HOSTING FAILED: {why}\n");
        boot.Each(nameof(IHostBootHook.OnHostFailed), h => h.OnHostFailed(why));
        console.Quit(1);
    }
}
