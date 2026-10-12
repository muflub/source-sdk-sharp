namespace SourceSharp.Abi;

//=============================================================================
// IVEngineClient, WHICH IS TWO CLASSES AND ONE TABLE.
//
// cdll_int.h declares
//
//     abstract_class IVEngineClient013 { ... 138 virtuals ... };
//     abstract_class IVEngineClient : public IVEngineClient013 { ... 8 more ... };
//
// Single inheritance, so the derived interface KEEPS the base's slots where
// they are and appends: GetProtocolVersion is slot 138, not slot 0. The
// generated struct walks the pair as one chain and is 146 fields long.
//
//-----------------------------------------------------------------------------
// THE TWO CONDITIONAL DECLARATIONS, AND WHERE THAT PROBLEM WENT.
//
// Two of those 138 sit inside preprocessor conditionals:
//
//     #if !defined( NO_VOICE )
//         virtual IVoiceTweak_s *GetVoiceTweakAPI( void ) = 0;   // slot 53
//     #endif
//     #if defined( USE_SDL )
//         virtual void GetMouseDelta( int &, int &, bool ) = 0;  // slot 127
//     #endif
//
// Each answer shifts every slot below it by one, so there are four compilations
// of this class and the version string cannot tell them apart --
// VENGINE_CLIENT_INTERFACE_VERSION is a #define OUTSIDE the class body and reads
// "VEngineClient014" under all four.
//
// This used to be answered by measurement against the shipped engine.so,
// resolving a "conditional floor" from 53 to 146 on every run. That is all
// gone.
//
// WHAT REPLACES IT IS THAT THE QUESTION IS NOT ASKED. The owner's rule is that
// native compiles with the same defines as the engine, and the shipped engine
// has both conditionals IN. shell_vtable_interfaces.cpp `#error`s if it is built
// with USE_SDL off or NO_VOICE on, vtable_index_agreement_test.cpp defines
// USE_SDL above its includes and static_asserts the resulting length, and
// tools/vtable-layout counts both declarations in. Three files that must agree,
// each of which fails loudly on its own rather than deriving anything.
//=============================================================================

/// <summary>
/// What the engine client knows about the loaded map and the mod, as game code
/// should see it — no pointer arithmetic, no slot number.
/// </summary>
public interface IVtableEngineClient
{
    /// <summary>The loaded map's own path, <c>maps/&lt;map&gt;.bsp</c>, or null.</summary>
    string? LevelName();

    /// <summary>The mod directory the engine is running out of, or null.</summary>
    string? GameDirectory();

    /// <summary>
    /// The network protocol the engine speaks — <c>PROTOCOL_VERSION</c> unless a
    /// demo is playing back, in which case it is the demo's.
    ///
    /// <para>
    /// <c>uint</c>, because <c>cdll_int.h</c> declares
    /// <c>uint GetProtocolVersion()</c>. The hand-typed row this replaced said
    /// <c>int</c> — same width and same register, so nothing was ever wrong at
    /// run time, and nothing had read the return type, which is why it drifted.
    /// </para>
    /// </summary>
    uint ProtocolVersion();

    /// <summary>
    /// Reconnects this client to the server it is on, as the console's
    /// <c>retry</c> does: what a hosting service needs once it has re-routed the
    /// player (plan_host_sdk §2.3, the user's ruling of 2026-09-30). The text is
    /// fixed here, so this is not a way to run a console command: the client
    /// still offers none (VtableBindingTests).
    /// </summary>
    void Reconnect();

    /// <summary>
    /// Queues this client's managed reload, as the console's <c>cs_cl_reload</c>
    /// does: the dev API's <c>reload/trigger</c> (plan_client_novalve §4.4).
    /// The text is fixed here, as <see cref="Reconnect"/>'s is, so this is not
    /// a way to run a console command either.
    /// </summary>
    void RequestManagedReload();

    /// <summary>
    /// Print on a line of the debug overlay.
    ///
    /// <para>
    /// THIS ONE DOES NOT GO THROUGH THE VTABLE, and it is the only method here
    /// that does not. <c>Con_NPrintf</c> is varargs, which cannot be issued
    /// through a fixed indirect signature at all, so the call happens in C++ —
    /// with <paramref name="text"/> as an ARGUMENT and never as the format
    /// string.
    /// </para>
    /// </summary>
    void ConsolePrintAt(int position, string text);

