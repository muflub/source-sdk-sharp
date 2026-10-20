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
///
/// <para>
/// <c>IFwd</c>: the engine's own <c>IVEngineClient</c>, called through its
/// forwarder table. It was <c>IFwdEngineClient</c> from when managed code
/// called the engine's vtable; nothing does since the forwarders.
/// </para>
/// </summary>
public interface IFwdEngineClient
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

    /// <summary>
    /// <c>IVEngineClient::SentenceIndexFromName( name )</c>: the sentence's
    /// index, or -1 -- what the client's UTIL_EmitAmbientSound plays a
    /// "!NAME" by (stock's client SENTENCEG_Lookup).
    /// </summary>
    int SentenceIndexFromName(string name);

    /// <summary>
    /// <c>IVEngineClient::GetLocalPlayer()</c>: the viewer's entity index (its
    /// player slot), what a CLocalPlayerFilter holds.
    /// </summary>
    int LocalPlayerSlot();

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

/// <summary>What the scoreboard needs of <c>player_info_t</c>.</summary>
/// <param name="Guid">player_info_t's guid (the hashed id voice bans key on); empty when the engine has none.</param>
public readonly record struct ClientPlayerInfo(string Name, int UserId, bool Fake, string Guid = "");

/// <summary>The three commands a spectator's keys run (stock HandleSpectatorKeyInput).</summary>
public enum SpectatorKeyCommand
{
    Next,
    Prev,
    Mode,
}
