//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The world's record of what recently made a noise — <c>CSoundEnt</c>, in C#.
///
/// <para>
/// plan_gamedll_after.md Phase A3. It was row 14 of <c>kServerSystems[]</c> and
/// <c>game/server/soundent.cpp</c> was in <c>server_csharp.vpc</c>; neither is
/// true now. What the tree still spells — <c>CSoundEnt::InsertSound</c> and its
/// three siblings — is a shim (<c>shell_soundent.cpp</c>) forwarding here, so
/// the compiled native callers are unedited. <c>world_adapter.cpp</c>'s
/// <c>InitSoundEnt</c> in particular belongs to another lane and keeps its
/// spelling for exactly that reason.
/// </para>
///
/// <para>
/// <b>It is a list, not an entity, and it has not been one since Phase 7a.</b>
/// Stock spawned a <c>CPointEntity</c> with the world whose whole contribution
/// was a Think slot and a row in the entity list. The Think is
/// <see cref="IFrameLifecycle.OnPreThink"/> here, which is the same moment:
/// stock ran the sweep from <c>FrameUpdatePreEntityThink</c>, so a sound that
/// expired last frame is gone before anything listens for it this one.
/// </para>
///
/// <para>
/// <b>Nothing NATIVE reads a <c>CSound</c> any more.</b> That is what let
/// <c>soundent.cpp</c> leave the build rather than the pool being mirrored on
/// both sides. The two compiled readers were <c>gameinterface.cpp</c>'s
/// disconnect path — one <c>Reset()</c> on a leaving client's reserved slot,
/// which is <see cref="ResetClientSound"/> — and <c>shell_sound.cpp</c>'s
/// <c>SourceSharp_SoundEntQuery</c>, which existed only to hand the list to
/// managed code and is gone: <c>env_microphone</c> and <c>ai_sound</c> resolve
/// this service directly.
/// </para>
///
/// <para>
/// <b>Every rule below is stock's, including the odd ones.</b> The channel
/// reuse that updates a sound in place rather than allocating, the reserved
/// per-client slots that never expire, the "had an owner and lost it" test that
/// throws a sound away, and the pool sizing that differs between single- and
/// multiplayer are reproduced rather than tidied — they are what the AI and the
/// map entities were written against.
/// </para>
/// </summary>
public sealed class SoundEnt : IServerSoundEnt, IFrameLifecycle
{
    /// <summary>
    /// <c>MAX_WORLD_SOUNDS_SP</c>. Stock's comment says why the number is
    /// frozen: it is also the number of entries a savegame carried.
    /// </summary>
    public const int MaxWorldSoundsSp = 64;

    /// <summary><c>MAX_WORLD_SOUNDS_MP</c> — the pool's physical size.</summary>
    public const int MaxWorldSoundsMp = 128;

    /// <summary><c>SOUNDLIST_EMPTY</c>. The end of either list, and "no slot".</summary>
    public const int ListEmpty = -1;

    /// <summary>
    /// How often the active list is swept, in seconds. Stock's
    /// <c>SetNextThink( curtime + 0.1 )</c>, kept as an interval because a game
    /// system is called every frame and an unguarded sweep would run sixty
    /// times a second instead of ten.
    /// </summary>
    public const float SweepInterval = 0.1f;

    /// <summary>
    /// One slot of the pool. A struct in a fixed array, which is stock's
    /// <c>CSound m_SoundPool[MAX_WORLD_SOUNDS_MP]</c> — the indices ARE the
    /// identity here (<see cref="ActiveList"/> hands them out), so a slot that
    /// could be reallocated somewhere else would be a different data structure
    /// wearing the same interface.
    /// </summary>
    private struct Slot
    {
        public Vector Origin;
        public int Type;
        public int Volume;
        public float OcclusionScale;
        public float ExpireTime;
        public bool NoExpirationTime;
        public int Next;
        public EntityHandle Owner;
        public EntityHandle Target;
        public bool HasOwner;
        public int OwnerChannel;

        /// <summary><c>CSound::Clear</c>. Note it does NOT clear the owner.</summary>
        public void Clear()
        {
            Origin = default;
            Type = 0;
            Volume = 0;
            OcclusionScale = 0;
            ExpireTime = 0;
            NoExpirationTime = false;
            Next = ListEmpty;
        }

