using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The managed mirror of the engine's <c>CGlobalVars</c> (public/edict.h) and
/// the <c>CGlobalVarsBase</c> it derives from (public/globalvars_base.h), x64.
///
/// <para>
/// NOTHING CONSTRUCTS ONE OF THESE. It exists to be pointed at — the engine owns
/// the memory, allocated before the CLR existed and alive for the whole process,
/// and <see cref="Globals"/> reads through a pointer to it. See
/// src/sourcesharp/game/shared/globalvars_layout.h, which is where the offsets
/// are asserted against the real C++ class and where the reasoning lives.
/// </para>
///
/// <para>
/// THE FIELD NAMES ARE THE C++ NAMES, deliberately, down to <c>m_bClient</c> and
/// the lowercase <c>curtime</c>. This type is a layout description and not an
/// API — <see cref="Globals"/> is the API — and a mirror whose names have been
/// tidied cannot be diffed against the header by eye or by the parity test.
/// </para>
///
/// <para>
/// NO PADDING FIELDS. The four bytes before <c>pSaveData</c> and the three after
/// <c>m_bClient</c> are there, but they come from the runtime's natural
/// alignment under <see cref="LayoutKind.Sequential"/>, exactly as they come
/// from the C++ compiler's. Writing them out by hand would be a second copy of
/// the rule that put them there, and it would be the copy that goes stale.
/// </para>
///
/// <para>
/// EVERY BOOL IS A <see cref="byte"/>. C# <c>bool</c> is one byte in the runtime
/// layout and four under the marshaller, so a struct declaring <c>bool</c> has
/// two layouts that disagree with each other and one of them is wrong about
/// every field after it. The bools surface as <c>bool</c> properties on
/// <see cref="Globals"/> instead, where the conversion is explicit and costs a
/// compare.
/// </para>
///
/// <para>
/// <c>mapname</c> and <c>startspot</c> are <c>string_t</c>. In this tree that is
/// a pointer (public/string_t.h's strong form wraps a <c>const char *</c>; the
/// client's NO_STRING_T form is one), so they are <see cref="nint"/> here and
/// they are not exposed at all. See <see cref="Globals"/>.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CGlobalVarsRaw
{
    // ---- CGlobalVarsBase, public ------------------------------------------
    public float realtime;                  //   0
    public int framecount;                  //   4
    public float absoluteframetime;         //   8
    public float curtime;                   //  12
    public float frametime;                 //  16
    public int maxClients;                  //  20
    public int tickcount;                   //  24
    public float interval_per_tick;         //  28
    public float interpolation_amount;      //  32
    public int simTicksThisFrame;           //  36
    public int network_protocol;            //  40
    public nint pSaveData;                  //  48 (CSaveRestoreData *)

    // ---- CGlobalVarsBase, private, and still part of the layout -----------
    // Leaving these out does not shrink the struct by twelve bytes and stop
    // there: it moves mapname from 72 to 56 and every field after it with it.
    public byte m_bClient;                  //  56 (bool)
    public int nTimestampNetworkingBase;    //  60
    public int nTimestampRandomizeWindow;   //  64

    // ---- CGlobalVars's own fields, which begin after the base's 72 bytes ---
    public nint mapname;                    //  72 (string_t)
    public int mapversion;                  //  80
    public nint startspot;                  //  88 (string_t)
    public int eLoadType;                   //  96 (MapLoadType_t)
    public byte bMapLoadFailed;             // 100 (bool)
    public byte deathmatch;                 // 101 (bool)
    public byte coop;                       // 102 (bool)
    public byte teamplay;                   // 103 (bool)
    public int maxEntities;                 // 104
    public int serverCount;                 // 108
}

/// <summary>
/// How the current map was loaded — the engine's <c>MapLoadType_t</c>
/// (public/edict.h).
/// </summary>
public enum MapLoadType
{
    NewGame = 0,
    LoadGame = 1,
    Transition = 2,
    Background = 3,
}

