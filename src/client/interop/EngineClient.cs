namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IFwdEngineClient"/> over the engine's own object, called
/// through its forwarder table (<see cref="EngineClientFwd"/>). Also the input host's
/// <see cref="Client.Input.IClientInputEngine"/>, implemented explicitly so the
/// class offers game code exactly its interface.
/// </summary>
public sealed unsafe class EngineClient : IFwdEngineClient, ISteamLaunchCommands, ISaveMemory, Client.Input.IClientInputEngine,
                                          IPlaybackState, IClientPredictCommand, IWeaponSelectionCommand,
                                          IViolenceSettings
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

    // The view area those four proxies are slots of (plan_shim_areas_split
    // phase 11): the side's, taken when this was acquired.
    private readonly IViewClientArea _view;

    private EngineClient(EngineSide side, IViewClientArea view, void* self, EngineClientFwd* table)
    {
        _side = side;
        _view = view;
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// The engine client, or null with the reason, acquired through
    /// the side's own interface factory. Null on the server,
    /// which is not an error: this interface exists only on the client.
    /// </summary>
    internal static IFwdEngineClient? Acquire(EngineSide side, out string why)
    {
        return Forwarders.TryAcquire<EngineClientFwd>(
                   side.Attachment.Interfaces, SourceSharpFactoryType.AppSystem,
                   InterfaceVersion, out void* self, out EngineClientFwd* table, out why)
            ? new EngineClient(side, side.Attachment.Areas.Require<IViewClientArea>(), self, table)
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
                                    Vtable.TextOf(raw.GuidBuffer, PlayerInfoT.GuidBytes) ?? string.Empty,
                                    raw.CustomFile0);
        return true;
    }

    /// <inheritdoc/>
    public bool CopyLocalFile(string source, string destination)
    {
        fixed (byte* s = Vtable.Utf8(source))
        fixed (byte* d = Vtable.Utf8(destination))
            return _fwd->CopyLocalFile(_self, s, d) != 0;
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
    void IClientPredictCommand.SetPredict(bool predict)
    {
        ReadOnlySpan<byte> text = predict ? "cl_predict 1\0"u8 : "cl_predict 0\0"u8;
        fixed (byte* p = text)
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

    /// <inheritdoc/>
    void IWeaponSelectionCommand.SendSelection(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _fwd->ServerCmd(_self, p, 1);
    }

    /// <inheritdoc/>
    void IWeaponSelectionCommand.Escape()
    {
        fixed (byte* p = "escape\0"u8)
            _fwd->ClientCmd(_self, p);
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
        => _side.ConNPrintf(_view, _self, position, text);

    /// <inheritdoc/>
    public void ConsolePrintAt(int position, float timeToLive,
                               float red, float green, float blue,
                               bool fixedWidthFont, string text)
        => _side.ConNXPrintf(_view, _self, position, timeToLive, red, green, blue,
                             fixedWidthFont, text);

    /// <inheritdoc/>
    public bool TryLightForPoint(in Vector position, bool clamp, out Vector light)
        => _side.TryGetLightForPoint(_view, _self, position, clamp, out light);

    /// <inheritdoc/>
    public bool IsInGame() => _fwd->IsInGame(_self) != 0;

    /// <inheritdoc/>
    public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light)
        => _side.TryGetLightForPointFast(_view, _self, position, clamp, out light);

    /// <inheritdoc/>
    Client.Input.InputAngles Client.Input.IClientInputEngine.GetViewAngles()
    {
        float* a = stackalloc float[3];
        _fwd->GetViewAngles(_self, a);
        return new Client.Input.InputAngles(a[0], a[1], a[2]);
    }

    /// <inheritdoc/>
    void Client.Input.IClientInputEngine.SetViewAngles(Client.Input.InputAngles angles)
    {
        float* a = stackalloc float[3] { angles.Pitch, angles.Yaw, angles.Roll };
        _fwd->SetViewAngles(_self, a);
    }

    /// <inheritdoc/>
    void Client.Input.IClientInputEngine.GetScreenSize(out int width, out int height)
    {
        int w = 0, h = 0;
        _fwd->GetScreenSize(_self, &w, &h);
        width = w;
        height = h;
    }

    /// <inheritdoc/>
    bool Client.Input.IClientInputEngine.IsPaused => _fwd->IsPaused(_self) != 0;

    /// <inheritdoc/>
    bool IPlaybackState.IsPaused => _fwd->IsPaused(_self) != 0;

    /// <inheritdoc/>
    bool IPlaybackState.IsPlayingDemo => _fwd->IsPlayingDemo(_self) != 0;

    /// <inheritdoc/>
    bool IPlaybackState.IsPlayingTimeDemo => _fwd->IsPlayingTimeDemo(_self) != 0;

    /// <inheritdoc/>
    bool IViolenceSettings.EngineSaysLowViolence => _fwd->IsLowViolence(_self) != 0;

    /// <inheritdoc/>
    void Client.Input.IClientInputEngine.GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta)
    {
        int dx = 0, dy = 0;
        // NULL where the engine is built without SDL (Valve's Windows engine
        // has no GetMouseDelta): then there is no delta to read.
        if (_fwd->GetMouseDelta != null)
            _fwd->GetMouseDelta(_self, &dx, &dy, (byte)(ignoreNextMouseDelta ? 1 : 0));
        x = dx;
        y = dy;
    }

    /// <inheritdoc/>
    void Client.Input.IClientInputEngine.ClientCmdUnrestricted(string command)
    {
        byte[] bytes = Vtable.Utf8(command);
        fixed (byte* p = bytes)
            _fwd->ClientCmd_Unrestricted(_self, p);
    }
}

/// <summary>
/// <c>player_info_t</c> (public/cdll_int.h): 132 bytes, name at 0, userID at
/// 32, guid at 36, fakeplayer at 108 (shell_hud.cpp asserts the same four of the
/// C++), and customFiles at 112 (shell_te_backends_client.cpp asserts that one).
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

    /// <summary><c>customFiles[0]</c>: the spray logo's CRC32, 0 for none.</summary>
    [System.Runtime.InteropServices.FieldOffset(112)] public uint CustomFile0;
}

/// <summary>
/// The real <see cref="IFwdEngineClientProbe"/>: the engine client asked of
/// the composition's own side, which the client's root registers.
/// </summary>
internal sealed class FwdEngineClientProbe(ModuleEngine engine) : IFwdEngineClientProbe
{
    private readonly ModuleEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public bool Served(out string why) => EngineClient.Acquire(_engine.Api, out why) is not null;
}
