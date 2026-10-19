using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The client and player lifecycle calls into the mod, managed-built
/// (plan_engineapi_vtables E10): the Entry.cs thunks' bodies, moved. Two keep
/// their own catch because their failure answer is not 0: a connect check that
/// faults ALLOWS the player (never lock players out because managed code
/// faulted), and a relationship that faults answers the stock rule.
/// </summary>
public sealed unsafe class ClientsBinding(Crossings crossings, ILog log, IServerModule game) : IManagedClients
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public int OnClientConnect(byte* a0, byte* a1, byte* a2, int a3)
    {
        Enter(CrossingOp.ServerOnClientConnect);
        try
        {
            string? reason = game.OnClientConnect(Utf8(a0), Utf8(a1));
            if (reason is null) return 1;   // allow

            if (a2 != null && a3 > 0)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(reason);
                int n = Math.Min(bytes.Length, a3 - 1);
                for (int i = 0; i < n; i++) a2[i] = bytes[i];
                a2[n] = 0;
            }
            return 0;   // refuse
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClients.OnClientConnect", e);
            return 1;   // never lock players out because managed code faulted
        }
    }

    public void OnClientPutInServer(int a0, byte* a1) { Enter(CrossingOp.ServerOnClientPutInServer); game.OnClientPutInServer(a0, Utf8(a1)); }

    public void OnClientActive(int a0, int a1) { Enter(CrossingOp.ServerOnClientActive); game.OnClientActive(a0, a1 != 0); }

    public void OnClientGamePrecache() { Enter(CrossingOp.ServerOnClientGamePrecache); game.OnClientGamePrecache(); }

    public void OnRespawn(int a0, int a1) { Enter(CrossingOp.ServerOnRespawn); game.OnRespawn(a0, a1 != 0); }

    public void OnPlayerSpawn(uint a0) { Enter(CrossingOp.ServerOnPlayerSpawn); game.OnPlayerSpawn(new EntityHandle(a0)); }

    public void OnPlayerSpawned(uint a0) { Enter(CrossingOp.ServerOnPlayerSpawned); game.OnPlayerSpawned(new EntityHandle(a0)); }

    public void OnPlayerKilled(uint a0, uint a1)
    {
        Enter(CrossingOp.ServerOnPlayerKilled);
        game.OnPlayerKilled(new EntityHandle(a0), new EntityHandle(a1));
    }

    public uint OnSelectSpawnPoint(uint a0) { Enter(CrossingOp.ServerSelectSpawnPoint); return game.SelectSpawnPoint(new EntityHandle(a0)).Value; }

    public int GetPlayerRelationship(uint a0, uint a1)
    {
        Enter(CrossingOp.ServerGetPlayerRelationship);
        try { return (int)game.GetPlayerRelationship(new EntityHandle(a0), new EntityHandle(a1)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClients.GetPlayerRelationship", e);
            return (int)PlayerRelationship.UseStockRule;
        }
    }

    public int ServerSkyboxAreaForClient(int a0, void* a1)
    {
        Enter(CrossingOp.ServerSkyboxAreaForClient);
        if (game.Entities is not { } entities) return 0;
        if (!entities.SkyboxAreaForClient(a0, out int area)) return 0;
        if (a1 != null) *(int*)a1 = area;
        return 1;
    }

    public int ServerTransmitAlwaysForClient(int a0)
    {
        Enter(CrossingOp.ServerTransmitAlwaysForClient);
        return game.Entities is { } entities ? entities.TransmitAlwaysForClient(a0) : -1;
    }

    public int OnClientNetMessage(int a0, byte* a1, void* a2, int a3)
    {
        Enter(CrossingOp.ServerOnClientNetMessage);
        try
        {
            var payload = a2 == null || a3 <= 0 ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(a2, a3);
            return game.OnClientNetMessage(a0, Utf8(a1), payload) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClients.OnClientNetMessage", e);
            return 0;
        }
    }

    public int OnClientSettingsChanged(int a0)
    {
        Enter(CrossingOp.ServerOnClientSettingsChanged);
        try
        {
            return game.OnClientSettingsChanged(a0) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClients.OnClientSettingsChanged", e);
            return 0;
        }
    }

    public int ServerAreaPortalVisibility(void* a0, float a1, void* a2, int a3)
    {
        Enter(CrossingOp.ServerAreaPortalVisibility);
        if (game.Clients is not { } clients || a0 == null || a2 == null || a3 <= 0) return -1;
        float* origin = (float*)a0;
        try
        {
            return clients.AreaPortalVisibility(new Vector(origin[0], origin[1], origin[2]), a1, new Span<byte>(a2, a3));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedClients.ServerAreaPortalVisibility", e);
            return -1;
        }
    }

    public int ServerEarPositionForClient(int a0, void* a1)
    {
        Enter(CrossingOp.ServerEarPositionForClient);
        if (game.Clients is not { } clients) return 0;
        if (!clients.EarPositionForClient(a0, out Vector ears)) return 0;
        if (a1 != null)
        {
            float* xyz = (float*)a1;
            xyz[0] = ears.X;
            xyz[1] = ears.Y;
            xyz[2] = ears.Z;
        }
        return 1;
    }
}
