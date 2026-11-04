using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The local player's <c>tf_player_data</c>, predicted: a participant of the
/// core's client replay (<see cref="IClientPredictedState"/>) over a WORKING
/// COPY of the carrier's <see cref="TFPlayerNetState"/>, on ClientArsenal's
/// pattern. Once a frame the copy goes back to the decoded state (the
/// server's word) and, when a fresh decode arrived, the acknowledged
/// command's saved slot is judged against it; after each replayed command
/// the copy is saved under the command's number in a
/// <see cref="PredictionRing"/>.
///
/// <para>
/// The client's <see cref="ITFPlayerData"/>: a movement rule or a weapon
/// writes the working copy, and only for the local player. Whatever writes it
/// outside a replayed command (the native movement forwarder, a read between
/// frames) is undone by the next frame's reset, never reaches the decoded
/// state, and reaches the ring only through a replayed command's save: the
/// working copy is the one thing a write can touch.
/// </para>
///
/// <para>
/// <see cref="TFPlayerNetState.CritMult"/> is sent and not predicted (stock's
/// rule), so the compare takes it from the arrival: the server changing it is
/// news, not a misprediction.
/// </para>
///
/// <para>
/// The condition words news the same way, one arm later: the expiry the
/// server's condition machine runs between two decodes
/// (<c>ConditionGameRulesThink</c>, tf_player_shared.cpp:2334-2375, is
/// GAME_DLL and the durations ride no packet) is a write no replay can have
/// made, so when an arrival's words move, their bytes are the arrival's —
/// the class change's news, word-sized.
/// </para>
///
/// <para>
/// The spy's cloak is predicted here too, because stock's client runs it
/// inside the prediction as well: the local player's <c>PreThink</c> per
/// replayed command reaches <c>UpdateTimers</c> → <c>SharedThink</c>
/// (c_tf_player.cpp:6149, 5936-5943), whose client arm is the local player's
/// <c>ConditionThink</c> → <c>UpdateCloakMeter</c>
/// (tf_player_shared.cpp:3123, 14190-14198) — the drain while stealthed, the
/// refill to 100 while not, and the meter-out <c>FadeInvis</c>. The toggle's
/// state half (<c>ActivateInvisibilityWatch</c>,
/// tf_weapon_invis.cpp:206-263) rides the weapon machine's class skill, now
/// on every pass: the server's run and every replay of the command must
/// write the same state. The watch's cooldown
/// (<c>m_flStealthNextChangeTime</c>) is component state on the server and
/// rides no packet, so each basis re-derives it from the ring — the last
/// saved toggle is the last pair of slots whose stealth bit moved.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFPlayerData)])]
public sealed class TFPlayerPrediction : IClientPredictedState, IOrderedHook, ITFPlayerData
{
    /// <summary>How many mispredictions are logged before the log goes quiet; the tally keeps counting.</summary>
    public const int QuietAfter = 3;

    private readonly ILog _log;
    private readonly TFClientCarriers _carriers;
    private readonly NetStateCatalog _catalog;
    private readonly IClientStandalones _standalones;
    private readonly IEngineEnvironment _environment;
    private readonly TFSpyCloakConsumeRateConVar _cloakConsume;
    private readonly TFSpyCloakRegenRateConVar _cloakRegen;
    private readonly TFSpyInvisTimeConVar _invisTime;
    private readonly TFSpyInvisUnstealthTimeConVar _invisUnstealth;

    private readonly TFPlayerNetState _working = new();
    private readonly PredictionRing _ring = new();
    private byte[] _scratch = Array.Empty<byte>();

    // The sent-not-predicted fields, as (offset, bytes, bit shift, bits); made
    // on the first compare.
    private (int Offset, int Bytes, int Shift, int Bits, bool Packed)[]? _unpredicted;

    private ClientTFPlayerData? _local;
    private int _localIndex = -1;
    private bool _hasWorking;
    private EntityHandle _lastComparedCarrier = EntityHandle.None;
    private int _lastComparedDecode = -1;

    private int _saves, _holds, _tolerated, _mispredicts, _noBasis, _classNews, _conditionNews;
    private int _lastArrivedClass = -1;
    private TFCondMask _lastArrivedConditions;
    private bool _seenArrivedConditions;
    private int _lastMissCommand = -1;
    private string _lastMissFields = "";