        /// <summary>
        /// <c>CSound::Reset</c> — clears the volume, origin and type but does
        /// NOT expire or unlink. Deliberately not <see cref="Clear"/>: the
        /// caller is a client leaving, and its reserved slot has to stay in the
        /// active list under its own index.
        /// </summary>
        public void Reset()
        {
            Origin = default;
            Type = 0;
            Volume = 0;
            Next = ListEmpty;
        }
    }

    private readonly IEngineConsole _console;

    /// <summary>
    /// Server time. <see cref="IGameClock"/> rather than a globals read, which
    /// is the tree's own abstraction for exactly this and is what makes the
    /// sweep's "has this expired" and the insert's "when does it expire"
    /// testable at a time the test chooses.
    /// </summary>
    private readonly IGameClock _clock;

    /// <summary>
    /// Whether an owner handle still resolves — <c>CSound::ValidateOwner</c>'s
    /// question.
    ///
    /// <para>
    /// <b>The INJECTED area, not <see cref="EntityHandle.IsValid"/>.</b> That
    /// property reaches the process-wide static <c>Engine</c>, which answers
    /// false to everything in a test host — so a fact asserting that a
    /// dead-owner noise is swept would have passed whatever this class did, and
    /// its opposite could not be written at all. docs/writing-tests.md's "no
    /// globals" and "the check that cannot fail", met in one edit.
    /// </para>
    /// </summary>
    private readonly IEngineEntities _entities;

    private readonly Slot[] _pool = new Slot[MaxWorldSoundsMp];

    private int _free = ListEmpty;
    private int _active = ListEmpty;
    private int _lastActiveSounds;
    private float _nextExpiry;

    /// <summary>
    /// <c>g_pSoundEnt != NULL</c> — the gate every static in stock tests first.
    ///
    /// <para>
    /// It is not the same question as "does the object exist". The list object
    /// exists from the moment the module is built; the POOL only exists between
    /// <see cref="Initialize"/> and <see cref="Shutdown"/>, and a sound inserted
    /// outside that window has nowhere to go. Stock spelled the distinction as
    /// a second pointer and this is that pointer.
    /// </para>
    /// </summary>
    private bool _up;

    public SoundEnt(IEngineConsole console, IGameClock clock, IEngineEntities entities)
    {
        _console = console;
        _clock = clock;
        _entities = entities;
    }

    // ---- placement ---------------------------------------------------------

    /// <summary>
    /// <c>CSoundEnt</c> was row 14 and overrode <c>FrameUpdatePreEntityThink</c>
    /// and nothing else (docs/gamedll-ledger.md's measured stage matrix).
    /// </summary>
    int IFrameLifecycle.FrameOrder => (int)FrameStage.SoundEnt;

    // IT IS NOT AN IGameLifecycle PARTICIPANT, and that is a decision rather
    // than an omission. <see cref="Initialize"/> and <see cref="Shutdown"/> are
    // reached BY NAME, from the world's spawn and from the level teardown --
    // the same two moments stock reached them from, through the same two
    // callers, which still spell CSoundEnt::InitSoundEnt and
    // CSoundEnt::ShutdownSoundEnt. soundent.cpp's own comment is the argument:
    // "Moving the work to the game system hooks as well would run it twice."
    //
    // A LevelShutdownPreEntity override here would not be harmless belt and
    // braces either: it would kill the pool EARLIER than stock does, and every
    // sound inserted between the two moments would go nowhere.

    // ---- the list itself ---------------------------------------------------

    /// <summary>Whether the pool is up: between init and shutdown.</summary>
    public bool IsUp => _up;

    /// <summary><c>CSoundEnt::ActiveList</c> — the head, or <see cref="ListEmpty"/>.</summary>
    public int ActiveList => _up ? _active : ListEmpty;

    /// <summary><c>CSoundEnt::FreeList</c> — the head, or <see cref="ListEmpty"/>.</summary>
    public int FreeList => _up ? _free : ListEmpty;

    /// <summary><c>CSoundEnt::IsEmpty</c>.</summary>
    public bool IsEmpty => _active == ListEmpty;

