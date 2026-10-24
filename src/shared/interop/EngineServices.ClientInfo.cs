namespace SourceSharp.Abi;

public static partial class EngineServices
{
    /// <summary>
    /// What the server engine knows about each connected client: its SteamID
    /// and its address (plan_host_sdk §2.3). Server only: the client engine has
    /// no IVEngineServer. Over <paramref name="server"/>, the composing
    /// module's own (plan_abi_hotreload D8).
    /// </summary>
    public static IClientInfo ClientInfo(IFwdEngineServer server)
        => new EngineClientInfo(server ?? throw new ArgumentNullException(nameof(server)));

    private sealed class EngineClientInfo(IFwdEngineServer server) : IClientInfo
    {
        public ulong SteamId64(int entIndex) => server.ClientSteamId(entIndex);

        public string? Address(int entIndex) => server.ClientAddress(entIndex);

        public float? AvgLatency(int entIndex) => server.ClientAvgLatency(entIndex);

        public string? ClientConVar(int entIndex, string name) => server.ClientConVarValue(entIndex, name);
    }
}