    // The cloak's replay clock: the basis's TickBase — the tick the window's
    // first command runs at, the same number the core's CurTime carries —
    // counted in commands since the anchor; the watch's cooldown, re-derived
    // at every basis; and the stealth bit's place in the image, made once.
    private float _cloakAnchorTick;
    private int _cloakCommands;
    private float _stealthNextChangeTime;
    private (int Offset, byte Mask)? _stealthBit;

    // The five condition words' spans in the image, for the news overlay; made
    // on the first compare, as the unpredicted spans are.
    private (int Offset, int Bytes)[]? _conditionWords;

    public TFPlayerPrediction(ILog log, TFClientCarriers carriers, NetStateCatalog catalog,
                              IClientStandalones standalones, IEngineEnvironment environment,
                              TFSpyCloakConsumeRateConVar cloakConsume, TFSpyCloakRegenRateConVar cloakRegen,
                              TFSpyInvisTimeConVar invisTime, TFSpyInvisUnstealthTimeConVar invisUnstealth)
    {
        _log = log;
        _carriers = carriers;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _standalones = standalones;
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _cloakConsume = cloakConsume;
        _cloakRegen = cloakRegen;
        _invisTime = invisTime;
        _invisUnstealth = invisUnstealth;
        _working.BindLocal();
    }

    /// <summary>A stage of its own in TF2's thousand, below the mods' (ModStages.First).</summary>
    public const int Order = 1_000_000 - 1_000 + 80;

    public int HookOrder => Order;

    //-------------------------------------------------------------------------
    // ITFPlayerData.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public TFPlayerNetState? Predicted(int playerIndex)
        => _hasWorking && playerIndex == _localIndex ? _working : null;

    /// <inheritdoc/>
    public TFPlayerNetState? Networked(int playerIndex) => Find(playerIndex)?.State;

    //-------------------------------------------------------------------------
    // IClientPredictedState.
    //-------------------------------------------------------------------------

    public void ResetBasis(EntityHandle player, int acknowledged)
    {
        // Players hold 1..maxplayers identically on both sides; the low bits
        // of a client handle are its entity-list entry.
        _localIndex = (int)(player.Value & 0xFFFF);
        _local = Find(_localIndex);

        if (_local is null)
        {
            _hasWorking = false;
            return;
        }

        if (_local.Handle != _lastComparedCarrier || _local.Decodes != _lastComparedDecode)
        {
            _lastComparedCarrier = _local.Handle;
            _lastComparedDecode = _local.Decodes;
            Compare(acknowledged);
        }

        byte[] basis = _local.State.Snapshot();
        if (_ring.BlobBytes != basis.Length)
        {
            _ring.Init(basis.Length);
            _scratch = new byte[basis.Length];
        }

        if (basis.Length > 0)
            _working.Decode(basis);
        _hasWorking = true;

        // The replay's clock, anchored where the core's is: the basis's TickBase
        // is the tick the window's first command runs at, and the entity's state
        // is the state that basis was decoded from. The watch's cooldown, whose
        // server half is component state, re-derives from the ring now too.
        _cloakAnchorTick = _standalones.GetByIndex(_localIndex) is ClientSsPlayer local
            ? local.State.TickBase
            : 0f;
        _cloakCommands = 0;
        StealthDebounceFromRing(acknowledged);
    }

    public void CommandRan(EntityHandle player, int commandNumber, bool firstTime)
    {
        if (!_hasWorking || (int)(player.Value & 0xFFFF) != _localIndex)
            return;

        // The cloak, before the save: per command, at the command's own clock —
        // the anchor plus this command's place in the window, the number the
        // core's CurTime carries for the same command. The server's drain is
        // the component stage after the command's attack, so the drain/fill
        // choice is the bit as this command's own attack leaves it: the cloak
        // command drains, its successor... also drains, and the decloak
        // command fills — the same side of the toggle both places.
        float now = (_cloakAnchorTick + _cloakCommands) * Interval;
        _cloakCommands++;
        CloakTick(now);

        if (_scratch.Length > 0 && _working.CopyTo(_scratch) == _scratch.Length
            && _ring.SaveCommand(commandNumber, _scratch))
        {
            _saves++;
        }
    }

    //-------------------------------------------------------------------------
    // The compare.
    //-------------------------------------------------------------------------