    /// <summary>
    /// <c>CSoundEnt::Initialize</c>: clear every slot, chain them into the free
    /// list, then reserve one per client that never expires.
    /// </summary>
    /// <param name="maxClients">
    /// <c>gpGlobals->maxClients</c>. A PARAMETER rather than a globals read
    /// because the shim already has it and because it is what decides the pool
    /// size — a service whose size depends on a value it reads itself cannot be
    /// tested at both sizes.
    /// </param>
    /// <param name="curTime">
    /// <c>gpGlobals->curtime</c>. Arms the first sweep a whole second out,
    /// which is what <c>InitSoundEnt</c>'s second half did and what the
    /// entity's first <c>SetNextThink</c> did before that. An argument for the
    /// same reason <paramref name="maxClients"/> is one.
    /// </param>
    public void Initialize(int maxClients, float curTime)
    {
        _nextExpiry = curTime + 1.0f;

        _free = 0;
        _active = ListEmpty;
        _lastActiveSounds = 0;

        // In SP only the first 64 slots are used so save/load works right; in MP
        // one per player plus 32 spare. Stock's comment and stock's arithmetic.
        int inPool = MaxWorldSoundsSp;
        if (maxClients > 1)
            inPool = Math.Min(MaxWorldSoundsMp, maxClients + 32);

        if (maxClients + 16 > inPool)
            _console.Warning("CSoundEnt pool is low on sounds due to high number of clients.\n");

        int i;
        for (i = 0; i < inPool; i++)
        {
            _pool[i].Clear();
            _pool[i].Next = i + 1;
        }

        _pool[i - 1].Next = ListEmpty;

        // The pool is up BEFORE the client reserve, because the reserve
        // allocates through the same path every later insert uses.
        _up = true;

        for (i = 0; i < maxClients; i++)
        {
            int slot = AllocSound();
            if (slot == ListEmpty)
            {
                _console.Msg("Could not AllocSound() for Client Reserve! (DLL)\n");
                return;
            }

            _pool[slot].NoExpirationTime = true;
        }
    }

    /// <summary>
    /// <c>CSoundEnt::ShutdownSoundEnt</c>. Stock calls <c>FreeList()</c> here
    /// and throws the answer away — the call is a no-op and the assignment of
    /// the pointer to NULL is the whole of it. Reproduced as what it MEANT: the
    /// pool is down, and every static answers as if there were none.
    /// </summary>
    public void Shutdown()
    {
        _up = false;
        _active = ListEmpty;
        _free = ListEmpty;
    }

    /// <summary>
    /// <c>CSoundEnt::IAllocSound</c> — move the head of the free list onto the
    /// head of the active list and hand back its index.
    /// </summary>
    public int AllocSound()
    {
        if (_free == ListEmpty)
        {
            if (Developer >= 2)
                _console.Msg("Free Sound List is full!\n");

            return ListEmpty;
        }

        int slot = _free;

        _free = _pool[_free].Next;
        _pool[slot].Next = _active;
        _active = slot;

        return slot;
    }

    /// <summary>
    /// <c>CSoundEnt::FreeSound</c> — unlink an ACTIVE sound and push it onto the
    /// free list. <paramref name="previous"/> is the caller's cursor, which is
    /// why this is not a search: the sweep already knows where it is.
    /// </summary>
    public void FreeSound(int sound, int previous)
    {
        if (!_up)
            return;

        if (previous != ListEmpty)
            _pool[previous].Next = _pool[sound].Next;
        else
            _active = _pool[sound].Next;

        _pool[sound].Next = _free;
        _free = sound;
    }

    /// <summary>
    /// <c>CSoundEnt::FindOrAllocateSound</c>: a channel that is already speaking
    /// for this owner is UPDATED rather than added to, which is what keeps a
    /// repeating noise from filling the pool.
    /// </summary>
    public int FindOrAllocateSound(EntityHandle owner, int channel)
    {
        int sound = _active;

        while (sound != ListEmpty)
        {
            if (_pool[sound].OwnerChannel == channel && _pool[sound].Owner.Equals(owner))
                return sound;

            sound = _pool[sound].Next;
        }

        return AllocSound();
    }

