//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// Where the parallel entity hierarchy runs in the frame, and when it is torn
/// down — <c>CSourceSharpStandaloneSystem</c>'s two surviving stages, as an
/// ordering rather than as a row.
///
/// <para>
/// plan_gamedll_after.md Phase A3, the fifth and last "C" system, and the only
/// one of the five that was SourceSharp's own code. Part 9a's note on it —
/// <i>"SourceSharp's own code, already ours"</i> — reads as an argument that it
/// moves easily. Measured, it is the opposite: <c>shell_standalone.cpp</c> and
/// its neighbours are the native FLOOR this managed side stands on, and a floor
/// cannot stand on itself.
/// </para>
///
/// <para>
/// <b>It held no logic, and one of the four things it said that about has since
/// stopped being true.</b> The methods it calls walk a <c>CUtlVector</c> of
/// native shells, replay <c>CUserCmd</c>s around a saved
/// <c>gpGlobals-&gt;curtime</c>, and free native objects — none of which has a
/// managed half to move to. But <i>"compose attachment-parented children against
/// their parents' poses"</i> did: step 2h moved the whole of parenting into
/// <see cref="Hierarchy"/>, and this class now OWNS that registry
/// (<see cref="Parenting"/>) and runs its sweep itself. What still moves it is
/// the same thing that moved the rest: <b>the order</b>.
/// </para>
///
/// <para>
/// <b>Three frame steps and not one, because the order between them was a C++
/// comment.</b> The row ran the player commands and then the entity thinks, and
/// said why in prose: <i>"Players first, so an entity's think this tick sees
/// where the players moved this tick — the same order stock keeps by simulating
/// players inside the physics pass and thinking entities after."</i> An order
/// written as a comment is an order nobody decided where orders are decided.
/// <see cref="IEngineFrame"/>'s own rule is that native exposes one step per
/// irreducible piece and C# owns the order, so it is two steps and the sentence
/// above is <see cref="OnThink"/>'s body.
/// </para>
///
/// <para>
/// <b>The position is the row's, and it is arithmetic rather than judgement.</b>
/// <c>CSourceSharpStandaloneSystem</c> was row 11 of <c>kServerSystems[]</c> and
/// <c>SourceSharpManaged</c> is row 12, with nothing between them. Every other
/// <see cref="IFrameLifecycle"/> participant at <see cref="OnThink"/> runs from
/// inside row 12's body, so <see cref="FrameStage.Standalone"/> at 50 — below
/// all of them — puts the hierarchy back where index 11 was. The teardown is the
/// same argument on the BACKWARD walk: row 12 declares
/// <c>LevelShutdownPreEntity</c> now and no row between 11 and 12 declares it,
/// so the hierarchy is torn down where it always was.
/// </para>
///
/// <para>
/// <b>What it costs, stated.</b> Three boundary crossings that used to be three
/// virtual calls — two per frame and one per level change — plus one behaviour
/// difference: <c>NetTrafficProfiler.Frame</c> is the one thing in
/// <c>ServerModule.Frame</c> that runs before the participants, so it now
/// samples the hierarchy's payloads BEFORE this tick's thinks wrote them rather
/// than after. It is a diagnostic, it is off unless a convar turns it on, and
/// what it then reports is one tick stale for these entities. Recorded in
/// docs/divergences.md rather than left to be discovered.
/// </para>
/// </summary>
public sealed class StandaloneHierarchy : IFrameLifecycle, IGameLifecycle, ISaveCustom
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineFrame _frame;
    private readonly IEngineLevel _level;
    private readonly IEngineHost _host;
    private readonly EntityDirectory _directory;

    /// <summary>
    /// The movement integrators — <c>plan_entity.md</c> step 2f. Here for the
    /// same reason the think heap is: WHERE in the frame it runs is the
    /// decision, and this class is where the frame's decisions are.
    /// </summary>
    private readonly EntityMotion _motion;

    /// <summary>
    /// THE FRAME'S OVERLAP REPORT — <c>plan_entity.md</c> step 2b. Here for the
    /// think heap's reason: what the report is FOR is a diff the directory owns,
    /// and WHERE in the frame it is taken is this class's decision.
    /// </summary>
    private readonly OverlapReader _overlaps;

    /// THE LEVEL'S PARENTING — plan_entity.md §3.2 and §3.5, step 2h made live.
    ///
    /// <para>
    /// <b>Here rather than in the container, and it is not a filing decision.</b>
    /// Two of parenting's fifteen entry points are not ABI slots at all: the
    /// once-a-frame sweep and the level teardown. Both had to run at an exact
    /// point in the frame — the sweep after the thinks and before the deferred
    /// deletes, the teardown before the entities are freed — and this class is
    /// the one thing in the port that owns both of those points. Putting the
    /// registry anywhere else would mean a service that had to be handed the
    /// order rather than being the place the order is kept.
    /// </para>
    ///
    /// <para>
    /// <b>NOT A STATIC</b>, which is <see cref="Hierarchy"/>'s own rule: one per
    /// side module, holding one level's links, emptied at the level change. A
    /// static would survive a hot reload holding handles from the map before it.
    /// </para>
    /// </summary>
    public HierarchyDriver Parenting { get; }

    public StandaloneHierarchy(ILog log, IEngineFrame frame, IEngineLevel level, IEngineHost host,
                               EntityDirectory directory, EntityMotion motion,
                               IEngineEntities entities, ShellLessRoutes shellLess,
                               IEngineNetworking networking, SolverHeldOverlaps solverHeld,
                               IEngineOverlaps overlaps)
    {
        _solverHeld = solverHeld;

        _log = log;
        // The world answers from THIS side's areas (plan_abi_hotreload D8): it
        // used to read the process-wide Engine, so a second game instance in the
        // process would have composed its children against the first one's.
        Parenting = new HierarchyDriver(new EngineHierarchyWorld(entities, networking, log));

        _frame = frame;
        _level = level;
        _host = host;
        _directory = directory;
        _motion = motion;

        // THE LINKS AN ENTITY TAKES WITH IT -- plan_entity.md step 2c, and it is
        // 2h's answer kept rather than a new one. ~CStandaloneEntity reached
        // StandaloneHierarchy_EntityRemoved for this; the destructor holds no
        // policy now, so the directory's removal pass asks the registry
        // directly, in the same position and inside a crossing that already
        // happens.
        //
        // HERE AND NOT IN THE REGISTRY because Parenting is this object's: the
        // registry owns the directory, and this owns the hierarchy that has to
        // be told about one of its removals.
        _directory.RemovalDetach = handle => Parenting.EntityRemoved(handle);

        // THE PUSH HALF NEEDS THE LINKS -- plan_entity.md step 2g. A mover
        // integrates in the frame it actually moves in, which for a child is
        // its parent's, and UpdateMovement reached
        // StandaloneHierarchy_WorldToLocal for that. Handed over here rather
        // than injected because this object is built WITH the integrator, so
        // asking the container for it inside EntityMotion would be a cycle --
        // the same reason RemovalDetach above is a hook and not a parameter.
        motion.Parenting = Parenting.Model;

        // AND WHERE A SHELL-LESS ENTITY'S parentname GOES -- plan_entity.md
        // step 2e, for the reason the line above it is here rather than in the
        // registry: Parenting is this object's.
        //
        // An entity with a shell reaches this through
        // CStandaloneEntity::KeyValueShellOnly, which forwards the key to
        // StandaloneHierarchy_SetParentName and so back up to this same driver.
        // A logical entity has no shell to forward it, and parenting is not
        // something a class opts into -- so the key would have been silently
        // dropped for all 130-odd of them.
        // On the composition's own route (plan_abi_hotreload D7), the one its
        // registry answers; it was a static.
        shellLess.ParentNameSink =
            (child, name) => Parenting.SetParentName(child, name);

        shellLess.ParentAttachmentNameSink =
            (child, name) => Parenting.SetParentAttachmentName(child, name);

        // The engine's report, with the trigger pairs vphysics holds open
        // merged in: managed's half of native's g_SolverHeld (B2.2).
        _overlaps = new OverlapReader(log, into => solverHeld.Merge(into, overlaps.OverlapReport(into)));
    }

    /// <summary>The trigger pairs vphysics holds open (B2.2), dropped with the level.</summary>
    private readonly SolverHeldOverlaps _solverHeld;

    /// <summary>The reader, so <c>debug-api status</c> can say how big the report ran. Instrumentation.</summary>
    public OverlapReader Overlaps => _overlaps;

    /// <summary>
    /// First, and <see cref="FrameStage.Standalone"/> says why: everything else
    /// at this point of the frame observes what the hierarchy's thinks produced.
    /// </summary>
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Standalone;

    /// <summary>
    /// After <see cref="LifecycleStage.SoundController"/>, which is stock's
    /// order: on the BACKWARD walk of <c>kServerSystems[]</c> the controller's
    /// row (18) ran before the hierarchy's (15).
    /// </summary>
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Standalone;

    /// <summary>
    /// The post-entity-think point, which is the one the row overrode.
    ///
    /// <para>
    /// <b>Players first.</b> An entity's think this tick should see where the
    /// players moved this tick, which is the order stock keeps by simulating
    /// players inside the physics pass and thinking entities after it. This is
    /// the whole reason the drive is two steps rather than one.
    /// </para>
    /// </summary>
    void IFrameLifecycle.OnThink(float frameTime)
    {
        _frame.StandalonePlayerCommands();

        // THE NATIVE UPDATE STEP IS NOT CALLED ANY MORE -- plan_entity.md step
        // 2g, and this line is the last of three removals rather than a new
        // decision. StandaloneEntities_Update ran the thinks (step 1g took
        // them), then the touch sweep (2b), then the movers; the movers are the
        // pass on the next line. The function is empty, so calling it would be
        // a crossing a frame for nothing. The step, its ABI slot and the
        // function are asked for together in abi-requests/2g.md, which is the
        // only place a slot may be removed from.

        // THE MOVEMENT INTEGRATORS -- plan_entity.md step 2f, EntityMotion.cs.
        //
        // EXACTLY WHERE UpdateFlight WAS, and the placement is the whole reason
        // it is a line here rather than a frame participant of its own: native
        // ran touch links, then the movers, then flight, and then the thinks.
        // A projectile that struck something this tick has to have struck it
        // BEFORE the thinks run, or a class deciding what to do about an impact
        // spends a frame not knowing about it -- and a projectile placed AFTER
        // the deferred deletes would be moved a frame after it was removed.
        //
        // It costs NO crossing of its own: this method is already inside the
        // one ServerGameFrame crossing, and the pass is one managed loop. What
        // it replaces called back into C# once per impact.
        _motion.RunFrame(frameTime);

        // THE TOUCH LINKS — plan_entity.md step 2b, EntityDirectory's touch half.
        //
        // BEFORE THE THINKS, which is where the native sweep was: the first line
        // of StandaloneEntities_Update was UpdateTouchLinks, and its comment
        // said why — "who has LEFT is computed before the thinks so that a
        // think asking 'is anybody standing in me' sees this frame's answer
        // rather than last frame's". That constraint is unchanged and this is
        // the same position in the frame.
        //
        // AFTER THE MOVERS AND THE INTEGRATORS, and that half is NEW. The
        // native version was per entity — each shell swept its own links and
        // then moved — so an entity's links were diffed against where it was
        // BEFORE this frame's movement. One report for the whole world cannot
        // be both before and after the movement, and after is the better half
        // of the choice: the pairs are then the pairs that exist at the moment
        // the thinks run, which is what the sentence above asks for. A trigger
        // a lift carried you into fires on the frame it happened rather than
        // the next one.
        //
        // IT COSTS NO CROSSING. This method is already inside the one
        // ServerGameFrame crossing; the report itself is one OUTBOUND call, and
        // what it replaces was one INBOUND crossing per touching pair per frame.
        //
        // A REPORT THAT COULD NOT BE READ WHOLE SKIPS THE FRAME rather than
        // being diffed short — see OverlapReader. Every link then stands for
        // one more frame, which is a stale answer; diffing a truncated report
        // would be a wave of departures that did not happen, which is a wrong
        // one.
        if (_overlaps.TryRead(out var overlaps))
            _directory.TouchFrame(overlaps);

        // THE SERVER FRAME'S ONE THINK PASS — plan_entity.md §3.4, step 1g.
        //
        // It costs NO crossing: the schedule and the run are both managed, and
        // this whole method is already inside the one ServerGameFrame crossing.
        // What it replaces was a native walk that crossed once per entity whose
        // think had come due, which §3.4 forbids by name.
        //
        // BETWEEN the two native halves rather than before or after both, and
        // that placement is the reason there are two. shell_standalone_base.cpp
        // argues each side: the attachment compose has to run after the thinks
        // (a think can advance a parent's sequence) and the deferred deletes
        // have to run after them too (an entity that dies inside its own think
        // must not be freed under the caller, and must not survive the frame).
        _directory.RunFrame(_host.CurrentTime);

        // THE PARENTING SWEEP — the links this hierarchy cannot see move.
        //
        // BETWEEN the thinks and StandaloneFinish, and both halves of that are
        // load-bearing. AFTER the thinks, because a think is one of the things
        // that can advance a parent's animation and an attachment frame is a
        // function of that clock, so a child placed before them would sit on
        // last frame's pose. BEFORE the deletes, so a child is not composed
        // against a parent that is already going. That is exactly where the
        // native sweep ran, at the top of StandaloneEntities_Finish, and exactly
        // where step 1g's split says the attachment compose belongs.
        //
        // IT COSTS NO CROSSING, which is why step 2h's ABI request listed it as
        // a slot and then said it might not need to be one. This method is
        // already inside the one ServerGameFrame crossing; a native sweep
        // reaching a managed registry would have needed a second.
        Parenting.RunFrame();

        // The attachment-parented children. The deferred deletes are the LINE
        // BELOW and are no longer in there; what is left in the native step is
        // the walk this hierarchy's own vector still needs.
        _frame.StandaloneFinish();

        // THE DEFERRED DELETES -- plan_entity.md step 2c, EntityDirectory's
        // removal half.
        //
        // LAST, and every earlier line above is a reason. After the thinks, so
        // an entity that decides to die inside its own think is not freed under
        // the caller that killed it. After the parenting sweep, so a child is
        // never composed against a parent that has already gone. And inside this
        // frame, so a handle does not survive into the next one -- which is what
        // makes "a handle stays valid until end of frame" a bound and not a
        // hope.
        //
        // IT COSTS ONE CROSSING FOR THE WHOLE FRAME'S REMOVALS (§3.4), and none
        // at all on a frame that removed nothing. What it replaces was a native
        // list drained in StandaloneEntities_Finish, whose every entry crossed
        // back into managed code once through ENTITY_OP_REMOVED.
        _directory.FreeRemoved();
    }

    /// <summary>
    /// The map is going away and its entities are still alive.
    ///
    /// <para>
    /// <b>PRE-entity, and post would be wrong rather than merely late.</b> The
    /// teardown sequence runs the pre-entity pass, then <c>gEntList.Clear()</c>,
    /// then the post-entity one — so from the post-entity half an entity
    /// removing itself is pulling out of an entity list and a spatial partition
    /// that have both been emptied underneath it.
    /// </para>
    ///
    /// <para>
    /// <b>And it is not optional.</b> <c>gEntList</c> cannot see these entities
    /// at all: the list is <c>IHandleEntity</c>-typed and its iteration walks
    /// <c>CBaseEntity</c>. Without this step an entity from the previous map
    /// survives into the next one, holding an edict, answering to its name and
    /// thinking.
    /// </para>
    /// </summary>
    //-------------------------------------------------------------------------
    // WHAT THE WORLD HOLDS ABOUT ITS ENTITIES -- ISaveCustom, the third tier.
    //
    // FOUR SUBSYSTEMS AND ONE RECORD, because this class already owns all four:
    // the parent links, the touch table and the think heap on the directory,
    // and the two motion tables. Four participants would have been four
    // records, four orderings and four places for the next person to add the
    // fifth thing and forget.
    //
    // NONE OF IT IS AN ENTITY'S OWN STATE, which is exactly why no declaration
    // on an entity could carry it and why this tier exists. A parent link
    // belongs to two entities; a touch pair belongs to two; a booking is a
    // position in a heap; a mover's target is in a frame the entity does not
    // know it is in.
    //
    // MEASURED BEFORE ANY OF THIS EXISTED, on dm_lockdown across one
    // cs_sv_reload, with `make check` and the live gate both green:
    //   think.pending   8 -> 0     every timed entity in the map stopped
    //   entities      473 -> 483   Pickups placed the map's kits a second time
    // and the parent links, the movers and the touch table went the same way
    // without a counter to show it.
    //-------------------------------------------------------------------------

    /// <summary>The row kinds this record carries. One list, tagged.</summary>
    private const string RowKind = "kind";
    private const string RowLink = "link";
    private const string RowPendingAttachment = "pend";
    private const string RowThink = "think";
    private const string RowTouch = "touch";

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (ParentLink link in Parenting.Model.Links)
        {
            var row = writer.AddRow()
                            .Set(RowKind, RowLink)
                            .Set("c", link.Child)
                            .Set("p", link.Parent)
                            .Set("a", link.ParentAttachment)
                            .Set("b", link.ParentIsBase)
                            .Set("r", link.Resolved)
                            .Set("pn", link.ParentName)
                            .Set("an", link.ParentAttachmentName);

            // THE OFFSET, AS TWELVE NUMBERS. It is what makes the child come
            // back in the place it is already standing in rather than being
            // re-measured against a parent frame the engine may not have posed
            // yet -- see Hierarchy.RestoreLink.
            Transform t = link.ChildToParent;
            row.Set("m", string.Join(" ", new[]
            {
                F(t.M00), F(t.M01), F(t.M02), F(t.M03),
                F(t.M10), F(t.M11), F(t.M12), F(t.M13),
                F(t.M20), F(t.M21), F(t.M22), F(t.M23),
            }));
        }

        foreach (var (child, attachment) in Parenting.Model.PendingAttachments())
        {
            writer.AddRow()
                  .Set(RowKind, RowPendingAttachment)
                  .Set("c", child)
                  .Set("an", attachment);
        }

        // THE SCHEDULE, AS SECONDS REMAINING AND NEVER AS AN ABSOLUTE TIME.
        // The reload does not stop the clock, and an absolute curtime written
        // before the unload is a time in the past by the time it is read -- so
        // every booking would come due on the first frame at once. Saved as a
        // delay for the same reason SavedIoEvent is (EntityIo.cs), and turned
        // back into a time against the clock as it reads on the way in.
        float now = _host.CurrentTime;

        foreach (ScheduledThink booked in _directory.Think.Bookings())
        {
            writer.AddRow()
                  .Set(RowKind, RowThink)
                  .Set("e", booked.Entity)
                  .Set("c", booked.Context ?? string.Empty)
                  .Set("d", booked.Time - now);
        }

        foreach (var (a, b) in _directory.TouchingPairs())
        {
            writer.AddRow()
                  .Set(RowKind, RowTouch)
                  .Set("a", a)
                  .Set("b", b);
        }

        _motion.SaveInto(writer);

        writer.Write("links", Parenting.Model.LinkCount);
        writer.Write("thinks", _directory.Think.Pending);
        writer.Write("touches", _directory.TouchLinkCount);
        writer.Write("movers", _motion.Count);
        writer.Write("pushers", _motion.PushCount);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        float now = _host.CurrentTime;
        int links = 0, pending = 0, thinks = 0, touches = 0;

        foreach (ReloadRow row in reader.Rows)
        {
            switch (row.GetString(RowKind))
            {
                case RowLink:
                {
                    if (Parenting.Model.RestoreLink(
                            reader.Resolve(row.GetHandle("c")),
                            reader.Resolve(row.GetHandle("p")),
                            row.GetInt("a"), row.GetBool("b"), row.GetBool("r"),
                            ParseTransform(row.GetString("m")),
                            row.GetString("pn"), row.GetString("an")))
                    {
                        links++;
                    }

                    break;
                }

                case RowPendingAttachment:
                {
                    EntityHandle child = reader.Resolve(row.GetHandle("c"));
                    if (child != EntityHandle.None)
                    {
                        Parenting.Model.SetParentAttachmentName(child, row.GetString("an"));
                        pending++;
                    }

                    break;
                }

                case RowThink:
                {
                    EntityHandle entity = reader.Resolve(row.GetHandle("e"));
                    if (entity == EntityHandle.None)
                        break;

                    string context = row.GetString("c");

                    // A DELAY THAT HAS ALREADY EXPIRED IS BOOKED AT NOW, not at
                    // a negative time: Schedule reads a negative TIME as a
                    // cancel, so a think that came due during the unload would
                    // be silently thrown away by the one arm that looks like it
                    // is handling it.
                    float delay = row.GetFloat("d");

                    _directory.Think.Schedule(entity, context.Length == 0 ? null : context,
                                              now + (delay < 0f ? 0f : delay));
                    thinks++;
                    break;
                }

                case RowTouch:
                {
                    if (_directory.RestoreTouch(reader.Resolve(row.GetHandle("a")),
                                                reader.Resolve(row.GetHandle("b"))))
                    {
                        touches++;
                    }

                    break;
                }
            }
        }

        int moved = _motion.RestoreFrom(reader);

        if (links + pending + thinks + touches + moved == 0)
            return;

        _log.Msg($"[SourceSharp/Server] reload: {links} parent link(s), {pending} pending " +
                $"attachment(s), {thinks} think booking(s), {touches} touch pair(s) and " +
                $"{moved} moving entit(ies) carried across\n");
    }

    private static string F(float value)
        => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Twelve numbers back into a transform. Anything else is
    /// <see cref="Transform.Identity"/> — which is the honest answer for a
    /// record that cannot be read, and leaves the child composed onto its
    /// parent's own frame rather than somewhere arbitrary.
    /// </summary>
    private static Transform ParseTransform(string text)
    {
        string[] parts = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 12)
            return Transform.Identity;

        var m = new float[12];

        for (int i = 0; i < 12; i++)
        {
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out m[i]))
            {
                return Transform.Identity;
            }
        }

        return new Transform(m[0], m[1], m[2], m[3],
                             m[4], m[5], m[6], m[7],
                             m[8], m[9], m[10], m[11]);
    }

    void IGameLifecycle.OnLevelShutdownPreEntity()
    {
        // THE PARENT LINKS FIRST, and the order is the whole point rather than a
        // tidy-up: with the registry already empty, no entity going below finds
        // a child to orphan, and nothing is queued for a deferred delete that
        // nothing is left to drain. It is the order the native teardown kept
        // (StandaloneEntities_LevelShutdown opened with it) and it survives the
        // move because both halves are now on this side of the boundary.
        Parenting.LevelShutdown();

        // vphysics' held trigger pairs name this map's entities, as native's
        // g_SolverHeld did and was cleared with (B2.2).
        _solverHeld.Clear();

        // Nothing is flying on a map that is going away, and a handle from the
        // old map is a handle the next one will reuse. Before the native
        // teardown, so the pass cannot be walking entities that are being
        // deleted underneath it.
        _motion.Clear();

        // EVERY ENTITY, THROUGH THE SAME PATH ANY OTHER REMOVAL TAKES -- step
        // 2c, and this is the step's live gate ("clean shutdown with every
        // entity destroyed").
        //
        // IT IS NOT BELT AND BRACES OVER THE NATIVE WALK BELOW; it is the thing
        // that tells managed code these entities have gone. ~CStandaloneEntity
        // announced each removal with an ENTITY_OP_REMOVED crossing until this
        // step, and a level teardown was six hundred of them. The announcement
        // is a managed call now, so the teardown has to go through the directory
        // or every class's OnRemoved is simply never called on a map change --
        // silently, and only on the SECOND map.
        //
        // The native walk still runs, after this, and finds nothing of its own
        // left: it is the belt and braces for a shell that was never bound here.
        _directory.RemoveAll();
        _directory.FreeRemoved();

        _level.ShutdownStandaloneEntities();
    }
}