    private void Compare(int acknowledged)
    {
        // Recorded before the early returns: a compare that finds no basis
        // (the first after a join) still names the class its next one changes
        // from. A class change is the server's reset (stock's ChangeTeam →
        // Spawn): the spawn rewrote the shared's fields outside any command —
        // the class, a full watch — and no replay can have run it, so the
        // command's saved run is news, not a misprediction. The engine
        // block's overlay speaks the same word for the fields the engine owns.
        int arrivedClass = _local!.State.PlayerClass;
        bool classChanged = _lastArrivedClass >= 0 && arrivedClass != _lastArrivedClass;
        _lastArrivedClass = arrivedClass;

        // The condition machine's writes, the same bookkeeping: the arrival's
        // words against the last arrival's.
        TFCondMask arrivedConditions = _local.State.Conditions();
        bool conditionsMoved = _seenArrivedConditions && arrivedConditions != _lastArrivedConditions;
        _seenArrivedConditions = true;
        _lastArrivedConditions = arrivedConditions;

        ReadOnlySpan<byte> predicted = _ring.CommandBytes(acknowledged);
        if (predicted.IsEmpty)
        {
            _noBasis++;
            return;
        }

        byte[] arrived = _local.State.Snapshot();
        if (arrived.Length != predicted.Length)
        {
            _noBasis++;
            return;
        }

        if (classChanged)
        {
            _classNews++;
            return;
        }

        // The engine block in front of the declared fields is the server's
        // (ClientArsenal's reason), and so are the sent-not-predicted fields.
        byte[] judged = PredictionJudge.OverlayDeclared(predicted, arrived,
            PredictionJudge.DeclaredStart(typeof(TFPlayerNetState), arrived.Length));
        OverlayUnpredicted(judged, arrived);

        // The expiry the server's condition machine ran between the decodes
        // (ConditionGameRulesThink is GAME_DLL, tf_player_shared.cpp:2334-2375;
        // no client tick mirrors it and the durations ride no packet): the
        // ring's words are stale by exactly that write, the compare's news as
        // the class change is. Everything outside the words still judges.
        if (conditionsMoved && OverlayConditionWords(judged, arrived))
            _conditionNews++;

        if (judged.AsSpan().SequenceEqual(arrived))
        {
            _holds++;
            return;
        }

        int standing = PredictionJudge.Judge(_catalog, NetSchema.TypeIdOf(TFEntityNames.PlayerData),
            judged, arrived, out string fields, out _);

        if (standing == 0)
        {
            _holds++;
            _tolerated++;
            return;
        }

        _mispredicts++;
        _lastMissCommand = acknowledged;
        _lastMissFields = fields.Length > 0 ? fields : "(unnamed)";

        if (_mispredicts <= QuietAfter)
        {
            _log.Msg($"[TF2] player data prediction: MISPREDICTION at command {acknowledged} — " +
                     $"{standing} byte(s) stand: {_lastMissFields}" +
                     (_mispredicts == QuietAfter ? " (quiet from here; the tally keeps counting)" : "") + "\n");
        }
    }

    private void OverlayUnpredicted(Span<byte> judged, ReadOnlySpan<byte> arrived)
    {
        _unpredicted ??= Unpredicted();

        foreach (var (offset, bytes, shift, bits, packed) in _unpredicted)
        {
            if (offset < 0 || offset + bytes > judged.Length)
                continue;

            if (!packed)
            {
                arrived.Slice(offset, bytes).CopyTo(judged.Slice(offset, bytes));
                continue;
            }

            for (int bit = shift; bit < shift + bits; bit++)
            {
                int at = offset + bit / 8;
                byte mask = (byte)(1 << (bit % 8));
                judged[at] = (byte)((judged[at] & ~mask) | (arrived[at] & mask));
            }
        }
    }

    private static (int, int, int, int, bool)[] Unpredicted()
    {
        var list = new List<(int, int, int, int, bool)>();
        foreach (var field in StandaloneNetStates.Of(typeof(TFPlayerNetState)).Fields)
        {
            var property = typeof(TFPlayerNetState).GetProperty(field.Name);

            // A generated array's row ("Ammo.1") is no property of its own:
            // the sent-not-predicted question is the array's, so the parent's
            // attribute answers for every element.
            if (property is null && field.Name.IndexOf('.') is int dot and > 0)
                property = typeof(TFPlayerNetState).GetProperty(field.Name[..dot]);

            if (property is null || property.GetCustomAttribute<PredictedAttribute>() is not null)
                continue;

            list.Add((field.Offset, field.Bytes, field.BitShift, field.Bits, field.IsPacked));
        }

        return list.ToArray();
    }