    /// <summary><c>CSoundEnt::InsertSound</c>.</summary>
    public void Insert(int type, Vector origin, int volume, float duration,
                       EntityHandle owner = default, int channel = 0,
                       EntityHandle target = default)
    {
        if (!_up)
            return;

        int slot = channel == SoundChannel.Unspecified
            ? AllocSound()
            : FindOrAllocateSound(owner, channel);

        if (slot == ListEmpty)
        {
            _console.Msg("Could not AllocSound() for InsertSound() (Game DLL)\n");
            return;
        }

        float now = CurTime;

        ref Slot s = ref _pool[slot];
        s.Origin = origin;
        s.Type = type;
        s.Volume = volume;
        s.OcclusionScale = 0.5f;
        s.ExpireTime = now + duration;
        s.NoExpirationTime = false;
        s.Owner = owner;
        s.Target = target;
        s.OwnerChannel = channel;

        // "Keep track of whether this sound had an owner when it was made" --
        // a long sound can outlive its owner, and a slot that HAD one and no
        // longer resolves is thrown away by the sweep rather than reported as a
        // world noise. An unset handle is EntityHandle.None (which an omitted
        // owner is), which never resolves, so the flag is the only thing that
        // tells the two apart. Stock asks m_hOwner.Get() != NULL, so the world
        // named as an owner counts as one.
        s.HasOwner = owner != EntityHandle.None;

        int show = DisplaySoundList;
        if (show == 1)
        {
            _console.Msg($"  Added Sound! Type:{s.Type}  Duration:{duration:F6} (Time:{now:F6})\n");
        }

        if (show == 2 && (type & SoundFlags.Danger) != 0)
        {
            _console.Msg($"  Added Danger Sound! Duration:{duration:F6} (Time:{now:F6})\n");
        }
    }

    /// <summary>
    /// <c>CSoundEnt::ClientSoundIndex</c>. Clients are 1..maxClients and their
    /// reserved slots are 0..maxClients-1, so the index is one less.
    /// </summary>
    public static int ClientSoundIndex(int entIndex) => entIndex - 1;

    /// <summary>
    /// <c>CSound::Reset</c> on a client's reserved slot — what
    /// <c>gameinterface.cpp</c>'s disconnect path did through a
    /// <c>CSound *</c>. Out-of-range is silently nothing, which is what
    /// <c>SoundPointerForIndex</c>'s NULL answer already meant there.
    /// </summary>
    public void ResetClientSound(int index)
    {
        if (!_up || index < 0 || index >= MaxWorldSoundsMp)
            return;

        _pool[index].Reset();
    }

    /// <summary>
    /// <c>CSoundEnt::ISoundsInList</c>, for the active list.
    /// </summary>
    public int ActiveCount => CountFrom(_active);

    /// <summary><c>CSoundEnt::ISoundsInList</c>, for the free list.</summary>
    public int FreeCount => CountFrom(_free);

    private int CountFrom(int head)
    {
        int n = 0;
        while (head != ListEmpty)
        {
            n++;
            head = _pool[head].Next;
        }

        return n;
    }

    /// <summary>
    /// Every active sound matching <paramref name="typeMask"/>, oldest link
    /// order preserved. <c>SourceSharp_SoundEntQuery</c>'s answer, without the
    /// boundary crossing it used to need.
    /// </summary>
    /// <returns>How many entries were written.</returns>
    public int Query(SoundType typeMask, Span<HeardSound> into)
    {
        if (!_up || typeMask == SoundType.None || into.Length == 0)
            return 0;

        int written = 0;
        int sound = _active;

        while (sound != ListEmpty && written < into.Length)
        {
            ref Slot s = ref _pool[sound];
            sound = s.Next;

            if (((int)typeMask & s.Type) == 0)
                continue;

            into[written] = new HeardSound
            {
                Origin = s.Origin,
                Type = (SoundType)s.Type,
                Volume = s.Volume,

                // The handle, not a resolved entity. shell_sound.cpp's own note
                // is the reason and it still holds: a sound made by a managed
                // entity resolves to no CBaseEntity, so a listen filter asking
                // "who made this" saw a world noise. The handle is the one
                // identity both hierarchies share.
                Owner = s.Owner,
            };

            written++;
        }

        return written;
    }

    /// <summary>
    /// <c>CSoundEnt::GetLoudestSoundOfType</c> — the nearest sound of exactly
    /// this type whose volume still reaches <paramref name="ear"/>.
    ///
    /// <para>
    /// <b>An EXACT type match, not a mask</b>, which is stock's
    /// <c>pSound->m_iType == iType</c> and reads as a bug next to
    /// <see cref="Query"/>'s mask. It is not this port's to fix: a caller
    /// asking for <c>SOUND_COMBAT</c> deliberately does not want the one that
    /// also carries <c>SOUND_CONTEXT_GUNFIRE</c>.
    /// </para>
    /// </summary>
    /// <returns>The pool index, or <see cref="ListEmpty"/>.</returns>
    public int LoudestOfType(int type, Vector ear)
    {
        int best = ListEmpty;

        // MAX_COORD_RANGE squared, so the first sound in range wins outright.
        float bestDist = 16384.0f * 16384.0f;
        int sound = ActiveList;

        while (sound != ListEmpty)
        {
            ref Slot s = ref _pool[sound];

            if (s.Type == type && ValidateOwner(in s))
            {
                float dist = (s.Origin - ear).Length;

                if (dist <= s.Volume && dist < bestDist)
                {
                    best = sound;
                    bestDist = dist;
                }
            }

            sound = s.Next;
        }

        return best;
    }