    /// <summary>
    /// The same, with the overlay line's colour and lifetime.
    /// </summary>
    void ConsolePrintAt(int position, float timeToLive,
                        float red, float green, float blue, bool fixedWidthFont, string text);

    /// <summary>
    /// The light reaching a point in the world, or false when nothing answered.
    ///
    /// <para>
    /// ALSO NOT THROUGH THE VTABLE. The engine returns <c>Vector</c> BY VALUE,
    /// which under the SysV classification comes back in <c>xmm0</c> and
    /// <c>xmm1</c> and which <c>delegate* unmanaged&lt;...&gt;</c> has no
    /// spelling for, so a C++ proxy writes the three floats through a pointer.
    /// </para>
    /// </summary>
    bool TryLightForPoint(in Vector position, bool clamp, out Vector light);

    /// <summary><c>IVEngineClient::IsInGame</c>: a level is loaded and this client is in it.</summary>
    bool IsInGame();

    /// <summary>The cheaper approximation of the same, from the same proxy pair.</summary>
    bool TryLightForPointFast(in Vector position, bool clamp, out Vector light);

    /// <summary>
    /// A connected player's name, user id and whether a bot (GetPlayerInfo),
    /// for the scoreboard (plan_client_ui U3). False for an empty slot.
    /// </summary>
    bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info);

    /// <summary>
    /// The team menu's choice (plan_client_ui U3): <c>jointeam N</c> through
    /// ClientCmd, a fixed text like <see cref="Reconnect"/>'s. 0 is auto-assign.
    /// </summary>
    void JoinTeam(int team);

    /// <inheritdoc cref="ISteamLaunchCommands.ConnectFromSteamLaunch"/>
    void ConnectFromSteamLaunch(string connectLine);

    /// <summary>
    /// A line the player typed into the UI's console (plan_client_ui U3), run as
    /// the engine console runs one: ClientCmd. The console window's whole
    /// purpose, and the one place a command text comes from outside this file.
    /// </summary>
    void SubmitConsoleLine(string line);

    /// <summary>
    /// A spectator's key (stock ClientModeShared::HandleSpectatorKeyInput):
    /// spec_next, spec_prev or spec_mode through ClientCmd, which the client's
    /// own spec_* command forwards to the server. Fixed texts, so no allocation.
    /// </summary>
    void SpectatorCommand(SpectatorKeyCommand command);

    /// <summary>
    /// A spectator's spec_* request to the server (stock ForwardSpecCmdToServer):
    /// the line through ServerCmd, reliable, as stock's default.
    /// </summary>
    void SpectatorServerCommand(string line);

    /// <summary>The voice manager's VModEnable and vban (stock CVoiceStatus::UpdateServerState), reliably or not.</summary>
    void VoiceServerCommand(string line, bool reliable);

    // WHAT IS NOT HERE, AND WHAT CHANGED.
    //
    // Managed code MAY run console commands (user, 2026-10-05,
    // plan_client_novalve.md §8: "as more and more is in managed code"). It does
    // so through members named for their purpose -- Reconnect, RequestManagedReload,
    // JoinTeam, SpectatorCommand and SubmitConsoleLine (the UI console's free text) -- each over the
    // ClientCmd slot, which queues the line on the engine's command buffer to run
    // locally or forward to the server as stock does. The raw slots are still not
    // members: ClientCmd itself, ClientCmd_Unrestricted (which skips the buffer's
    // FCVAR_CLIENTCMD_CAN_EXECUTE restriction) and ExecuteClientCmd (which runs at
    // once, re-entering the caller) have no method here, and nothing has asked for
    // what they add over ClientCmd. LoadModel loads a model and TakeScreenshot
    // writes a file; neither is offered.
    //
    // The reasons are in the generated struct's own comments and in
    // tools/vtable-policy.json.
}

/// <summary>
/// The one console command a Steam launch runs (stock UnsafeCmdLineProcessor,
/// game/shared/secure_command_line.cpp): <c>connect &lt;address&gt;</c>, after
/// the managed check of every character, through ClientCmd_Unrestricted as
/// stock sends it. Unrestricted because the engine restricts client commands
/// from its client state's construction (it sets
/// m_bRestrictClientCommands), and <c>connect</c> is not
/// FCVAR_CLIENTCMD_CAN_EXECUTE, so ClientCmd would drop it.
/// </summary>
public interface ISteamLaunchCommands
{
    /// <summary>Runs <paramref name="connectLine"/>, which the caller has checked begins <c>connect </c>.</summary>
    void ConnectFromSteamLaunch(string connectLine);
}