    /// <summary>The arrival's word bytes over the prediction's; true when a byte moved.</summary>
    private bool OverlayConditionWords(Span<byte> judged, ReadOnlySpan<byte> arrived)
    {
        _conditionWords ??= ConditionWordSpans();
        bool changed = false;

        foreach (var (offset, bytes) in _conditionWords)
        {
            if (offset < 0 || offset + bytes > judged.Length)
                continue;

            for (int b = 0; b < bytes; b++)
            {
                changed |= judged[offset + b] != arrived[offset + b];
                judged[offset + b] = arrived[offset + b];
            }
        }

        return changed;
    }

    /// <summary>The five condition words' spans, from the same declaration the compare judges.</summary>
    private static (int Offset, int Bytes)[] ConditionWordSpans()
    {
        ReadOnlySpan<string> names = ["PlayerCond", "PlayerCondEx", "PlayerCondEx2", "PlayerCondEx3", "PlayerCondEx4"];
        var spans = new List<(int, int)>();

        foreach (var field in StandaloneNetStates.Of(typeof(TFPlayerNetState)).Fields)
        {
            if (names.Contains(field.Name))
                spans.Add((field.Offset, field.Bytes));
        }

        return [.. spans];
    }

    //-------------------------------------------------------------------------
    // The cloak: the state half of stock's UpdateCloakMeter, FadeInvis and
    // ActivateInvisibilityWatch — the visuals gate on EffectsLive in their
    // own arms; these writes are what the replay must reproduce.
    //-------------------------------------------------------------------------

    /// <summary>The watch refuses to engage at 8% or under (tf_weapon_invis.cpp:243).</summary>
    private const float CloakStartMeter = 8f;

    /// <summary>
    /// The replay's second: the interval the core's own per-command clock ticks in
    /// (<c>shell_movement.cpp</c> FillConstants reads the same global), which is
    /// also the server's per-tick frametime — the tick-locked clock pays exactly
    /// one interval per command.
    /// </summary>
    private float Interval => _environment.Globals.IsAvailable ? _environment.Globals.IntervalPerTick : 0f;

    /// <summary>
    /// The meter's tick, the client arm of <c>ConditionThink</c>
    /// (tf_player_shared.cpp:14190-14198): the drain while stealthed — no
    /// clamp; the fade below owns the bottom — and the refill, clamped at 100,
    /// while not. A non-spy loses the bit (InvisibilityThink's safety net,
    /// tf_player.cpp:7977's else arm).
    /// </summary>
    private void CloakTick(float now)
    {
        float dt = Interval;

        if (_working.Class() != TFClass.Spy)
        {
            if (_working.InCond(TFCond.Stealthed))
                CloakClearStealth();
            return;
        }

        if (_working.InCond(TFCond.Stealthed))
        {
            _working.CloakMeter -= dt * _cloakConsume.Value;
            if (_working.CloakMeter <= 0f)
                CloakFade(1f, now);
        }
        else
        {
            _working.CloakMeter = MathF.Min(100f, _working.CloakMeter + dt * _cloakRegen.Value);
        }
    }

    /// <summary>
    /// The fade's state half (<c>FadeInvis</c>, tf_player_shared.cpp:7924-7972):
    /// the bit goes and the change completes after the unstealth time. The
    /// decloak-rate attribute rides the items, which the replay wears none of
    /// yet, and NoAttackExpire is sent-not-predicted — the arrival's word.
    /// </summary>
    private void CloakFade(float rateScale, float now)
    {
        if (_working.InCond(TFCond.Stealthed))
            CloakClearStealth();

        _working.InvisChangeCompleteTime = now + rateScale * _invisUnstealth.Value;
    }

    /// <summary>
    /// The watch's state half (<c>ActivateInvisibilityWatch</c>,
    /// tf_weapon_invis.cpp:206-263) for <paramref name="ownerIndex"/>'s
    /// secondary: the fade out when stealthed, the cloak on over 8%, and the
    /// next-change debounce either way — 0.5 s on a skill, 0.1 s without
    /// (:253-260). CanGoInvisible's flag and winner gates are the server's
    /// world (documented divergence, the flags lane's).
    /// </summary>
    internal bool CloakActivate(int ownerIndex, float now)
    {
        if (Predicted(ownerIndex) is null || _working.Class() != TFClass.Spy)
            return false;

        if (_working.InCond(TFCond.Taunting))   // DoClassSpecialSkill's own gate, tf_player_shared.cpp:12338
            return false;

        if (now < _stealthNextChangeTime)       // its debounce, :12340
            return false;

        bool cloaked = false;
        if (_working.InCond(TFCond.Stealthed))
        {
            CloakFade(1f, now);
        }
        else if (_working.CloakMeter > CloakStartMeter)
        {
            _working.SetConditions(_working.Conditions() | TFCondMask.Of(TFCond.Stealthed));
            // The cond's arrival stamps the change too
            // (tf_player_shared.cpp:6993-6999, the local player's arm).
            _working.InvisChangeCompleteTime = now + _invisTime.Value;
            cloaked = true;
        }

        _stealthNextChangeTime = now + (cloaked ? 0.5f : 0.1f);
        return cloaked;
    }

