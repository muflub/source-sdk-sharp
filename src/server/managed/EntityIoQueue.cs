//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// THE I/O EVENT QUEUE — <c>CEventQueue</c> (<c>cbase.cpp:813-1350</c>) in
/// managed code, and <c>plan_entity.md</c> §3.5's "the I/O queue: … delays are
/// queued here".
///
/// <para>
/// <b>WHY IT MOVED, IN CROSSINGS.</b> An output fired by a C# entity used to
/// leave managed code (<c>FireScriptedOutput</c>), be parsed into a native
/// event, wait in <c>g_EventQueue</c>, and come back in
/// (<c>StandaloneEntity_DeliverInput</c> → <c>AcceptInput</c> →
/// <c>ENTITY_OP_INPUT</c>) to reach a C# entity. Two crossings to carry a
/// message from one managed object to another. With the queue here, a managed
/// output reaching a managed input costs none.
/// </para>
///
/// <para>
/// <b>ORDERING IS STOCK'S, INCLUDING THE PART THAT LOOKS LIKE AN ACCIDENT.</b>
/// <c>ServiceEvents</c> restarts from the head of the list after every delivery
/// (<c>cbase.cpp:1329</c>, "restart the list (to catch any new items…)"), so an
/// event posted with NO delay from inside a delivery is serviced in the SAME
/// pass, after the events already due. That is what makes a chain of
/// zero-delay relays resolve within one frame instead of one link per frame,
/// and a great many maps depend on it without saying so.
/// </para>
///
/// <para>
/// <b>AND THE PART THAT IS NOT STOCK'S: THE CHAIN IS BOUNDED.</b> Two entities
/// firing each other with no delay is an infinite loop in stock — the queue is
/// never empty and <c>curtime</c> never advances — and it takes the server down
/// with no message. <see cref="MaxChainPerPass"/> stops it, once, with the
/// names of the last delivery. The cap is far past any real map, so nothing a
/// mapper would write can reach it; what it changes is a hang into a
/// diagnosis. Stated as a divergence rather than left to be discovered.
/// </para>
/// </summary>
public sealed class EntityIoQueue(ILog log)
{
    /// <summary>
    /// How many deliveries one <see cref="Service"/> pass will make before it
    /// declares a cycle. See the type's remarks for why this exists and why the
    /// number is this large.
    /// </summary>
    public const int MaxChainPerPass = 4096;

    private readonly List<EntityIoEvent> _events = new();
    private long _sequence;

    /// <summary>
    /// The <c>curtime</c> the last <see cref="Service"/> pass ran at, or
    /// negative infinity before the first one.
    /// </summary>
    /// <remarks>
    /// It exists for <see cref="PostedAfterThePass"/>, and it is the whole
    /// mechanism behind that number: an event posted at a time this queue has
    /// ALREADY serviced is an event that missed its own frame.
    /// </remarks>
    private float _lastServicedTime = float.NegativeInfinity;

    /// <summary>How many events are waiting.</summary>
    public int Count => _events.Count;

    /// <summary>The events, in delivery order. For a report and for the reload record.</summary>
    public IReadOnlyList<EntityIoEvent> Pending => _events;

    /// <summary>Whether a <see cref="Service"/> pass is running right now.</summary>
    public bool Servicing { get; private set; }

    /// <summary>
    /// How many deliveries the last <see cref="Service"/> pass made, and how
    /// many this queue has made in its life. Instrumentation, in the same sense
    /// and for the same reason as <see cref="EntityDirectory.ThinksRanLastFrame"/>:
    /// "the queue costs no crossings now" is trivially true of a queue that
    /// stopped delivering, and nothing else can tell the two apart.
    /// </summary>
    public int DeliveredLastPass { get; private set; }

    /// <inheritdoc cref="DeliveredLastPass"/>
    public long DeliveredTotal { get; private set; }

    /// <summary>How many times a pass has hit <see cref="MaxChainPerPass"/>.</summary>
    public int CyclesRefused { get; private set; }

