namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IVtableEngineClient"/> over the engine's own object, called
/// through its vtable.
/// </summary>
public sealed unsafe class EngineClient : IVtableEngineClient, ISteamLaunchCommands, ISaveMemory
{
    /// <summary>
    /// <c>VENGINE_CLIENT_INTERFACE_VERSION</c>, from <c>cdll_int.h:188</c>.
    ///
    /// <para>
    /// It reads the same under all four compilations of this header, which is
    /// why it pins the DECLARATION and cannot pin the COMPILATION — see the
    /// note at the top of this file for what does.
    /// </para>
    /// </summary>
    public const string InterfaceVersion = "VEngineClient014";

    private readonly void* _self;
    private readonly EngineClientFwd* _fwd;

    // The side this was acquired on, whose slots carry the four C++ proxies
    // below (plan_abi_hotreload D8: they went through the static Engine).
    private readonly EngineSide _side;

    private EngineClient(EngineSide side, void* self, EngineClientFwd* table)
    {
        _side = side;
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// The engine client, or null with the reason, acquired through
    /// <paramref name="engine"/>'s own interface factory. Null on the server,
    /// which is not an error: this interface exists only on the client.
    /// </summary>
    public static IVtableEngineClient? Acquire(ModuleEngine engine, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        EngineSide side = engine.Api;

        return Forwarders.TryAcquire<EngineClientFwd>(
                   side.Interfaces, SourceSharpFactoryType.AppSystem,
                   InterfaceVersion, out void* self, out EngineClientFwd* table, out why)
            ? new EngineClient(side, self, table)
            : null;
    }

    /// <summary>IVEngineClient::SaveAllocMemory, for the shared SaveBlockSession (the client save codec).</summary>
    void* ISaveMemory.SaveAlloc(nuint count, nuint size) => _fwd->SaveAllocMemory(_self, count, size);

    /// <summary>IVEngineClient::SaveFreeMemory.</summary>
    void ISaveMemory.SaveFree(void* block) => _fwd->SaveFreeMemory(_self, block);

    /// <inheritdoc/>
    public string? LevelName()
        => Vtable.TextOf(_fwd->GetLevelName(_self), Vtable.MaxPath);

    /// <inheritdoc/>
    public int SentenceIndexFromName(string name)
    {
        fixed (byte* p = Vtable.Utf8(name ?? string.Empty))
            return _fwd->SentenceIndexFromName(_self, p);
    }

    /// <inheritdoc/>
    public int LocalPlayerSlot() => _fwd->GetLocalPlayer(_self);

    /// <inheritdoc/>
    public string? GameDirectory()
        => Vtable.TextOf(_fwd->GetGameDirectory(_self), Vtable.MaxPath);

    /// <inheritdoc/>
    public uint ProtocolVersion() => _fwd->GetProtocolVersion(_self);

    /// <inheritdoc/>
    public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info)
    {
        PlayerInfoT raw;
        if (entIndex <= 0 || _fwd->GetPlayerInfo(_self, entIndex, &raw) == 0)
        {
            info = default;
            return false;
        }
        info = new ClientPlayerInfo(Vtable.TextOf(raw.NameBuffer, PlayerInfoT.NameBytes) ?? string.Empty,
                                    raw.UserId, raw.FakePlayer != 0,
                                    Vtable.TextOf(raw.GuidBuffer, PlayerInfoT.GuidBytes) ?? string.Empty);
        return true;
    }

    /// <inheritdoc/>
    public void ConnectFromSteamLaunch(string connectLine)
    {
        if (!connectLine.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"a Steam launch runs only `connect`, not `{connectLine}`", nameof(connectLine));
        byte[] bytes = Vtable.Utf8(connectLine);
        fixed (byte* p = bytes)
            _fwd->ClientCmd_Unrestricted(_self, p);
    }

    /// <inheritdoc/>
    public void JoinTeam(int team)
    {
        byte[] bytes = Vtable.Utf8($"jointeam {team}");
        fixed (byte* p = bytes)
            _fwd->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void SubmitConsoleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _fwd->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void SpectatorCommand(SpectatorKeyCommand command)
    {
        ReadOnlySpan<byte> text = command switch
        {
            SpectatorKeyCommand.Next => "spec_next\0"u8,
            SpectatorKeyCommand.Prev => "spec_prev\0"u8,
            _ => "spec_mode\0"u8,
        };
        fixed (byte* p = text)
            _fwd->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void VoiceServerCommand(string line, bool reliable)
    {
        if (string.IsNullOrEmpty(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _fwd->ServerCmd(_self, p, (byte)(reliable ? 1 : 0));
    }

    /// <inheritdoc/>
    public void SpectatorServerCommand(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _fwd->ServerCmd(_self, p, 1);
    }

    /// <summary>The one text <see cref="Reconnect"/> sends through the ClientCmd slot.</summary>
    internal const string ReconnectCommand = "retry";

    /// <inheritdoc/>
    public void Reconnect()
    {
        byte[] bytes = Vtable.Utf8(ReconnectCommand);

        fixed (byte* p = bytes)
            _fwd->ClientCmd(_self, p);
    }

    /// <summary>The one text <see cref="RequestManagedReload"/> sends through the ClientCmd slot.</summary>
    internal const string ReloadCommand = "cs_cl_reload";

    /// <inheritdoc/>
    public void RequestManagedReload()
    {
        byte[] bytes = Vtable.Utf8(ReloadCommand);

        fixed (byte* p = bytes)
            _fwd->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void ConsolePrintAt(int position, string text)
        => _side.ConNPrintf(_self, position, text);

    /// <inheritdoc/>
    public void ConsolePrintAt(int position, float timeToLive,
                               float red, float green, float blue,
                               bool fixedWidthFont, string text)
        => _side.ConNXPrintf(_self, position, timeToLive, red, green, blue,
                             fixedWidthFont, text);

    /// <inheritdoc/>
    public bool TryLightForPoint(in Vector position, bool clamp, out Vector light)
        => _side.TryGetLightForPoint(_self, position, clamp, out light);

    /// <inheritdoc/>
    public bool IsInGame() => _fwd->IsInGame(_self) != 0;

    /// <inheritdoc/>
    public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light)
        => _side.TryGetLightForPointFast(_self, position, clamp, out light);
}

/// <summary>
/// <c>player_info_t</c> (public/cdll_int.h): 132 bytes, name at 0, userID at
/// 32, guid at 36, fakeplayer at 108 (shell_hud.cpp asserts the same four of the C++).
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 132)]
internal unsafe struct PlayerInfoT
{
    public const int NameBytes = 32;

    /// <summary>SIGNED_GUID_LEN + 1.</summary>
    public const int GuidBytes = 33;

    [System.Runtime.InteropServices.FieldOffset(0)] public fixed byte NameBuffer[NameBytes];
    [System.Runtime.InteropServices.FieldOffset(32)] public int UserId;
    [System.Runtime.InteropServices.FieldOffset(36)] public fixed byte GuidBuffer[GuidBytes];
    [System.Runtime.InteropServices.FieldOffset(108)] public byte FakePlayer;
}

/// <summary>
/// The real <see cref="IVtableEngineClientSource"/>: <see cref="EngineClient.Acquire"/>
/// over the composition's own engine, which the client's root registers.
/// </summary>
internal sealed class VtableEngineClientSource(ModuleEngine engine) : IVtableEngineClientSource
{
    private readonly ModuleEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public IVtableEngineClient? Acquire(out string why) => EngineClient.Acquire(_engine, out why);
}