/// <summary>
/// The abstract environment: curtime, frametime, tickcount and the rest, read
/// live out of the engine's own <c>CGlobalVars</c>.
///
/// <para>
/// EIGHT BYTES, AND ONE OF THEM IS THE POINT. This is a reader, not a copy.
/// <c>CGlobalVars</c> changes every frame, so a snapshot taken when the shell
/// started would be one frame stale everywhere and wrong in a way nothing
/// reports — every property here is a load from engine memory at the moment it
/// is asked.
/// </para>
///
/// <para>
/// THERE IS NOTHING TO PIN. <c>GCHandle</c> and <c>fixed</c> exist to stop the
/// collector moving a *managed heap object* out from under native code holding
/// its address. This is the other direction: native memory the engine allocated
/// before the CLR existed, at a fixed address, alive for the whole process. The
/// collector has no opinion about it and it never moves.
/// </para>
///
/// <para>
/// x64 ONLY. Every offset in <see cref="CGlobalVarsRaw"/> from <c>pSaveData</c>
/// on depends on the pointer width. globalvars_layout.h refuses to compile when
/// <c>sizeof(void *) != 8</c> and GlobalVarsLayoutParityTests refuses to pass
/// when <see cref="nint"/> is not eight bytes.
/// </para>
///
/// <para>
/// WHAT IS NOT HERE, on purpose: <c>mapname</c> and <c>startspot</c>. They are
/// <c>string_t</c> — an engine-owned pointer with an engine lifetime, not a
/// .NET string — and exposing something shaped like a string that is really a
/// number, or a pointer that may be freed under the reader, is the bug rather
/// than the feature. The map name reaches managed code through MapLoad, which
/// already carries it.
/// </para>
///
/// <para>
/// A default-constructed <c>Globals</c> holds a null pointer and every property
/// read on it throws <see cref="NullReferenceException"/>. That is deliberate:
/// the alternative is returning a plausible zero for <c>CurTime</c> forever.
/// Ask <see cref="IsAvailable"/> when it might not be set.
/// </para>
/// </summary>
public readonly unsafe struct Globals
{
    private readonly CGlobalVarsRaw* _p;

    /// <summary>Wraps the engine's globals. The pointer is not copied from.</summary>
    public Globals(CGlobalVarsRaw* globals) => _p = globals;

    /// <summary>Wraps the engine's globals as handed across the ABI boundary.</summary>
    public Globals(nint globals) => _p = (CGlobalVarsRaw*)globals;

    /// <summary>Whether the shell has handed the engine's globals across yet.</summary>
    public bool IsAvailable => _p is not null;

    /// <summary>The raw pointer, for code that has to hand it back to native.</summary>
    public nint Pointer => (nint)_p;

    // ---- CGlobalVarsBase ---------------------------------------------------

    /// <summary>Absolute time, per frame. Not a high-precision clock.</summary>
    public float RealTime => _p->realtime;

    /// <summary>Absolute frame counter.</summary>
    public int FrameCount => _p->framecount;

    /// <summary>Non-paused frametime.</summary>
    public float AbsoluteFrameTime => _p->absoluteframetime;

    /// <summary>Current time. Means different things client-side; see globalvars_base.h.</summary>
    public float CurTime => _p->curtime;

    /// <summary>Time spent on the last server or client frame.</summary>
    public float FrameTime => _p->frametime;

    /// <summary>
    /// Sets <c>curtime</c> and <c>frametime</c>, as stock's
    /// <c>CPlayerMove::RunCommand</c> does for each user command
    /// (plan_server_native_v2 T4g: the server's command loop is managed). The
    /// engine's struct, written where a game DLL writes it; nothing else.
    /// </summary>
    public void SetClock(float curTime, float frameTime)
    {
        _p->curtime = curTime;
        _p->frametime = frameTime;
    }

    /// <summary>The current maxplayers setting.</summary>
    public int MaxClients => _p->maxClients;

    /// <summary>Simulation ticks.</summary>
    public int TickCount => _p->tickcount;

    /// <summary>Simulation tick interval.</summary>
    public float IntervalPerTick => _p->interval_per_tick;

    /// <summary>Client-only interpolation amount, a fraction of the next tick.</summary>
    public float InterpolationAmount => _p->interpolation_amount;

    /// <summary>Simulation ticks in this frame.</summary>
    public int SimTicksThisFrame => _p->simTicksThisFrame;

    /// <summary>The network protocol version.</summary>
    public int NetworkProtocol => _p->network_protocol;

    /// <summary>Which side these globals belong to. The engine sets it once.</summary>
    public bool IsClient => _p->m_bClient != 0;

    // ---- CGlobalVars -------------------------------------------------------

    /// <summary>The current map's version.</summary>
    public int MapVersion => _p->mapversion;

    /// <summary>How the current map was loaded. Written by the shell, never by managed code.</summary>
    public MapLoadType LoadType => (MapLoadType)_p->eLoadType;

    /// <summary>Whether the map failed to load and the game is going back to the menu.</summary>
    public bool MapLoadFailed => _p->bMapLoadFailed != 0;

    /// <summary>The deathmatch game flag.</summary>
    public bool Deathmatch => _p->deathmatch != 0;

    /// <summary>The co-op game flag.</summary>
    public bool Coop => _p->coop != 0;

    /// <summary>The teamplay game flag.</summary>
    public bool Teamplay => _p->teamplay != 0;

    /// <summary>The current max entity count.</summary>
    public int MaxEntities => _p->maxEntities;

    /// <summary>The server count, bumped on each level change.</summary>
    public int ServerCount => _p->serverCount;
}