    /// <summary>
    /// HOW MANY EVENTS WERE POSTED TOO LATE TO BE DELIVERED IN THEIR OWN FRAME
    /// — the sub-ioqueue lane's instrument for the two-service-points defect.
    ///
    /// <para>
    /// An event counted here was already due when it was posted
    /// (<c>fireTime &lt;= </c> the time of the pass that has already run this
    /// frame) and was posted from OUTSIDE a service pass. Stock cannot produce
    /// one: <c>CEventQueue::ServiceEvents</c> re-reads the head after every
    /// delivery, so an event a delivery posts for "now" is delivered in the
    /// same pass. This build could, while there were TWO queues on two clocks —
    /// the managed one serviced inside <c>ServerSystemsPostThink</c> and the
    /// native one serviced after it — because an input the NATIVE pass
    /// delivered fired a managed output whose event arrived after the managed
    /// pass had finished, and waited a whole frame.
    /// </para>
    ///
    /// <para>
    /// <b>It reads zero on an idle server</b>, which is what makes it an
    /// instrument rather than a decoration: nothing posts, so nothing can be
    /// late, and a non-zero reading is always something that happened.
    /// </para>
    /// </summary>
    public long PostedAfterThePass { get; private set; }

    /// <summary>
    /// Posts an event.
    ///
    /// <para>
    /// Inserted BEFORE the first event whose fire time is strictly greater,
    /// which is where stock's linked-list walk puts it
    /// (<c>CEventQueue::AddEvent</c>, <c>cbase.cpp:1093</c>) and is what makes
    /// two events posted for the same instant deliver in posting order. A sort
    /// keyed on time alone would be free to reorder them, and a map whose two
    /// relays both fire at <c>t+0</c> would then be a coin toss.
    /// </para>
    /// </summary>
    public EntityIoEvent Add(float fireTime, string target, EntityHandle directTarget,
                             string targetInput, string? value,
                             EntityHandle activator, EntityHandle caller, int outputId)
    {
        var posted = new EntityIoEvent(fireTime, target ?? string.Empty, directTarget,
                                       targetInput ?? string.Empty, value,
                                       activator, caller, outputId, ++_sequence);

        // TOO LATE FOR THIS FRAME — see PostedAfterThePass. Counted at the post
        // and not at the delivery, because the delivery cannot tell the two
        // cases apart: by then a late event and a punctual one are both simply
        // due.
        if (!Servicing && fireTime <= _lastServicedTime)
            PostedAfterThePass++;

        int at = _events.Count;

        while (at > 0 && _events[at - 1].FireTime > fireTime)
            at--;

        _events.Insert(at, posted);
        return posted;
    }

    /// <summary>
    /// Runs the queue: delivers every event due at or before
    /// <paramref name="curTime"/>, re-reading the head each time so that an
    /// event posted by a delivery is picked up in this pass.
    /// </summary>
    /// <param name="deliver">
    /// What actually delivers one event. Answers nothing — a target that was
    /// not found is the deliverer's business to report, because it is the side
    /// that knows what a target is.
    /// </param>
    /// <returns>How many events were delivered.</returns>
    public int Service(float curTime, Action<EntityIoEvent> deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);

        // NOT RE-ENTRANT, AND THE INNER CALL ANSWERS ZERO RATHER THAN THROWING.
        // Stock services the queue from exactly one place, once a frame; an
        // entity that reaches back in from inside its own input handler would
        // otherwise deliver the events queued behind it out of order and
        // underneath the loop that is already walking them. The outer pass
        // picks them up on its next turn, which is the same frame.
        if (Servicing)
            return 0;

        Servicing = true;
        int delivered = 0;

        // THE WATERMARK IS RAISED AT THE START OF THE PASS, NOT AT ITS END.
        //
        // From here on, "this instant has been serviced" is TRUE — which is
        // what makes the Servicing test in Add() load-bearing rather than
        // decorative: a zero-delay event posted BY a delivery is inside the
        // pass and will be picked up by the head re-read below, so it is not
        // late; the identical event posted from outside the pass, at the same
        // instant, has missed its frame. Raising it in the `finally` instead
        // made both cases read the PREVIOUS pass's time and the distinction
        // could not be drawn at all.
        _lastServicedTime = curTime;

        try
        {
            while (_events.Count > 0 && _events[0].FireTime <= curTime)
            {
                if (delivered >= MaxChainPerPass)
                {
                    CyclesRefused++;

                    log.Warning(
                        $"[SourceSharp] entity I/O: {MaxChainPerPass} events delivered in one " +
                        $"pass without the queue emptying; this is a zero-delay cycle, not a " +
                        $"chain. The last was ({_events[0].Target},{_events[0].TargetInput}). " +
                        "The rest of the pass is dropped; stock would have hung here\n");

                    _events.Clear();
                    break;
                }

                var due = _events[0];
                _events.RemoveAt(0);

                delivered++;
                deliver(due);
            }
        }
        finally
        {
            Servicing = false;
            DeliveredLastPass = delivered;
            DeliveredTotal += delivered;
        }