    // ---- the sweep ---------------------------------------------------------

    /// <summary>
    /// <c>CSoundEnt::FrameUpdatePreEntityThink</c>: at interval, drop every
    /// active sound that has expired or whose owner has gone.
    /// </summary>
    void IFrameLifecycle.OnPreThink(float frameTime)
    {
        if (!_up)
            return;

        float now = CurTime;
        if (now < _nextExpiry)
            return;

        _nextExpiry = now + SweepInterval;

        int show = DisplaySoundList;
        int previous = ListEmpty;
        int sound = _active;

        while (sound != ListEmpty)
        {
            ref Slot s = ref _pool[sound];

            if ((s.ExpireTime <= now && !s.NoExpirationTime) || !ValidateOwner(in s))
            {
                int next = s.Next;

                if (show == 1)
                    _console.Msg($"  Removed Sound: {s.Type} (Time:{now:F6})\n");

                if (show == 2 && (s.Type & SoundFlags.Danger) != 0)
                    _console.Msg($"  Removed Danger Sound: {s.Type} (time:{now:F6})\n");

                FreeSound(sound, previous);
                sound = next;
            }
            else
            {
                if (show != 0)
                    ReportSurvivor();

                previous = sound;
                sound = _pool[sound].Next;
            }
        }
    }

    /// <summary>
    /// <c>displaysoundlist</c>'s counts, printed once per SURVIVING sound.
    ///
    /// <para>
    /// <b>Per sound, not per sweep, and that is stock's.</b> The <c>DevMsg</c>
    /// sits inside the loop's else branch, so a sweep over five live sounds
    /// prints five lines and the delta is zero on four of them — the counter it
    /// subtracts from is updated on every one. Reproduced rather than tidied,
    /// because a developer reading this convar's output against a stock build
    /// is comparing line counts.
    /// </para>
    ///
    /// <para>
    /// <b>WHAT IS NOT REPRODUCED, and it is the only thing.</b> Stock also draws
    /// three axis-aligned crosses per sound through <c>NDebugOverlay::Line</c>,
    /// sized by the sound's volume, plus a second cross for the occluded volume.
    /// There is no managed debug-overlay engine area — <c>IEngineEffects</c> has
    /// beams and blood and no line — so the picture is gone and the numbers are
    /// not. <b>BLOCKER: a managed <c>NDebugOverlay</c> area (one slot,
    /// <c>AddLineOverlay</c>).</b> It is named here, in
    /// <c>shell_soundent.cpp</c> and in docs/gamedll-ledger.md rather than only
    /// in a table, because this tree's Phase 9 finding is that a verdict which
    /// lives only in a table is the one the next reader never sees.
    /// </para>
    /// </summary>
    private void ReportSurvivor()
    {
        // DevMsg( 2, ... ) -- stock's level, so it is silent below developer 2
        // even with the convar on.
        if (Developer < 2)
        {
            // The counter is still advanced: stock updates it on the line below
            // the DevMsg, and DevMsg's level test is inside DevMsg.
            _lastActiveSounds = ActiveCount;
            return;
        }

        int active = ActiveCount;
        _console.Msg($"Soundlist: {active} / {FreeCount}  ({active - _lastActiveSounds})\n");
        _lastActiveSounds = active;
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// <c>CSound::ValidateOwner</c>: valid if the sound never had an owner, or
    /// if the one it had still resolves.
    /// </summary>
    private bool ValidateOwner(in Slot s) => !s.HasOwner || _entities.IsValid(s.Owner);

    private float CurTime => _clock.Now;

    /// <summary>
    /// <c>displaysoundlist</c>, read once per sweep rather than once per sound.
    ///
    /// <para>
    /// Stock evaluates it four times inside the loop; a convar cannot change
    /// between two lines of one sweep, so one read is the same behaviour and is
    /// one name lookup rather than four per sound.
    /// </para>
    /// </summary>
    private int DisplaySoundList => IntConVar("displaysoundlist");

    private int Developer => IntConVar("developer");

    private int IntConVar(string name)
    {
        string? value = _console.FindConVarValue(name);
        return value is not null && int.TryParse(value, out int n) ? n : 0;
    }
}