    private void CloakClearStealth() => _working.SetConditions(_working.Conditions() & ~TFCondMask.Of(TFCond.Stealthed));

    /// <summary>
    /// The watch's cooldown is server component state and rides no packet, so
    /// each basis re-derives it from the ring: the last pair of saved slots
    /// whose stealth bit moved is the last toggle, whose command ran one tick
    /// before the later slot's — the stamp carries that tick's share of the
    /// 0.5/0.1 s window, one tick conservative when the toggle landed in the
    /// earlier of the pair.
    /// </summary>
    private void StealthDebounceFromRing(int acknowledged)
    {
        _stealthNextChangeTime = 0f;
        if (_ring.BlobBytes == 0)
            return;

        for (int command = acknowledged; command > acknowledged - PredictionRing.Slots; command--)
        {
            ReadOnlySpan<byte> after = _ring.CommandBytes(command);
            ReadOnlySpan<byte> before = _ring.CommandBytes(command - 1);
            if (after.IsEmpty || before.IsEmpty || StealthBitAt(after) == StealthBitAt(before))
                continue;

            _stealthNextChangeTime = TickOf(command, acknowledged) + (StealthBitAt(after) ? 0.5f : 0.1f);
            return;
        }
    }

    /// <summary>The tick <paramref name="command"/> ran at: the anchor counts from the window's first, the acknowledged + 1st.</summary>
    private float TickOf(int command, int acknowledged) => (_cloakAnchorTick - (acknowledged + 1 - command)) * Interval;

    private bool StealthBitAt(ReadOnlySpan<byte> image)
    {
        var (offset, mask) = (_stealthBit ??= LocateStealthBit());
        return offset < image.Length && (image[offset] & mask) != 0;
    }

    /// <summary>PlayerCond's TF_COND_STEALTHED bit: the word's offset and the bit's mask.</summary>
    private static (int Offset, byte Mask) LocateStealthBit()
    {
        int bit = (int)TFCond.Stealthed;
        foreach (var field in StandaloneNetStates.Of(typeof(TFPlayerNetState)).Fields)
        {
            if (field.Name == "PlayerCond")
                return (field.Offset + bit / 8, (byte)(1 << (bit % 8)));
        }

        return (int.MaxValue, 0);
    }

    //-------------------------------------------------------------------------
    // Bookkeeping.
    //-------------------------------------------------------------------------

    // Re-resolved per ask: a carrier can arrive, leave or be remade between
    // any two frames (ClientArsenal's FindLocalPair).
    private ClientTFPlayerData? Find(int playerIndex) => _carriers.Of(playerIndex);

    /// <summary>The meter, one line (and the last miss when there was one).</summary>
    public string Report()
    {
        string line =
            $"[TF2] player data prediction: carrier {(_local is not null ? "present" : "absent")} " +
            $"(player {_localIndex}), {_ring.BlobBytes} byte(s) ringed — {_saves} save(s), " +
            $"{_holds} hold(s) ({_tolerated} tolerated), {_mispredicts} misprediction(s), " +
            $"{_noBasis} no-basis, {_classNews} class news, {_conditionNews} condition news";

        if (_lastMissCommand >= 0)
            line += $"\n  last mismatch: command {_lastMissCommand}: {_lastMissFields}";

        return line;
    }

    /// <summary>The meter's counts, for facts.</summary>
    public (int Saves, int Holds, int Tolerated, int Mispredicts, int NoBasis, int ClassNews, int ConditionNews) Counts
        => (_saves, _holds, _tolerated, _mispredicts, _noBasis, _classNews, _conditionNews);
}

/// <summary>The console door onto the meter, beside cs_cl_weapon_predict_report.</summary>
[ConCommand("tf2_cl_player_data_report", Help = "What the tf_player_data prediction ring has seen")]
public sealed class TFPlayerDataReportCommand(ILog log, TFPlayerPrediction prediction)
{
    public void Execute() => log.Msg(prediction.Report() + "\n");
}