/// <summary>
/// <see cref="IVtableEngineClient"/> over the engine's own object, called
/// through its vtable.
/// </summary>
public sealed unsafe class EngineClient : IVtableEngineClient, ISteamLaunchCommands
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
    private readonly EngineClientVtbl* _vtbl;

    // The side this was acquired on, whose slots carry the four C++ proxies
    // below (plan_abi_hotreload D8: they went through the static Engine).
    private readonly EngineSide _side;

    private EngineClient(EngineSide side, void* self, void** table)
    {
        _side = side;
        _self = self;
        _vtbl = (EngineClientVtbl*)table;
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

        return Vtable.TryAcquire<EngineClientVtbl>(
                   side.Interfaces, SourceSharpFactoryType.AppSystem,
                   InterfaceVersion, out void* self, out void** table, out why)
            ? new EngineClient(side, self, table)
            : null;
    }

    /// <inheritdoc/>
    public string? LevelName()
        => Vtable.TextOf(_vtbl->GetLevelName(_self), Vtable.MaxPath);

    /// <inheritdoc/>
    public string? GameDirectory()
        => Vtable.TextOf(_vtbl->GetGameDirectory(_self), Vtable.MaxPath);

    /// <inheritdoc/>
    public uint ProtocolVersion() => _vtbl->GetProtocolVersion(_self);

    /// <inheritdoc/>
    public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info)
    {
        PlayerInfoT raw;
        if (entIndex <= 0 || _vtbl->GetPlayerInfo(_self, entIndex, &raw) == 0)
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
            _vtbl->ClientCmd_Unrestricted(_self, p);
    }

    /// <inheritdoc/>
    public void JoinTeam(int team)
    {
        byte[] bytes = Vtable.Utf8($"jointeam {team}");
        fixed (byte* p = bytes)
            _vtbl->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void SubmitConsoleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _vtbl->ClientCmd(_self, p);
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
            _vtbl->ClientCmd(_self, p);
    }

    /// <inheritdoc/>
    public void VoiceServerCommand(string line, bool reliable)
    {
        if (string.IsNullOrEmpty(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _vtbl->ServerCmd(_self, p, (byte)(reliable ? 1 : 0));
    }

    /// <inheritdoc/>
    public void SpectatorServerCommand(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;
        byte[] bytes = Vtable.Utf8(line);
        fixed (byte* p = bytes)
            _vtbl->ServerCmd(_self, p, 1);
    }

    /// <summary>The one text <see cref="Reconnect"/> sends through the ClientCmd slot.</summary>
    internal const string ReconnectCommand = "retry";

    /// <inheritdoc/>
    public void Reconnect()
    {
        byte[] bytes = Vtable.Utf8(ReconnectCommand);

        fixed (byte* p = bytes)
            _vtbl->ClientCmd(_self, p);
    }

    /// <summary>The one text <see cref="RequestManagedReload"/> sends through the ClientCmd slot.</summary>
    internal const string ReloadCommand = "cs_cl_reload";

    /// <inheritdoc/>
    public void RequestManagedReload()
    {
        byte[] bytes = Vtable.Utf8(ReloadCommand);

        fixed (byte* p = bytes)
            _vtbl->ClientCmd(_self, p);
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
    public bool IsInGame() => _vtbl->IsInGame(_self) != 0;

    /// <inheritdoc/>
    public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light)
        => _side.TryGetLightForPointFast(_self, position, clamp, out light);
}

/// <summary>What the scoreboard needs of <c>player_info_t</c>.</summary>
/// <param name="Guid">player_info_t's guid (the hashed id voice bans key on); empty when the engine has none.</param>
public readonly record struct ClientPlayerInfo(string Name, int UserId, bool Fake, string Guid = "");

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

/// <summary>The three commands a spectator's keys run (stock HandleSpectatorKeyInput).</summary>
public enum SpectatorKeyCommand
{
    Next,
    Prev,
    Mode,
}
