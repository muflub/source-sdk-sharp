namespace SourceSharp.Abi;

public static partial class EngineServices
{
    /// <summary>
    /// What the server engine knows about each connected client: its SteamID
    /// and its address (plan_host_sdk §2.3). Server only: the client engine has
    /// no IVEngineServer. Over <paramref name="engine"/>, the composing
    /// module's own (plan_abi_hotreload D8).
    /// </summary>
    public static IClientInfo ClientInfo(ModuleEngine engine)
        => new EngineClientInfo((engine ?? throw new ArgumentNullException(nameof(engine))).Api);

    private sealed class EngineClientInfo(EngineSide side) : IClientInfo
    {
        public ulong SteamId64(int entIndex) => EngineBound.Server(side).ClientSteamId(entIndex);

        public string? Address(int entIndex) => EngineBound.Server(side).ClientAddress(entIndex);

        public float? AvgLatency(int entIndex) => EngineBound.Server(side).ClientAvgLatency(entIndex);

        public string? ClientConVar(int entIndex, string name) => EngineBound.Server(side).ClientConVarValue(entIndex, name);
    }
}