        return delivered;
    }

    /// <summary>
    /// Drops every event posted by one entity — <c>CEventQueue::CancelEvents</c>,
    /// which stock calls when an entity is removed. Answers how many went.
    /// </summary>
    public int CancelFrom(EntityHandle caller)
    {
        if (caller == EntityHandle.None)
            return 0;

        return _events.RemoveAll(e => e.Caller == caller);
    }

    /// <summary>
    /// Drops every event aimed at one entity's named input —
    /// <c>CEventQueue::CancelEventOn</c>. The name is matched by the same rule
    /// a delivery would use, so cancelling what a wildcard posted works.
    /// </summary>
    public int CancelOn(string targetName, string inputName)
    {
        return _events.RemoveAll(
            e => EntityNameMatch.Match(targetName, e.Target)
                 && string.Equals(e.TargetInput, inputName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether anything is waiting for one entity's named input.</summary>
    public bool HasPending(string targetName, string inputName)
    {
        foreach (var e in _events)
        {
            if (EntityNameMatch.Match(targetName, e.Target)
                && string.Equals(e.TargetInput, inputName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Empties the queue. A level change.</summary>
    /// <remarks>
    /// The serviced-time watermark goes with the events. A new level starts
    /// <c>curtime</c> over, so a watermark carried across one would make every
    /// event of the first frames read as posted after a pass that never ran.
    /// </remarks>
    public void Clear()
    {
        _events.Clear();
        _lastServicedTime = float.NegativeInfinity;
    }

    /// <summary>
    /// The queue as reload records, with each event's REMAINING delay measured
    /// against <paramref name="curTime"/>.
    /// </summary>
    public List<SavedIoEvent> Save(float curTime)
    {
        var records = new List<SavedIoEvent>(_events.Count);

        foreach (var e in _events)
        {
            // Never negative: an event already due comes back due, and a
            // negative delay would sort ahead of everything on the far side.
            float remaining = e.FireTime - curTime;

            records.Add(new SavedIoEvent(remaining > 0f ? remaining : 0f,
                                         e.Target, e.DirectTarget.Value, e.TargetInput, e.Value,
                                         e.Activator.Value, e.Caller.Value, e.OutputId));
        }

        return records;
    }

    /// <summary>
    /// Puts saved events back, rebased on the clock as it is NOW. Answers how
    /// many were restored.
    /// </summary>
    public int Restore(IReadOnlyList<SavedIoEvent> records, float curTime)
    {
        if (records is null)
            return 0;

        foreach (var r in records)
        {
            Add(curTime + r.Delay, r.Target, new EntityHandle(r.DirectTarget), r.TargetInput,
                r.Value, new EntityHandle(r.Activator), new EntityHandle(r.Caller), r.OutputId);
        }

        return records.Count;
    }
}

/// <summary>
/// EVERY CONNECTION ONE ENTITY'S OUTPUTS CARRY, keyed by output name.
///
/// <para>
/// The managed replacement for <c>COutputEvent m_Outputs[]</c> on the native
/// shell. Keyed by NAME rather than by slot, because the slot was only ever a
/// way for two sides to agree about a list one of them declared: the shell
/// asked C# for the index, stored connections under it, and fired by index
/// again. With the list here there is no second side to agree with.
/// </para>
///
/// <para>
/// Case-insensitive, because <c>StandaloneOutputIndex</c> was — a map writing
/// <c>"ontrigger"</c> connects to <c>OnTrigger</c>, and it did before this.
/// </para>
/// </summary>
public sealed class EntityConnections
{
    private readonly Dictionary<string, List<EventAction>> _byOutput =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many outputs have at least one connection.</summary>
    public int OutputCount => _byOutput.Count;

    /// <summary>How many connections there are in total.</summary>
    public int Count
    {
        get
        {
            int total = 0;
            foreach (var list in _byOutput.Values)
                total += list.Count;
            return total;
        }
    }

    /// <summary>Every output that has a connection, with its connections.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<EventAction>>> All
    {
        get
        {
            foreach (var pair in _byOutput)
                yield return new(pair.Key, pair.Value);
        }
    }

    /// <summary>
    /// Adds one connection to an output.
    ///
    /// <para>
    /// APPENDED, where stock's <c>AddEventAction</c> PREPENDS
    /// (<c>cbase.cpp:520</c>). That is a deliberate divergence and it is a
    /// bug-for-order fix: stock's list is walked head first, so a map's
    /// connections fire in the reverse of the order the mapper wrote them, and
    /// the resulting order is an artefact of a singly-linked list rather than
    /// anything anyone chose. Two connections on one output with the same delay
    /// are the only case that can tell the difference, and this makes that case
    /// read the way the .vmf does.
    /// </para>
    /// </summary>
    public void Add(string outputName, EventAction action)
    {
        if (string.IsNullOrEmpty(outputName))
            return;

        if (!_byOutput.TryGetValue(outputName, out var list))
        {
            list = new List<EventAction>(1);
            _byOutput[outputName] = list;
        }

        list.Add(action);
    }

    /// <summary>The connections on one output, in firing order.</summary>
    public IReadOnlyList<EventAction> On(string outputName)
        => _byOutput.TryGetValue(outputName, out var list)
            ? list
            : Array.Empty<EventAction>();

    /// <summary>
    /// The longest delay on one output — <c>CBaseEntityOutput::GetMaxDelay</c>.
    /// </summary>
    public float MaxDelay(string outputName)
    {
        float max = 0f;

        foreach (var action in On(outputName))
        {
            if (action.Delay > max)
                max = action.Delay;
        }

        return max;
    }

    /// <summary>
    /// Fires one output: posts an event for each connection and spends a
    /// firing on the ones that count them.
    ///
    /// <para>
    /// <b>THE <c>times</c> ACCOUNTING IS STOCK'S, AND IT IS DESTRUCTIVE.</b>
    /// <c>CBaseEntityOutput::FireOutput</c> decrements <c>m_nTimesToFire</c> and
    /// UNLINKS the action when it reaches zero — so a one-shot connection is
    /// gone from the map after it fires, not merely inert. Reproduced here
    /// because a mapper counts on it: a <c>trigger_once</c> whose connection
    /// stayed in the list would fire again the moment anything re-triggered it.
    /// The record is replaced rather than mutated because it is a value type;
    /// that is a detail of the port and not of the rule.
    /// </para>
    ///
    /// <para>
    /// <b>AND THE DELAY ASYMMETRY IS STOCK'S TOO.</b> The no-parameter arm posts
    /// at <c>ev-&gt;m_flDelay + fDelay</c> and the parameter-override arm posts
    /// at <c>ev-&gt;m_flDelay</c> alone, dropping the caller's extra delay
    /// (<c>cbase.cpp:349</c> against <c>:358</c>). It reads like an oversight and
    /// it is reproduced anyway: every caller in this tree passes zero, so
    /// nothing observable turns on it, and "corrected" it would be a silent
    /// behaviour change with no test able to say which answer was wanted.
    /// </para>
    /// </summary>
    /// <returns>How many events were posted.</returns>
    public int Fire(EntityIoQueue queue, string outputName, float curTime, string? value,
                    EntityHandle activator, EntityHandle caller, float extraDelay = 0f)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (!_byOutput.TryGetValue(outputName, out var list) || list.Count == 0)
            return 0;

        int posted = 0;

        // Over a COPY, because a connection that spends its last firing is
        // removed from the live list and because a delivery cannot happen from
        // in here (everything is queued) — but a re-entrant Fire on the same
        // output can, and walking the list it is editing would skip a
        // connection.
        var firing = list.ToArray();
        var survivors = new List<EventAction>(firing.Length);

        for (int i = 0; i < firing.Length; i++)
        {
            var action = firing[i];

            if (!action.CanFire)
                continue;

            bool overridden = action.Parameter is not null;

            queue.Add(curTime + action.Delay + (overridden ? 0f : extraDelay),
                      action.Target, EntityHandle.None, action.TargetInput,
                      overridden ? action.Parameter : value,
                      activator, caller, action.IdStamp);

            posted++;

            var after = action.Fired();

            if (after.CanFire)
                survivors.Add(after);
        }

        // Anything added while this was firing is at the tail of the live list
        // and was not in the snapshot; keep it.
        for (int i = firing.Length; i < list.Count; i++)
            survivors.Add(list[i]);

        list.Clear();
        list.AddRange(survivors);

        if (list.Count == 0)
            _byOutput.Remove(outputName);

        return posted;
    }

    /// <summary>Drops every connection. The entity is going, or the level is.</summary>
    public void Clear() => _byOutput.Clear();
}
