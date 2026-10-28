namespace SourceSharp.Tests;

/// <summary>
/// EVERY DIFFERENCE BETWEEN WHAT STOCK DECLARES AND WHAT THE MANAGED BUILD
/// ANSWERS, WRITTEN DOWN.
///
/// <para>
/// The pinned half of <see cref="ContractDiffTests"/> — `plan_test.md`'s layer
/// 0. See that class for what the comparison is and how to regenerate this
/// file. Six lists, and the difference between them is the whole point:
/// </para>
///
/// <list type="bullet">
///   <item><see cref="UniversalArgued"/> — what <c>CBaseEntity</c> declares and
///   the managed <c>Entity</c> BASE does not answer, one fact each rather than
///   one per class, and each carrying the argument for itself.
///   <see cref="Universal"/> is the lines projected out of it. RE-DERIVED at
///   step 8b: the old description of this list said "no managed entity
///   answers", which was false of twelve of its entries.</item>
///   <item><see cref="ClassGaps"/> — everything else, per map classname. Nobody
///   argued for these; they are the work that is left.</item>
///   <item><see cref="Deliberate"/> — differences somebody CHOSE, each citing
///   the heading in <c>docs/divergences.md</c> that argues for it.</item>
///   <item><see cref="Invented"/> and <see cref="StockSourceDeleted"/> — the map
///   classnames stock does not declare, and the two very different reasons.</item>
///   <item><see cref="Refusals"/> — capabilities somebody DECIDED not to have,
///   each with the mechanism, the measurement and what would close it.</item>
///   <item><see cref="OpenDefects"/> — things that do not work and that NOBODY
///   decided. Added at step 8b, and kept apart from <see cref="Refusals"/> on
///   purpose: a bug filed as a decision reads as "we chose this".</item>
/// </list>
/// </summary>
public static class ContractBaseline
{
    //-------------------------------------------------------------------------
    // THE ALLOW-LIST.
    //-------------------------------------------------------------------------

    /// <summary>A difference somebody decided on, and where the decision is written.</summary>
    public sealed record Rule(string Description, string Heading, Func<string, string, bool> Predicate)
    {
        public bool Matches(string kind, string name) => Predicate(kind, name);
    }

    /// <summary>
    /// The differences that are decisions. Each names a heading in
    /// <c>docs/divergences.md</c>, and <see cref="ContractDiffTests"/> checks
    /// both that the heading is still there and that the rule still matches
    /// something — a citation that has rotted reads as an argument and is a dead
    /// link, and a rule matching nothing excuses nothing forever.
    /// </summary>
    public static readonly IReadOnlyList<Rule> Deliberate = new Rule[]
    {
        new("the physics-gun outputs and preferred carry angles",
            "### Physics-gun outputs and carry angles",
            (kind, name) =>
                (kind == "output" && name.StartsWith("OnPhysGun", StringComparison.OrdinalIgnoreCase))
             || (kind == "key" && string.Equals(name, "preferredcarryangles", StringComparison.OrdinalIgnoreCase))),
    };

    /// <summary>Whether any rule argues for this difference.</summary>
    public static bool IsDeliberate(string kind, string name)
    {
        foreach (var rule in Deliberate)
        {
            if (rule.Matches(kind, name))
                return true;
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // THE CLASSNAMES WITH NO STOCK DECLARATION AT ALL.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Map classnames this build invented. Stock has never had them, so there is
    /// nothing to compare and never will be: the <c>ss_*</c> hierarchy's own
    /// entities and the fixtures the test suite registers.
    /// </summary>
    public static readonly IReadOnlyList<string> Invented = new[]
    {
        "native_fixture",
        "reload_fixture",
        "reload_forgetful",
        // stage0_core_seams.md phase 0.7: the mode proxy's switch entity,
        // the name the plan names (not ss_*: like game_rules it is placed by
        // a mapper, not written by code -- spawned through the registry by
        // GameModesTests today; the sandbox station pass will place it once
        // the porting batch lands, per CLAUDE.md). Stock never declared it.
        "game_mode_switch",
        // plan_entity.md step 6e: the fixture SaveTierAgreementTests puts
        // through BOTH walks over the marked and custom tiers, so a difference
        // between them shows up as a key or a value and not as a guess.
        "tier_agreement",
        "tier_double_marked",
        "tier_fixture",
        "tier_key_clash",
        "save_thrower",
        "ss_barrel",
        "ss_pickup",
        "ss_te_tester",
        "ss_weapon",
        "test_bad_input",
        "test_binding",
        "test_injected",
        "test_injected_by_name",
        "test_injection_directory",
        "test_injection_missing",
        "test_injection_plain",
        "test_io_relay",
        "test_io_sink",
        "test_item",
        "test_kill_override",
        "test_no_vars",
        "test_recursive_input",
        // TouchLinkTests' three fixtures -- plan_entity.md step 2b. A volume, an
        // item and a prop, declared in the test assembly so that a failure
        // points at the touch diff rather than at a change in what a
        // trigger_multiple does.
        "test_touch_item",
        "test_touch_prop",
        "test_touch_volume",

        "test_spawn_relay",
        "test_spawn_sink",

        // ShellLessEntityTests' three fixtures -- plan_entity.md step 2e. Two
        // shell-less classes and one that DOES make a shell, so every fact
        // about the shell-less path has a control beside it.
        "test_shelled_prop",
        "test_shellless_relay",
        "test_shellless_sink",
    };

    /// <summary>
    /// THE ONES THE PORT ITSELF MADE UNCOMPARABLE, and the correction this file
    /// carries to <c>plan_test.md</c>.
    ///
    /// <para>
    /// Layer 0 was designed on the premise that "the thing it was ported FROM
    /// still builds and still runs", so stock's half of the comparison could
    /// always be read out of <c>src/game</c>. That is no longer true for the
    /// entities ported EARLIEST. Where a later port left the C++ compiled behind
    /// a <c>#ifndef SOURCESHARP</c>, an earlier one DELETED it — <c>sound.cpp</c>
    /// says so in its own header comment: "CAmbientGeneric and everything under
    /// it is deleted here rather than left compiled behind a guard, because
    /// Phase 13 makes CBaseEntity a typedef for IGameEntity". So for these
    /// classnames there is no <c>LINK_ENTITY_TO_CLASS</c> and no datadesc left
    /// in the tree, and the diff has nothing to diff.
    /// </para>
    ///
    /// <para>
    /// They are listed rather than skipped because a silent skip is the failure
    /// this whole file exists to prevent: an entity that is not compared reads
    /// exactly like an entity with no differences. Recovering their contract
    /// means reading a stock revision out of git history, which is layer 0's
    /// natural next step and is not in it today.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> StockSourceDeleted = new[]
    {
        "ai_sound",
        // plan_entity.md step 4f. CPointClientCommand and CPointServerCommand
        // were deleted from game/server/client.cpp rather than left compiled
        // behind their guard: both LINK_ENTITY_TO_CLASS lines were already
        // `#ifndef SOURCESHARP`, so nothing in this build could create either,
        // and they were two of the twelve `: public <entity>` declarations
        // entity_gates gate 1 counts on the server. PointClientCommand.cs and
        // PointServerCommand.cs are the classnames now, and the native
        // `sv_allow_point_servercommand` ConVar registration STAYS -- the
        // managed entity reads it by name.
        //
        // BOTH ROWS, and the second one is here because a measurement corrected
        // a guess. Deleting only point_clientcommand's declaration looked like
        // the whole answer, since testfunctions.cpp and vscript_server.cpp both
        // still spell "point_servercommand" in their ent_create escalation
        // checks -- but the scanner wants a LINK_ENTITY_TO_CLASS, not a mention,
        // and this fact reports one name at a time, so the first row simply
        // uncovered the second.
        "point_clientcommand",
        "point_servercommand",
        "ambient_generic",
        "env_entity_maker",
        "func_clip_vphysics",
        "func_conveyor",
        "func_illusionary",
        "func_rotating",
        "func_vehicleclip",
        "func_wall",
        "func_wall_toggle",
        "game_gib_manager",
        "game_player_hurt",
        "info_lighting_relative",
        "player_manager",
        "point_template",
    };

    //-------------------------------------------------------------------------
    // BEHAVIOURS THAT ARE REFUSED, AND WHY.
    //
    // A different kind of entry from everything else in this file. The four
    // lists above are about what an entity DECLARES — keyvalues, inputs,
    // outputs — and are generated. These are decisions to not support something
    // at all, which no diff can discover, so they are written by hand and each
    // one carries the argument for itself.
    //
    // plan_entity.md §7 is what puts them here. Its closing line is the reason:
    // "a behaviour that is refused is a ContractBaseline entry with a reason,
    // never a silent no-op". A refusal nobody wrote down is indistinguishable
    // from a bug, and the person who finds it a year from now has to rediscover
    // the argument before they can decide whether to change it.
    //
    // Appended to, never rewritten. If a refusal is lifted, its entry goes in
    // the same commit as the code that lifts it.
    //-------------------------------------------------------------------------

    /// <summary>A capability this port does not have, and the argument for not having it.</summary>
    /// <param name="Subject">What is refused, named the way the engine names it.</param>
    /// <param name="Reason">Why. One sentence, mechanism first.</param>
    /// <param name="Citation">The section of the plan that decided it.</param>
    /// <param name="File">
    /// Repo-relative path to the WITNESS -- the file that holds something which
    /// is in this tree precisely BECAUSE the refusal is live. Must exist.
    /// </param>
    /// <param name="Code">
    /// A COMMENT-STRIPPED substring of that file which is present because the
    /// refusal is live, so that lifting the refusal deletes it and the fact goes
    /// red. Empty means the refusal has no tree-local witness at all, and
    /// <see cref="Reason"/> must then say "no tree-local witness" and why.
    /// </param>
    /// <param name="Lifted">
    /// The OTHER half, and the one that catches a refusal lifted by ADDITION
    /// rather than by deletion: a COMMENT-STRIPPED substring which must NOT be
    /// in <see cref="File"/>, and which would appear there the day somebody
    /// implements the refused thing. Empty where no such string can be named
    /// honestly.
    ///
    /// <para>
    /// An absence check passes whenever it is not violated, which on its own is
    /// the shape this tree has caught two dozen times. Two things stop it being
    /// decoration here: the needle must occur SOMEWHERE ELSE in the tree
    /// (<c>EveryLiftedNeedleNamesSomethingThatIsRealSomewhereElse</c>), so it
    /// names a real idiom rather than a string nobody would ever write; and each
    /// one was mutation-verified by planting it in the witness file and watching
    /// the fact go red.
    /// </para>
    /// </param>
    public sealed record Refusal(string Subject, string Reason, string Citation,
                                 string File, string Code, string Lifted);

    /// <summary>
    /// Every refused behaviour. Read by <c>PhysicsShellContractTests</c>,
    /// <c>OverlayContractTests</c> and by anyone asking "why does this not work"
    /// before they assume it is broken.
    ///
    /// <para>
    /// <b>THE RATCHET, added at step 8e-ratchets.</b> Until then this list had
    /// none: <see cref="Deliberate"/> had
    /// <c>EveryDeliberateRuleStillMatchesSomething</c> and
    /// <see cref="OpenDefects"/> had a witness check, and the entries here were
    /// read only for the TEXT they carry -- so every fact about them passed
    /// whether or not the thing being refused still existed. A refusal whose
    /// subject is deleted would have gone on excusing nothing forever.
    /// </para>
    ///
    /// <para>
    /// Step 8b audited all nine by hand and reported none stale. The ratchet
    /// found one on its first run anyway, which is the argument for having it:
    /// the AI-node-overlay entry said <c>shell_ai_absent.cpp</c> "defines
    /// g_pAINetworkManager and g_pBigAINet as NULL", and step 4f
    /// (<c>1e9e44351</c>, "the AI stub file measured again") had deleted both
    /// stubs when it pruned that file from 155 definitions to 15. The refusal is
    /// still live; the sentence arguing it had stopped being true, and a hand
    /// audit read past it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<Refusal> Refusals = new Refusal[]
    {
        // A SOUND EMITTED BY A SHELL-LESS ENTITY WAS HERE, and plan_entity.md
        // step 8e-sound TOOK IT OUT because it is no longer refused.
        //
        // Step 4b measured it and argued it: SourceSharp_EntityEmitSound
        // resolved the handle through g_pEntityList so native code could ask
        // the entity where it is, and a shell-less entity is never in that list
        // by construction -- no edict, no entity index, no C++ object of any
        // kind. Three shelled func_doors gave three EmitSound: traces on
        // dm_lockdown and one shell-less info_target gave zero, while step 2e's
        // logical.nativeReach counter moved 24 -> 25 for that one emit.
        //
        // It closed the way 4b's own write-up said it would: the position comes
        // from the side that owns it. Engine.EntityEmitSound answers a
        // shell-less handle in managed code and routes it to
        // EmitSoundAtPosition, which already took a point -- the same route
        // EntityGetOrigin and CreateEntity already took, no new ABI slot, and
        // nativeReach now FLAT across the emit.
        //
        // Its entry is deleted rather than reworded. "Appended to, never
        // rewritten" above means the argument is not edited under a reader's
        // feet; a refusal whose subject is FIXED is not an argument any more,
        // and leaving it would go on excusing something nobody is refusing.

        new("a caller-supplied damage force on radius damage — env_explosion's "
          + "`damageforce` key",
            "CGameRules::RadiusDamage had two arms for the force it hands each "
          + "victim: manufacture one from the damage, or take the one the "
          + "caller supplied, decay it by the falloff and point it away from "
          + "the blast. The traversal is managed now (BlastSweep.cs) and the "
          + "damage ABI has room for only the first: SourceSharp_EntityTakeDamage "
          + "takes a direction and a force ORIGIN and always derives the "
          + "magnitude through CalculateExplosiveDamageForce. The second arm is "
          + "reachable only from env_explosion with a `damageforce` key set, "
          + "which no map in the mod sets, and the request is announced out "
          + "loud in the log rather than dropped in silence. Lifting it is a "
          + "wider TakeDamage slot, which is a design call for the ABI owner "
          + "rather than a value one lane appends -- see "
          + ".claude/entity-port/abi-requests/policy-rules.md.",
            "plan_entity.md §3.10 and §7, landed by the shim wave's policy-rules lane",
            // WITNESS. The announcement itself: it is there BECAUSE the arm is
            // refused, so implementing the arm takes it out.
            "src/sourcesharp/managed/SourceSharp.Server/BlastSweep.cs",
            "manufactures the force from the damage instead",
            // AND WHAT LIFTING IT WOULD PUT THERE. Stock's arm is
            // `flForce = GetDamageForce().Length() * falloff`, so a lifted
            // version has to reach the falloff RATE -- which the sweep does not
            // today, because AtDistance folds it in and never exposes it. The
            // needle is a real idiom rather than a string nobody would write:
            // BlastRules.cs itself calls it by this spelling, in AtDistance,
            // which is what makes the absence check mean something.
            "Falloff("),

        // IMotionEvent::Simulate -- "a managed-defined motion controller" -- stood
        // here from step 3a (plan_entity.md §3.8 and §7). LIFTED by the user's
        // ruling of 2026-10-11 (all physics logic managed): vphysics asks
        // managed's own IMotionEvent table, mid-step, and MotionSimulation
        // answers from the controller's managed state
        // (plan_physics_bones_native_zero 2.2/2.4). Its witness left with
        // shell_motioncontrollers.cpp.

        // IPhysicsCollisionSolver::ShouldCollide -- "a managed collision rule that
        // needs state outside the engine block" -- stood here from step 3a (plan_entity.md
        // §3.8 and §7). LIFTED by the user's ruling of 2026-10-11 (all physics logic
        // managed): vphysics asks managed's own IPhysicsCollisionSolver table, and
        // PhysicsSolver answers from the block and managed state alike
        // (plan_physics_bones_native_zero 2.1). Its witness left physics.cpp with
        // CCollisionEvent.

        new("UTIL_DrawPositioningOverlay and the AI node editor's overlays — "
          + "the air-node placement crosshair, the node and link boxes, the hint "
          + "type label and the ground crosshair",
            "They draw for the AI NODE GRAPH, and this build has no AI node "
          + "graph: Phase 7 decided this mod designs its own AI in C#, so "
          + "server_sourcesharp compiles no ai_*.cpp file at all and "
          + "shell_absent.cpp is what lets the rest of the SDK still link "
          + "without them (Phase 1b merged shell_ai_absent.cpp into it). "
          + "IT NO LONGER STUBS THE NETWORK MANAGER, and step "
          + "8e-ratchets is what corrected this: the sentence used to say that "
          + "file defined g_pAINetworkManager and g_pBigAINet as NULL, which "
          + "step 4f made false when it re-measured the stub file down to the "
          + "15 symbols the link still asks for. Nothing in the build defines "
          + "either name now, which is a STRONGER form of the same claim. The "
          + "block that drew "
          + "them opened with g_pAINetworkManager->GetEditOps() and NO NULL "
          + "TEST, so `map <name> edit` did not draw the wrong thing here, it "
          + "DEREFERENCED NULL — the feature was already unreachable and unsafe "
          + "rather than merely unused. UTIL_DrawPositioningOverlay had already "
          + "been reduced to a bare `return;`, which is the silent no-op §7 "
          + "forbids, and this entry is what that becomes. It also carried "
          + "§3.16's FAMILY 6 on this side: the hint node's owner turned into an "
          + "entity through a void pointer. What replaces it for the parallel "
          + "hierarchy is Engine.DrawEntityOverlay, which draws the box, the "
          + "name and the classname for an entity named by HANDLE and is driven "
          + "by managed code, because managed code owns the aim that picks the "
          + "subject.",
            "plan_entity.md §3.16 family 6 and §7, landed by step 4c",
            // WITNESS, AND THE ENTRY THAT PROVED THIS RATCHET WAS WORTH ADDING.
            // The Reason above USED to end "shell_ai_absent.cpp defines
            // g_pAINetworkManager and g_pBigAINet as NULL". Step 4f (1e9e44351)
            // deleted both stubs when it re-measured that file down from 155
            // definitions to 15, and nothing noticed for the whole of Phase 8:
            // step 8b audited all nine entries by hand and reported none stale.
            // The sentence is corrected above and the witness is now a fact
            // rather than a description.
            //
            // The server's source list is the witness because it is where "this
            // build has no AI" is DECIDED. shell_ai_absent.cpp is on the file
            // list precisely because none of ai_*.cpp is, and it goes the day
            // the AI comes back.
            //
            // RE-POINTED at Phase 6b: the list was server_csharp.vpc and is now
            // server_sourcesharp's in src/server/native/CMakeLists.txt. That
            // path is REPO-relative, not SdkTree-relative like every other
            // witness here; ContractDiffTests.WitnessPath resolves the CMake
            // source lists (CMakeGraph.ListFile) against the repo and reads
            // them with '#' comments stripped.
            "src/server/native/CMakeLists.txt",
            // Phase 1b merged shell_ai_absent.cpp into shell_absent.cpp. The
            // witness is unchanged in substance: a row on this file list that
            // exists precisely because none of ai_*.cpp does, and that goes the
            // day the AI comes back.
            "shell_absent.cpp",
            ""),

        new("NDebugOverlay::Text's view-obstruction check — the bViewCheck arm "
          + "that dropped world text standing behind a wall",
            "REFUSED AS A WHOLE NOW, not in part. Until the server gc cull "
          + "(2026-10-01) Overlay_Text was the port: it drew the text and "
          + "discarded bViewCheck, because the trace needs a trace filter and a "
          + "trace filter is entity-aware, which overlay_library.h is not on "
          + "purpose. Overlay_Text had no caller in either DLL "
          + "(--print-gc-sections discarded it; the client never compiled the "
          + "file), and the user ruled it deleted with the clip accessors that "
          + "existed for its tests. So NDebugOverlay::Text has no entry point at "
          + "all, which is not the silent no-op §7 forbids: nothing calls it, and "
          + "the world text this build does draw goes through "
          + "Overlay_TextAtPosition, which stock never clipped. Whoever ports a "
          + "caller of Text ports the function, with the obstruction trace. "
          + "There is no tree-local witness: the refusal is an absence, and the "
          + "lifted needle below is what goes red when the trace arrives.",
            "plan_entity.md §7, landed by step 4c; the entry point deleted by the server gc cull",
            // NO WITNESS: an absence has no line to point at. The file is
            // where a port would land.
            "src/sourcesharp/game/shared/overlay_library.cpp",
            "",
            // AND WHAT WOULD REPLACE IT: stock's own call, verbatim from
            // debugoverlay_shared.cpp's bViewCheck arm. Many other files in
            // this tree use it and this one does not.
            "UTIL_TraceLine"),
        new("a point gib's bounce sheds no energy",
            "CGib::Spawn sets SetFriction(0.55) and MOVECOLLIDE_FLY_BOUNCE takes "
          + "elasticity off a gib on every contact, so stock's gib settles after a "
          + "few. The managed integrator that flies one — MoveMath.ResolveFlight, "
          + "step 2f's — reflects perfectly, v - 2(v·n)n, with no elasticity term "
          + "and no friction, so a point gib skitters further before it comes to "
          + "rest. It is recorded rather than worked around HERE because the fix "
          + "belongs in ResolveFlight, where every flying thing would get it, and "
          + "not in a per-entity correction that would make the gib disagree with "
          + "the bolt and the energy ball about what a bounce is.",
            "plan_entity.md §7, recorded by step 4a",
            // WITNESS. The perfect reflection itself, v - 2(v·n)n, TERMINATED BY
            // ITS OWN SEMICOLON -- so an elasticity factor appended to the
            // expression reddens the fact rather than sliding past a prefix
            // match. That is the "a substring needle misses added text" shape,
            // and this is the entry it would have bitten.
            "src/sourcesharp/managed/SourceSharp.Shared.Interop/MoveMath.cs",
            "step.Velocity - 2.0f * step.Velocity.Dot(normal) * normal;",
            // AND THE WORD THE FIX WOULD BRING WITH IT, lower-case: the name
            // of the per-surface restitution term (surfacephysicsparams_t's
            // `elasticity`, public/vphysics_interface.h) and of MOVECOLLIDE_FLY_BOUNCE's
            // own description in public/const.h, both compiled. It was
            // "Elasticity", whose eleven other occurrences were all in stock
            // files neither DLL compiles, and those left the farm with the
            // sdk-links shrink. MoveMath.cs has neither spelling.
            "elasticity"),

        new("a sprite `shootmodel` throws an invisible gib and hangs no sprite on it",
            "Stock builds that case as TWO entities: CEnvShooter::CreateGib spawns "
          + "the gib with an empty model (\"HACK HACK\", its own comment) so the gib "
          + "itself is invisible, and CGibShooter::InitPointGib hangs a CSprite off "
          + "it that is the thing you actually see. Only the first half is here. "
          + "Closing it needs a lifetime on that sprite so it goes when the gib "
          + "does, and a parent link from one managed entity to another that "
          + "survives the gib being removed mid-flight. The gib IS thrown and IS "
          + "invisible, which is stock's own behaviour for the first half, and the "
          + "warning names the model once rather than once per gib.",
            "plan_entity.md §7, recorded by step 4a",
            // WITNESS. The Warning names the gap in the words the entry uses.
            // Hanging a sprite on the gib is what closes it, and the sentence
            // "no sprite is attached to it" cannot survive that.
            "src/sourcesharp/managed/SourceSharp.Server/Entities/Effects/Gibs.cs",
            "is thrown (invisible, as in stock) but no sprite is attached to it. ",
            ""),
    };

    //-------------------------------------------------------------------------
    // DEFECTS THAT ARE OPEN, WHICH ARE NOT REFUSALS AND MUST NEVER BE FILED AS
    // ONE.
    //
    // Added at step 8b, and the reason is the shape this project keeps finding.
    // Refusals above are DECISIONS: somebody weighed a cost and declined, and
    // the entry is the argument. The ones below are BUGS: nobody decided them,
    // they are recorded in .claude/entity-port/STATE.md, and several have no
    // owner at all. Putting a bug in Refusals turns "we have not fixed this"
    // into "we chose this", which is the laundering §7 exists to stop -- so they
    // get their own list, their own record type with an Owner field, and their
    // own ratchet: each names a WITNESS in the tree, and when the witness goes
    // the fact goes red and forces the entry out.
    //
    // THE RATCHET HAS NOW FIRED ONCE, which is worth recording because a
    // mechanism nobody has watched work is a mechanism nobody should trust.
    // The soundscape entry's witness was the emptiness of
    // SourceSharp_PlayerSetSoundscape's body; step 8e-sound wired the transport
    // and the fact went RED with the words "the slot has a body now; the
    // soundscape defect is closed, so delete its OpenDefects entry", which is
    // exactly what was then done.
    //-------------------------------------------------------------------------

    /// <summary>Something that does not work, which nobody chose.</summary>
    /// <param name="Subject">What is broken, in a mapper's or a player's terms.</param>
    /// <param name="Mechanism">Why, mechanism first. Not a symptom.</param>
    /// <param name="Owner">The lane or person who owns it, or the word "unowned".</param>
    /// <param name="File">Repo-relative path to the witness. Must exist.</param>
    /// <param name="Code">
    /// A COMMENT-STRIPPED substring of that file which is present precisely
    /// BECAUSE the defect is open. Empty means the defect has no witness in this
    /// tree at all, and <see cref="Mechanism"/> must then say "no tree-local
    /// witness" and why -- an unratchetable entry is allowed, but only out loud.
    /// </param>
    public sealed record Defect(string Subject, string Mechanism, string Owner, string File, string Code);

    /// <summary>Every open defect this port knows about that a map or a player would meet.</summary>
    public static readonly IReadOnlyList<Defect> OpenDefects = new Defect[]
    {
        // "A PLAYER'S SOUNDSCAPE NEVER CHANGES ON ENTERING A TRIGGER_SOUNDSCAPE"
        // WAS HERE, and plan_entity.md step 8e-sound closed it.
        //
        // Its witness was the EMPTINESS of SourceSharp_PlayerSetSoundscape's
        // body, checked structurally by ContractDiffTests so that giving the
        // slot a body would force the entry out. That is what happened, though
        // not the way the witness expected: the slot did not get a body, it got
        // RETIRED. audioparams_t was networked through CBasePlayer's local-data
        // table and that class retired at 13c, so there was nothing on the
        // server for a push to be networked BY -- the push travels as the
        // `Soundscape` user message now (Soundscapes.Push ->
        // SoundscapeMessage.OnReceived -> ClientSetSoundscape ->
        // Soundscape_Update; since sound cluster 17 the last two are the
        // managed ClientSoundscapes.Push), and the old slot is a declared dead
        // marker awaiting one ABI removal.
        //
        // Measured closed: on dm_lockdown the server chooses index 227
        // [prison.hall_with_burrows] for a player standing in it and index 223
        // [prison.larger_cellblock_lightcombat] 2,220 units away, and the
        // client's own report now names the same two in turn where it read
        // -1 [none] at both.

        // "THE 3D SKYBOX DOES NOT RENDER" WAS HERE, and lane defect-skybox
        // closed it. It was the LAST entry on this list.
        //
        // ITS WITNESS WAS THE LEDGER ROW `SourceSharp_NotShipped( "3D skybox
        // (client)" )` in shell_view_facts.cpp, and the ratchet fired the way it
        // was designed to: the row went, EveryOpenDefectsWitnessIsStillInTheCode
        // went red, and the entry came out.
        //
        // IT TOOK TWO LANES AND TWO SEPARATE FAULTS, which is the part worth
        // keeping. This entry always said the two halves of the render pipeline
        // were separate faults and that closing one would not close the other;
        // it was right twice over.
        //
        //   * REACHABILITY (lane phase4-exit). UpdateAllClientData walked
        //     UTIL_PlayerByIndex, which cannot answer on this build, so
        //     ClientData_Update never ran and SourceSharp_FillSkyboxParams --
        //     its only caller -- was never reached.
        //   * THE WIRE (lane defect-skybox). That fix landed and the server has
        //     been filling MvLocal().m_skybox3d ever since -- into a struct
        //     nothing sends. CStandalonePlayer networks PlayerNetState, not
        //     CPlayerLocalData.
        //
        // Measured closed, live on ss_sandbox, by a differential with a control
        // arm rather than by a green gate: before, 12,270 frames set a sky view
        // up, the engine said a 3D skybox was VISIBLE from the eye on all
        // 12,270, and SourceSharp_ViewerSkyboxParams answered NULL every time --
        // 0 drawn. After, the miniature world draws, and `r_3dsky 0` takes the
        // draw count back to 0 while `r_3dsky 1` restores it, which is the run's
        // own proof that the count is measuring that renderer. The numbers are
        // in .claude/entity-port/status/defect-skybox.md.

        // "ent_fire CANNOT REACH ANY ENTITY THAT HAS NO EDICT" WAS HERE, and
        // plan_entity.md step 8e-entfire closed it. It stayed on this list for
        // four waves afterwards and STATE recorded the disagreement twice
        // rather than settling it, because this was the one entry with NO
        // tree-local witness -- the walk that failed is inside engine.so and
        // the shape that defeated it is §3.1 working as designed -- so no
        // managed fact here could ever have noticed.
        //
        // SETTLED BY COMMAND, the way the 3D skybox half of the same
        // disagreement was: tools/8e-entfire-gate drives everything below FROM
        // THE CONSOLE (never `debug-api fire`, which reaches AcceptInput directly
        // and would pass on the broken build) and now also asserts this row is
        // absent, with the teleport row below as its positive control. Run
        // 2026-09-08, lane loose-ends, exit 0, 13 of 13 PASS:
        //
        //   the created entity has NO edict        index [-1] from debug-api ents
        //   a word nothing carries IS still refused  1 time(s)  <- the control
        //   ent_fire BY NAME moves the view        displacement 8.400, was 0.000
        //   ent_fire BY CLASSNAME tints the screen fade alpha 39
        //   ent_fire Kill removes the class        ents 1 -> 0
        //   ...and the census fell by exactly that status.logical.live 83 -> 82
        //   ent_fire reached no handle NATIVELY    nativeReach 1 -> 1, 2 -> 2
        //
        // The last pair is the one that says it is really the managed route:
        // step 2e's counter does not move across either fire.

        // "AN ENTITY THAT TELEPORTS IS DRAWN SLIDING TO WHERE IT WENT" WAS HERE,
        // and lane defect-teleport closed it. IT WAS THE LAST ENTRY ON THIS
        // LIST, and this list is now EMPTY for the first time since step 8b
        // created it.
        //
        // ITS WITNESS WAS `bool fresh = _handles[index] != handle;` in
        // SourceSharp.Client/Interpolation.cs -- the ONE condition the managed
        // interpolation pass reset a ring on, which is a HANDLE REBIND and so
        // covers a create and nothing else. The ratchet fired the way it was
        // designed to: the line is still there (a create still needs it) but it
        // is no longer the whole condition, and the entry came out with the
        // second one.
        //
        // IT WAS TWO HALVES AND THEY LANDED TOGETHER, which is what p4-base
        // said when it filed this and is the part worth keeping. Neither half
        // is a compile error, a crash or a red fact without the other:
        //
        //   * THE DISCONTINUITY, in the entity's own block.
        //     EngineBlockHead.InterpolationFrame is stock's
        //     m_ubInterpolationFrame -- bumped by TeleportDiscontinuity.Bump on
        //     the MANAGED side of every teleport crossing, so point_teleport,
        //     trigger_teleport, the trains and the dev commands are covered
        //     without a line of C++ -- and OldInterpolationFrame beside it is
        //     stock's m_ubOldInterpolationFrame, which is what makes
        //     IsNoInterpolationFrame() the two words differing.
        //   * THE RESET TRIGGER, in Interpolation.SamplePass: `fresh ||
        //     teleported`, and then Acknowledge, after the reset and never
        //     before.
        //
        // A COUNTER AND NOT A FLAG, which is the wire's own reason and not a
        // preference: a "do not interpolate" bit is true for the one snapshot
        // that carries it, so a client whose delta was computed against a later
        // tick never sees it and the entity slides anyway with nothing to say
        // so. Stock retired EF_NOINTERP for exactly this and c_baseentity.h:1402
        // still records it in its comment.
        //
        // Measured closed, live on ss_sandbox, by a RENDERED measurement with a
        // control arm rather than by a counter: the pose the frame was DRAWN
        // with -- the render tail, which is what GetRenderOrigin answers -- read
        // back against the pose the packet delivered, over the same 500-unit
        // journey twice. Declared a jump, the entity is at its destination on
        // the first frame; not declared one, it is drawn short of it for
        // several. The numbers are in
        // .claude/entity-port/status/defect-teleport.md.
    };

    //-------------------------------------------------------------------------
    // THE PINNED DIFF, AND THE ARGUMENT FOR EVERY LINE OF IT.
    //
    // The LINES are generated; the ARGUMENTS are not, and that is the point of
    // step 8b. See ContractDiffTests for how to regenerate, and read the
    // Universal doc comment below before you do.
    //-------------------------------------------------------------------------

    /// <summary>The twelve mechanisms a universal gap can belong to.</summary>
    /// <remarks>
    /// Named rather than free text so a new entry has to say which existing
    /// argument it joins, and so a group that stops matching anything is a dead
    /// group <c>ContractDiffTests</c> can find. They are not a taxonomy of
    /// stock's datadesc; they are a taxonomy of WHY THIS PORT DOES NOT ANSWER,
    /// which is a different question and the one this file exists to record.
    /// </remarks>
    public const string GroupParenting = "parenting";

    /// <summary>Render state: colour, alpha, shadows, model, sort order.</summary>
    public const string GroupRenderState = "render-state";

    /// <summary>Squirrel is not hosted (<c>plan_vm.md</c>); there is no VM.</summary>
    public const string GroupVscriptAbsent = "vscript-absent";

    /// <summary>No response-system TU is compiled into the server; there is no rule database.</summary>
    public const string GroupResponseSystemAbsent = "response-system-absent";

    /// <summary>The four <c>FireUserN</c> inputs and the four <c>OnUserN</c> outputs.</summary>
    public const string GroupUserIo = "user-io";

    /// <summary>Health, damage filters and damage forces.</summary>
    public const string GroupDamage = "damage";

    /// <summary>Team numbers, on a build where only the player has a team.</summary>
    public const string GroupTeam = "team";

    /// <summary>Velocity, gravity, friction, movetype and the mover's own clock.</summary>
    public const string GroupMotion = "motion";

    /// <summary>What a map calls the entity and what it points at.</summary>
    public const string GroupIdentity = "identity";

    /// <summary>The map's ability to set a first think time.</summary>
    public const string GroupThink = "think";

    /// <summary>The <c>+USE</c> path, which 31 managed classes answer for themselves.</summary>
    public const string GroupUse = "use";

    /// <summary><c>DispatchEffect</c>, which belongs to no other group here.</summary>
    public const string GroupDispatchEffect = "dispatch-effect";

    /// <summary>
    /// One universal gap, with the argument for it.
    /// </summary>
    /// <param name="Line">The generated <c>kind|name</c>, exactly as the diff emits it.</param>
    /// <param name="Group">One of the <c>Group*</c> constants above.</param>
    /// <param name="AnsweredBy">
    /// HOW MANY REGISTERED MANAGED CLASSES ANSWER THIS NAME THEMSELVES, and the
    /// field that makes the list honest. It is pinned by equality against a live
    /// count in <c>ContractDiffTests</c>, so an entry cannot drift from 0 to 31
    /// or back without the file saying so.
    /// </param>
    /// <param name="Reason">
    /// Why this port does not answer it, mechanism first, and what would close
    /// it. Not optional: a gap nobody can argue for is either a defect that
    /// should be named as one or dead weight that should go.
    /// </param>
    public sealed record UniversalGap(string Line, string Group, int AnsweredBy, string Reason);

    /// <summary>
    /// WHAT <c>CBaseEntity</c> DECLARES AND THE MANAGED <c>Entity</c> BASE DOES
    /// NOT ANSWER. Subtracted from every per-class comparison, which is why it
    /// is pinned by equality in both directions.
    ///
    /// <para>
    /// <b>RE-DERIVED AT STEP 8b, AND THE OLD DESCRIPTION OF IT WAS FALSE.</b>
    /// This list used to say it held what "<c>CBaseEntity</c> declares and
    /// NOTHING managed answers". It does not, and never did: it is computed as
    /// stock's root contract MINUS the names EVERY registered managed class
    /// answers, which is the managed BASE's contract and not the union of all
    /// the classes'. <b>Twelve of the 67 entries are answered by between 1 and
    /// 33 managed classes each</b> — <c>input|Use</c> by 31, <c>key|speed</c> by
    /// 33, <c>key|target</c> by 30 — and each of those lines stands in for the
    /// ~212 compared classes that do NOT answer it. The <c>AnsweredBy</c> field
    /// records that count per entry and is pinned live, so the half-truth is now
    /// a number in the file rather than a sentence that was simply wrong.
    /// </para>
    ///
    /// <para>
    /// Three entries are NEW at step 8b and were invisible rather than absent:
    /// <c>key|globalname</c>, <c>key|model</c> and <c>key|modelindex</c> are
    /// declared with <c>DEFINE_GLOBAL_KEYFIELD</c>, a spelling
    /// <c>StockContract</c>'s scraper did not match, so they were on NEITHER
    /// side of the comparison. <c>model</c> is the most-used keyvalue in any
    /// map. See <c>StockContract.KeyFieldRe</c>.
    /// </para>
    ///
    /// <para>
    /// HOW TO APPEND WITHOUT COLLIDING, because several lanes edit this file.
    /// The generated <c>string</c> list is GONE: <see cref="Universal"/> is now
    /// projected from <see cref="UniversalArgued"/>, so there is exactly one
    /// place to edit and nothing to keep in step. Add or remove whole
    /// <c>new(...)</c> entries, keeping the list in the diff's own ordinal order
    /// of <c>Line</c>; each entry owns its own opening and closing lines, so two
    /// lanes' additions never share a trailing line. Regenerating with
    /// <c>SOURCESHARP_DUMP_CONTRACT=1</c> prints skeletons carrying
    /// <c>"TODO: argue this entry"</c>, and a TODO is a test failure — which is
    /// how "argued per entry" survives the next lane in a hurry.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<UniversalGap> UniversalArgued = new UniversalGap[]
    {
        // AddOutput WAS HERE AND IS ANSWERED NOW -- ScriptedEntity.OnAddOutput.
        // The refusal was written while the connection table lived in native
        // memory and there was nothing on this side to hang a connection on;
        // step 2a moved the table into EntityDirectory, so wiring one at runtime
        // is the same call the map's parse makes. Removed from this list rather
        // than left in it, because a list of what nothing answers that names
        // something answered is worse than no list.
        new("input|AddContext", GroupResponseSystemAbsent, 0,
            "The AI response system is not in this build: server_sourcesharp "
              + "compiles no response-system translation unit, so there is no rule "
              + "database for a context to be matched against. AddContext would "
              + "append a key:value to m_ResponseContexts, a CUtlVector on "
              + "CBaseEntity that has no counterpart and no reader here."),
        new("input|Alpha", GroupRenderState, 0,
            "Alpha writes m_clrRender's alpha byte. The block carries "
              + "RenderColorA beside RenderColorR/G/B, so the storage and the wire "
              + "are both present and only the input name is missing."),
        new("input|AlternativeSorting", GroupRenderState, 0,
            "AlternativeSorting flips m_bAlternateSorting, a translucency "
              + "sort-order hack for entities the renderer orders wrongly. There is "
              + "no block word for it and no managed reader; it is the least "
              + "valuable render entry here and is named so nobody has to work that "
              + "out twice."),
        new("input|CallScriptFunction", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. CallScriptFunction names a function in the entity's "
              + "script scope; there are no script scopes, so there is no name "
              + "space for the argument to resolve in."),
        new("input|ClearContext", GroupResponseSystemAbsent, 0,
            "The AI response system is not in this build: server_sourcesharp "
              + "compiles no response-system translation unit, so there is no rule "
              + "database for a context to be matched against. ClearContext empties "
              + "that vector. It is the one of the four that would be a correct "
              + "no-op, which is exactly why it must be declared rather than "
              + "quietly answered."),
        new("input|ClearParent", GroupParenting, 0,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. ClearParent unparents. It is "
              + "the cheapest of the five -- Hierarchy already has the detach path, "
              + "because an entity being removed has to run it."),
        new("input|Color", GroupRenderState, 1,
            "Color writes m_clrRender's three colour bytes; the block carries "
              + "RenderColorR, RenderColorG and RenderColorB. One managed class -- "
              + "shadow_control -- declares an input of this name for itself, which "
              + "is why this entry says AnsweredBy 1 and not 0."),
        new("input|DisableShadow", GroupRenderState, 0,
            "DisableShadow sets EF_NOSHADOW in m_fEffects -- so it is blocked "
              + "on the same dead Effects word as key|effects, not on a shadow "
              + "subsystem. Step 7d proved the shadow path itself works once "
              + "something drives it."),
        new("input|DispatchEffect", GroupDispatchEffect, 0,
            "DispatchEffect fires a named client effect from the entity's "
              + "origin. The effect dispatch itself exists on both sides, so the "
              + "missing piece is only the input that names one from a map; it sits "
              + "alone in its own group because it belongs to no other subsystem "
              + "here."),
        new("input|DispatchResponse", GroupResponseSystemAbsent, 0,
            "The AI response system is not in this build: server_sourcesharp "
              + "compiles no response-system translation unit, so there is no rule "
              + "database for a context to be matched against. DispatchResponse "
              + "asks the rule database for a response to a concept and speaks it; "
              + "with no database the input has no subject at all, not merely no "
              + "storage."),
        new("input|EnableShadow", GroupRenderState, 0,
            "EnableShadow clears EF_NOSHADOW, and is the same word and the same "
              + "one-line fix as DisableShadow. The two are one entry's worth of "
              + "work listed as two because stock declares two names."),
        new("input|FireUser1", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. FireUser1 is three "
              + "lines in stock (baseentity.cpp's InputFireUser1). The managed base "
              + "has the whole output mechanism -- Output fields and "
              + "EntityDirectory's connection table since step 2a -- so all eight "
              + "of these are declarations the base never made, not machinery it "
              + "lacks."),
        new("input|FireUser2", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. FireUser2 is three "
              + "lines in stock (baseentity.cpp's InputFireUser2). The managed base "
              + "has the whole output mechanism -- Output fields and "
              + "EntityDirectory's connection table since step 2a -- so all eight "
              + "of these are declarations the base never made, not machinery it "
              + "lacks."),
        new("input|FireUser3", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. FireUser3 is three "
              + "lines in stock (baseentity.cpp's InputFireUser3). The managed base "
              + "has the whole output mechanism -- Output fields and "
              + "EntityDirectory's connection table since step 2a -- so all eight "
              + "of these are declarations the base never made, not machinery it "
              + "lacks."),
        new("input|FireUser4", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. FireUser4 is three "
              + "lines in stock (baseentity.cpp's InputFireUser4). The managed base "
              + "has the whole output mechanism -- Output fields and "
              + "EntityDirectory's connection table since step 2a -- so all eight "
              + "of these are declarations the base never made, not machinery it "
              + "lacks."),
        new("input|KillHierarchy", GroupParenting, 0,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. KillHierarchy removes the "
              + "entity and everything parented to it. The managed base answers "
              + "Kill (ScriptedEntity.OnKill) and the child list exists, so this is "
              + "Kill plus a walk of a list the block already counts."),
        new("input|RemoveContext", GroupResponseSystemAbsent, 0,
            "The AI response system is not in this build: server_sourcesharp "
              + "compiles no response-system translation unit, so there is no rule "
              + "database for a context to be matched against. RemoveContext "
              + "removes one by name from that same vector; with no vector there is "
              + "nothing to remove from and nothing that would ever read the "
              + "result."),
        new("input|RunScriptCode", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. RunScriptCode hands a source string to the same "
              + "absent VM. The gap is a consequence of a decision made in another "
              + "plan, not of anything the entity base forgot."),
        new("input|RunScriptFile", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. RunScriptFile hands a .nut path to a VM there is "
              + "none of. Answering it would mean shipping an interpreter this port "
              + "deliberately does not have."),
        new("input|SetDamageFilter", GroupDamage, 0,
            "SetDamageFilter names a filter entity that vets damage before it "
              + "lands. Damage filters are a filter_* entity class plus a hook in "
              + "the take-damage path; the port has neither on the base, so this is "
              + "a subsystem gap rather than a missing setter."),
        new("input|SetParent", GroupParenting, 0,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. SetParent names a parent by "
              + "targetname and is the input every mapper reaches for. This is the "
              + "single most valuable entry in this list to close: the registry it "
              + "would call already exists and is already tested."),
        new("input|SetParentAttachment", GroupParenting, 0,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. SetParentAttachment parents to "
              + "a named studio attachment and snaps the child onto it. It needs "
              + "the attachment lookup on top of the registry, so it is a step "
              + "behind SetParent rather than beside it."),
        new("input|SetParentAttachmentMaintainOffset", GroupParenting, 0,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. Same as SetParentAttachment "
              + "but keeps the child's current relative transform. It differs from "
              + "the entry above by one boolean at the call site, so the two close "
              + "together or not at all."),
        new("input|SetTeam", GroupTeam, 0,
            "Teams exist on the player and nowhere else: there is no team "
              + "entity and no team number on the managed entity base. SetTeam "
              + "changes an entity's team at runtime. On a deathmatch mod with no "
              + "team play it changes nothing observable, which is precisely why it "
              + "must be declared rather than silently accepted."),
        new("input|TeamNum", GroupTeam, 0,
            "Teams exist on the player and nowhere else: there is no team "
              + "entity and no team number on the managed entity base. TeamNum is "
              + "declared with DEFINE_INPUT, so it is an input AND a key -- the "
              + "same name appears twice in this list for that reason, and a "
              + "scraper that read DEFINE_INPUT as a keyfield would report only "
              + "half of it."),
        new("input|TerminateScriptScope", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. TerminateScriptScope tears down that scope. Like "
              + "ClearContext it is the arm that would be a harmless no-op, and for "
              + "that reason is the one most worth writing down."),
        new("input|Use", GroupUse, 40,
            "FORTY managed classes answer Use themselves (31 until "
              + "func_physbox and func_physbox_multiplayer took CPhysBox::Use's "
              + "OnPlayerUse, plan_physics_managed A28; 33 until prop_physics, "
              + "physics_prop, prop_physics_override, prop_physics_respawnable and "
              + "prop_physics_multiplayer took CPhysicsProp::Use with the player's "
              + "+USE, and 38 until item_healthcharger and item_suitcharger took "
              + "the wall chargers' held-use machine, 2026-10-26) -- every button, "
              + "door, train and usable prop -- which makes this the entry that "
              + "most contradicts the sentence this list used to carry. It is here "
              + "because the BASE does not answer it, and every one of the 40 had "
              + "to declare it independently. Closing it on the base would make the "
              + "+USE path uniform and would let the other ~212 compared classes be "
              + "used by a mapper's logic, which today they cannot be."),
        new("key|CollisionGroup", GroupMotion, 0,
            "The block carries CollisionGroup and step 3f-shadow made "
              + "SetCollisionGroup re-run RecheckCollisionFilter, so the storage "
              + "and its side effects both work. Only the map-facing key is "
              + "missing."),
        new("key|MoveType", GroupMotion, 0,
            "The block DOES carry MoveType, and CStandaloneEntity reads it, so "
              + "this is a key with working storage and no route from the map to "
              + "it. A mapper setting MoveType on an entity in Hammer is rare but "
              + "it is how a func_ brush is made to fall."),
        new("key|ResponseContext", GroupResponseSystemAbsent, 0,
            "The AI response system is not in this build: server_sourcesharp "
              + "compiles no response-system translation unit, so there is no rule "
              + "database for a context to be matched against. The key seeds "
              + "m_iszResponseContext, the initial contexts a mapper gives an "
              + "entity. It is the map-facing half of the four inputs above and "
              + "goes when they go."),
        new("key|TeamNum", GroupTeam, 2,
            "Teams exist on the player and nowhere else: there is no team "
              + "entity and no team number on the managed entity base. The key half "
              + "of the DEFINE_INPUT above. Two managed classes declare it -- "
              + "logic_eventlistener and logic_eventlistener_itemequip -- because "
              + "they filter events by team, and they are the only two places a "
              + "team number is read at all."),
        new("key|avelocity", GroupMotion, 0,
            "m_vecAngVelocity, the spin. Same shape as velocity: managed "
              + "integration, no block word, no base key."),
        new("key|basevelocity", GroupMotion, 0,
            "m_vecBaseVelocity is the conveyor/push velocity added on top of "
              + "the entity's own each tick. It is meaningful only where something "
              + "adds to it, and trigger_push and func_conveyor both declare what "
              + "they need for themselves, so this is the least reachable of the "
              + "motion keys."),
        new("key|classname", GroupIdentity, 0,
            "A map may override an entity's classname after the factory has "
              + "already chosen a class from it, which is a stock oddity rather "
              + "than a useful feature. The block carries ClassnameIndex and the "
              + "managed side owns the classname; answering this key would mean "
              + "letting a map rename an entity out of the class that is driving "
              + "it, so this entry is a candidate for a DELIBERATE rule rather than "
              + "for closing."),
        new("key|damagefilter", GroupDamage, 0,
            "The map-facing half of input|SetDamageFilter, and it goes with it: "
              + "a key that names a filter is worth nothing until there is a filter "
              + "to name."),
        new("key|effects", GroupRenderState, 0,
            "THE DEFECT HALF OF THIS ENTRY IS CLOSED (step 8e-render) and what "
              + "is left is ordinary. It used to read \"a defect, not a design\": "
              + "EngineBlockHead.Effects was a dead four-byte word in every "
              + "entity's block, and CStandaloneEntity::SetVisible expressed "
              + "\"invisible\" by writing Block().Head.ModelIndex = 0 instead of "
              + "using it, so an invisible managed brush reached the client with no "
              + "SHAPE. SetVisible now sets EF_NODRAW in that word, m_fEffects is a "
              + "real prop in standalone_nettable.h, and ClientRenderPolicy.Drawn "
              + "refuses the draw on the bit -- see "
              + "SetVisibleNoLongerZeroesTheModelIndex and "
              + "TheEngineBlocksEffectsWordIsTheVisibilitySwitch. WHY THE LINE "
              + "STAYS: a Universal entry is one every registered class fails to "
              + "answer, and Universal subtracts the INTERSECTION of the classes' "
              + "declared vars. This port has no base [ScriptVars] set at all, so "
              + "that intersection is empty by construction -- "
              + "TheManagedBaseAnswersOnlyKill pins it -- and 34 key| lines are in "
              + "Universal for exactly this reason, key|model and key|spawnflags "
              + "among them. So `effects` as a MAP KEY is not parsed, and it cannot "
              + "leave alone: giving it to every class means a base var set in "
              + "ScriptedEntity.cs, which moves all 34 rows and is a change to a "
              + "shared file 8e-render does not own. Filed in requests/8e-render.md."),
        new("key|friction", GroupMotion, 0,
            "m_flFriction scales ground friction for this entity. No block "
              + "word, no base field; the physics shell (step 3a) parameterises "
              + "friction on the physics object instead, which is a different "
              + "quantity and does not answer this key."),
        new("key|globalname", GroupIdentity, 0,
            "NEW AT STEP 8b: another DEFINE_GLOBAL_KEYFIELD the scraper could "
              + "not see. m_iGlobalname is the save/restore GLOBAL identity -- how "
              + "an entity keeps its state across a changelevel -- and the live "
              + "gate has never done a changelevel to a different map (recorded in "
              + "STATE.md under Phase 0's coverage gaps). So this is a gap in a "
              + "feature nothing in this project measures, and it is named here so "
              + "that is a stated fact rather than an assumption."),
        new("key|gravity", GroupMotion, 2,
            "m_flGravity is a per-entity multiplier on the world's gravity. Two "
              + "managed classes declare it -- target_changegravity and "
              + "trigger_gravity -- and both of those set the PLAYER's, not their "
              + "own, so no managed entity has a per-entity gravity at all."),
        new("key|hammerid", GroupIdentity, 0,
            "The VMF's own id for the entity, carried so a save and the editor "
              + "can be matched up. Nothing in this port reads it; it is the "
              + "cheapest possible entry to close (store a string) and the least "
              + "valuable, and both halves of that are the argument."),
        new("key|health", GroupDamage, 15,
            "Fifteen managed classes declare health for themselves -- every "
              + "breakable and every physics prop -- so the gap is not that health "
              + "does not work, it is that a mapper cannot set it on a class that "
              + "did not think to ask. There is no block word for health and no "
              + "base field; each of the fifteen keeps its own."),
        new("key|ltime", GroupMotion, 0,
            "m_flLocalTime is a MOVETYPE_PUSH mover's own clock, which stock "
              + "deliberately saves as a float rather than a time so it survives a "
              + "clock rebase. Step 2g's managed movers keep their own; nothing "
              + "reads a map-supplied value, and a map supplying one would be "
              + "setting a mover's internal cursor, which no map does."),
        new("key|max_health", GroupDamage, 0,
            "m_iMaxHealth is the ceiling health is clamped to and the value a "
              + "respawn restores. NO managed class declares it, not even the "
              + "fifteen that declare health -- so every managed breakable has a "
              + "health with no maximum, and health above the mapper's value cannot "
              + "be expressed at all."),
        new("key|model", GroupRenderState, 19,
            "NEW AT STEP 8b, AND IT WAS INVISIBLE FOR A REASON WORTH KEEPING. "
              + "model is declared with DEFINE_GLOBAL_KEYFIELD, a spelling "
              + "StockContract's KeyFieldRe did not match, so the most-used "
              + "keyvalue in any map was on neither side of this comparison. "
              + "Nineteen managed classes declare it for themselves -- every prop_* "
              + "and sprite class -- which is why nothing ever noticed. The base "
              + "does not, so an entity whose class did not think to declare it "
              + "silently ignores the mapper's model."),
        new("key|modelindex", GroupRenderState, 0,
            "The other half of the same DEFINE_GLOBAL_KEYFIELD hole, and unlike "
              + "model NO managed class answers it. The block carries ModelIndex "
              + "and CStandaloneEntity writes it, so this is a key with working "
              + "storage and no route from the map to it."),
        new("key|nextthink", GroupThink, 0,
            "nextthink sets the absolute time of the entity's next think from "
              + "the map. The managed think schedule is complete and step 1g took "
              + "its crossings to zero, so the mechanism is not the gap -- what is "
              + "missing is the map's ability to set a first think time on an "
              + "entity whose class did not offer one."),
        new("key|parentname", GroupParenting, 1,
            "Parenting is MANAGED POLICY and the mechanism is present: step 2h "
              + "put the registry in Hierarchy, and the engine block carries "
              + "ParentHandle and ChildCount for it. What is absent is the "
              + "map-facing declaration on the base. The key is how a map states a "
              + "parent at spawn rather than at runtime, and one managed class -- "
              + "func_traincontrols -- declares it for itself. That single class is "
              + "the evidence the storage works and the base is what is missing."),
        new("key|rendercolor", GroupRenderState, 11,
            "The map-facing half of input|Color, and eleven managed classes "
              + "declare it for themselves (env_beam, env_sprite, point_spotlight "
              + "and eight more). Those eleven are the proof that the block's "
              + "RenderColorR/G/B carry a map's value correctly; the base is what "
              + "does not offer it."),
        new("key|renderfx", GroupRenderState, 5,
            "m_nRenderFX is the pulse/fade/glow-shell enumeration. There is NO "
              + "block word for it -- FxBlend is the computed blend, not the effect "
              + "-- so unlike rendercolor this one needs storage before it needs a "
              + "key. Five managed classes carry their own."),
        new("key|rendermode", GroupRenderState, 5,
            "m_nRenderMode chooses the material blend (normal, texture, glow, "
              + "additive). Again no block word: the block answers Translucent and "
              + "RenderGroup, which are downstream of rendermode rather than it. "
              + "Five managed classes carry their own."),
        new("key|shadowcastdist", GroupRenderState, 0,
            "m_flShadowCastDistance is the per-entity shadow cutoff. The block "
              + "carries ShadowChoice, ShadowType and ShadowFlags but no distance, "
              + "so this needs a block word; it is the smallest of the render gaps "
              + "and nothing in the port reads it today."),
        new("key|spawnflags", GroupIdentity, 0,
            "m_spawnflags is the checkbox word every stock entity reads its "
              + "options out of, and NO managed class declares it -- each managed "
              + "class takes its options as named keys instead. That is a "
              + "deliberate-looking divergence that nobody has written a rule for, "
              + "so it stays a gap until somebody argues it; see "
              + "docs/divergences.md for where such an argument would go."),
        new("key|speed", GroupMotion, 33,
            "Thirty-three managed classes declare speed for themselves -- every "
              + "door, train, platform and path node -- which is the largest "
              + "partial answer in this list after Use. m_flSpeed on CBaseEntity is "
              + "a general-purpose float those classes each interpret; the base "
              + "offering it would not make it mean anything, so this entry is "
              + "arguably the one closest to being correctly refused rather than "
              + "closed."),
        new("key|target", GroupIdentity, 30,
            "Thirty managed classes declare target for themselves. "
              + "m_target on CBaseEntity is the generic \"the entity this one acts "
              + "on\" name, and like speed it means something different in each of "
              + "the 30, so a base that offered it would be offering a field with "
              + "no agreed meaning."),
        new("key|teamnumber", GroupTeam, 0,
            "Teams exist on the player and nowhere else: there is no team "
              + "entity and no team number on the managed entity base. m_iTeamNum "
              + "is the live team, as against m_iInitialTeamNum which TeamNum sets; "
              + "stock keeps them apart because a team entity may not exist yet "
              + "when the map is parsed. Neither is answered here, and the "
              + "distinction is recorded so it is not lost if one of them is "
              + "closed."),
        new("key|texframeindex", GroupRenderState, 0,
            "m_iTextureFrameIndex picks a frame out of an animated material. No "
              + "block word, no managed reader, and no managed class declares it -- "
              + "it is a real gap with no partial answer anywhere, which is why it "
              + "is here rather than in ClassGaps."),
        new("key|thinkfunction", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. m_iszScriptThinkFunction is a VSCRIPT think, not a "
              + "native one: CBaseEntity::ScriptThink calls it through the VM. It "
              + "is grouped with VScript rather than with think for that reason -- "
              + "the managed think schedule is complete and this key is not part of "
              + "it."),
        new("key|velocity", GroupMotion, 0,
            "m_vecVelocity as a spawn value. There is no block word for "
              + "velocity -- motion is integrated in managed state since step 2f -- "
              + "so a mapper cannot give an entity a starting velocity through any "
              + "class that did not declare its own."),
        new("key|view_ofs", GroupIdentity, 0,
            "m_vecViewOffset is where an entity's eyes are relative to its "
              + "origin. The block carries ViewOrigin and the player uses it, but "
              + "it is computed on the managed side rather than taken from the map, "
              + "so a mapper cannot move an entity's eye point."),
        new("key|vscripts", GroupVscriptAbsent, 0,
            "plan_vm.md decided against hosting a Squirrel VM -- .nut content "
              + "is converted to C# and compiled -- so shell_vscript_absent.cpp "
              + "defines g_pScriptVM as NULL and the native half of VScript is out "
              + "of the build. The key is the semicolon-separated script list a "
              + "mapper attaches to an entity, read by CBaseEntity::RunVScripts. It "
              + "is the map-facing half of the four inputs above."),
        new("key|waterlevel", GroupMotion, 0,
            "The block carries WaterLevel and the water code writes it, so as a "
              + "SPAWN key this is nearly meaningless -- the first water check "
              + "overwrites it. It is recorded as a gap rather than excused because "
              + "deciding it is meaningless is a judgement, and the judgement "
              + "belongs beside the entry."),
        new("output|OnUser1", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. OnUser1 is the "
              + "output half. It is fired by FireUser1 above and by nothing else, "
              + "so the pair is worthless separately and the two entries close "
              + "together."),
        new("output|OnUser2", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. OnUser2 is the "
              + "output half. It is fired by FireUser2 above and by nothing else, "
              + "so the pair is worthless separately and the two entries close "
              + "together."),
        new("output|OnUser3", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. OnUser3 is the "
              + "output half. It is fired by FireUser3 above and by nothing else, "
              + "so the pair is worthless separately and the two entries close "
              + "together."),
        new("output|OnUser4", GroupUserIo, 0,
            "The four user inputs and the four user outputs are one relay: "
              + "FireUserN fires OnUserN and does nothing else, which is how a "
              + "mapper wires behaviour stock never thought of. OnUser4 is the "
              + "output half. It is fired by FireUser4 above and by nothing else, "
              + "so the pair is worthless separately and the two entries close "
              + "together."),
    };

    /// <summary>
    /// The lines only, in the order the diff emits them. PROJECTED from
    /// <see cref="UniversalArgued"/> rather than kept beside it: a needle and
    /// its subject must not be able to drift apart, and two hand-maintained
    /// lists of the same 67 names is exactly that shape.
    /// </summary>
    public static readonly IReadOnlyList<string> Universal =
        UniversalArgued.Select(g => g.Line).ToArray();

    /// <summary>
    /// One line per difference: <c>kind|classname|name</c>. May only shrink.
    ///
    /// <para>
    /// Grew ONCE, by 25 lines, when the diff moved to SourceSharp.OracleTests
    /// and started reading the pristine upstream clone instead of the farm:
    /// <c>func_clip_vphysics</c>, <c>func_rotating</c> and <c>point_template</c>
    /// are on <see cref="StockSourceDeleted"/> (the fork deleted their stock
    /// source, so the farm-read diff could not compare them) and
    /// <c>env_sprite_oriented</c> had lost its base chain the same way. Upstream
    /// still declares all four, so they are compared now and their gaps are
    /// written down like every other.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> ClassGaps = new string[]
    {
        "input|beam|ColorBlueValue",
        "input|beam|ColorGreenValue",
        "input|beam|ColorRedValue",
        "input|beam|Noise",
        "input|beam|ScrollSpeed",
        "input|beam|Width",
        "input|color_correction_volume|DisableAndEndTouch",
        "input|color_correction_volume|EndTouch",
        "input|color_correction_volume|StartTouch",
        "input|color_correction_volume|Toggle",
        "input|color_correction_volume|TouchTest",
        "input|crossbow_bolt|BecomeRagdoll",
        "input|crossbow_bolt|Ignite",
        "input|crossbow_bolt|IgniteHitboxFireScale",
        "input|crossbow_bolt|IgniteLifetime",
        "input|crossbow_bolt|IgniteNumHitboxFires",
        "input|crossbow_bolt|KilledNPC",
        "input|crossbow_bolt|SetBodyGroup",
        "input|crossbow_bolt|SetCycle",
        "input|crossbow_bolt|SetLightingOrigin",
        "input|crossbow_bolt|SetLightingOriginHack",
        "input|crossbow_bolt|SetModel",
        "input|crossbow_bolt|SetModelScale",
        "input|crossbow_bolt|SetPlaybackRate",
        "input|crossbow_bolt|fademaxdist",
        "input|crossbow_bolt|fademindist",
        "input|crossbow_bolt|physdamagescale",
        "input|crossbow_bolt|skin",
        "input|dynamic_prop|AddHealth",
        "input|dynamic_prop|BecomeRagdoll",
        "input|dynamic_prop|Break",
        "input|dynamic_prop|DisablePhyscannonPickup",
        "input|dynamic_prop|DisablePuntSound",
        "input|dynamic_prop|EnablePhyscannonPickup",
        "input|dynamic_prop|EnablePuntSound",
        "input|dynamic_prop|Ignite",
        "input|dynamic_prop|IgniteHitboxFireScale",
        "input|dynamic_prop|IgniteLifetime",
        "input|dynamic_prop|IgniteNumHitboxFires",
        "input|dynamic_prop|RemoveHealth",
        "input|dynamic_prop|SetBodyGroup",
        "input|dynamic_prop|SetCycle",
        "input|dynamic_prop|SetHealth",
        "input|dynamic_prop|SetLightingOrigin",
        "input|dynamic_prop|SetLightingOriginHack",
        "input|dynamic_prop|SetModel",
        "input|dynamic_prop|SetModelScale",
        "input|dynamic_prop|SetPlaybackRate",
        "input|dynamic_prop|fademaxdist",
        "input|dynamic_prop|fademindist",
        "input|dynamic_prop|physdamagescale",
        "input|dynamic_prop|skin",
        "input|entityflame|Ignite",
        "input|env_beam|ScrollSpeed",
        "input|env_laser|ScrollSpeed",
        "input|func_areaportalwindow|SetFadeEndDistance",
        "input|func_areaportalwindow|SetFadeStartDistance",
        "input|func_breakable_surf|AddHealth",
        "input|func_breakable_surf|Break",
        "input|func_breakable_surf|RemoveHealth",
        "input|func_breakable_surf|SetHealth",
        "input|func_breakable_surf|SetMass",
        "input|func_breakable_surf|Shatter",
        "input|func_breakable_surf|physdamagescale",
        "input|func_breakable|SetMass",
        "input|func_breakable|physdamagescale",
        "input|func_brush|SetExcluded",
        "input|func_brush|SetInvert",
        "input|func_button|PressIn",
        "input|func_button|PressOut",
        "input|func_clip_vphysics|Disable",
        "input|func_clip_vphysics|Enable",
        "input|func_door_rotating|SetSpeed",
        "input|func_door_rotating|SetToggleState",
        "input|func_door|SetSpeed",
        "input|func_door|SetToggleState",
        "input|func_friction|DisableAndEndTouch",
        "input|func_friction|EndTouch",
        "input|func_friction|StartTouch",
        "input|func_friction|TouchTest",
        "input|func_physbox_multiplayer|AddHealth",
        "input|func_physbox_multiplayer|Break",
        "input|func_physbox_multiplayer|DisableFloating",
        "input|func_physbox_multiplayer|ForceDrop",
        "input|func_physbox_multiplayer|RemoveHealth",
        "input|func_physbox_multiplayer|SetHealth",
        "input|func_physbox_multiplayer|SetMass",
        "input|func_physbox_multiplayer|physdamagescale",
        "input|func_physbox|AddHealth",
        "input|func_physbox|Break",
        "input|func_physbox|DisableFloating",
        "input|func_physbox|ForceDrop",
        "input|func_physbox|RemoveHealth",
        "input|func_physbox|SetHealth",
        "input|func_physbox|SetMass",
        "input|func_physbox|physdamagescale",
        "input|func_pushable|physdamagescale",
        "input|func_reflective_glass|SetExcluded",
        "input|func_reflective_glass|SetInvert",
        "input|func_rot_button|PressIn",
        "input|func_rot_button|PressOut",
        "input|func_rotating|StopAtStartPos",
        "input|func_water|SetSpeed",
        "input|func_water|SetToggleState",
        "input|gib|BecomeRagdoll",
        "input|gib|Ignite",
        "input|gib|IgniteHitboxFireScale",
        "input|gib|IgniteLifetime",
        "input|gib|IgniteNumHitboxFires",
        "input|gib|SetBodyGroup",
        "input|gib|SetCycle",
        "input|gib|SetLightingOrigin",
        "input|gib|SetLightingOriginHack",
        "input|gib|SetModel",
        "input|gib|SetModelScale",
        "input|gib|SetPlaybackRate",
        "input|gib|fademaxdist",
        "input|gib|fademindist",
        "input|gib|skin",
        "input|item_ammo_357_large|BecomeRagdoll",
        "input|item_ammo_357_large|Ignite",
        "input|item_ammo_357_large|IgniteHitboxFireScale",
        "input|item_ammo_357_large|IgniteLifetime",
        "input|item_ammo_357_large|IgniteNumHitboxFires",
        "input|item_ammo_357_large|SetBodyGroup",
        "input|item_ammo_357_large|SetCycle",
        "input|item_ammo_357_large|SetLightingOrigin",
        "input|item_ammo_357_large|SetLightingOriginHack",
        "input|item_ammo_357_large|SetModel",
        "input|item_ammo_357_large|SetModelScale",
        "input|item_ammo_357_large|SetPlaybackRate",
        "input|item_ammo_357_large|fademaxdist",
        "input|item_ammo_357_large|fademindist",
        "input|item_ammo_357_large|skin",
        "input|item_ammo_357|BecomeRagdoll",
        "input|item_ammo_357|Ignite",
        "input|item_ammo_357|IgniteHitboxFireScale",
        "input|item_ammo_357|IgniteLifetime",
        "input|item_ammo_357|IgniteNumHitboxFires",
        "input|item_ammo_357|SetBodyGroup",
        "input|item_ammo_357|SetCycle",
        "input|item_ammo_357|SetLightingOrigin",
        "input|item_ammo_357|SetLightingOriginHack",
        "input|item_ammo_357|SetModel",
        "input|item_ammo_357|SetModelScale",
        "input|item_ammo_357|SetPlaybackRate",
        "input|item_ammo_357|fademaxdist",
        "input|item_ammo_357|fademindist",
        "input|item_ammo_357|skin",
        "input|item_ammo_ar2_altfire|BecomeRagdoll",
        "input|item_ammo_ar2_altfire|Ignite",
        "input|item_ammo_ar2_altfire|IgniteHitboxFireScale",
        "input|item_ammo_ar2_altfire|IgniteLifetime",
        "input|item_ammo_ar2_altfire|IgniteNumHitboxFires",
        "input|item_ammo_ar2_altfire|SetBodyGroup",
        "input|item_ammo_ar2_altfire|SetCycle",
        "input|item_ammo_ar2_altfire|SetLightingOrigin",
        "input|item_ammo_ar2_altfire|SetLightingOriginHack",
        "input|item_ammo_ar2_altfire|SetModel",
        "input|item_ammo_ar2_altfire|SetModelScale",
        "input|item_ammo_ar2_altfire|SetPlaybackRate",
        "input|item_ammo_ar2_altfire|fademaxdist",
        "input|item_ammo_ar2_altfire|fademindist",
        "input|item_ammo_ar2_altfire|skin",
        "input|item_ammo_ar2_large|BecomeRagdoll",
        "input|item_ammo_ar2_large|Ignite",
        "input|item_ammo_ar2_large|IgniteHitboxFireScale",
        "input|item_ammo_ar2_large|IgniteLifetime",
        "input|item_ammo_ar2_large|IgniteNumHitboxFires",
        "input|item_ammo_ar2_large|SetBodyGroup",
        "input|item_ammo_ar2_large|SetCycle",
        "input|item_ammo_ar2_large|SetLightingOrigin",
        "input|item_ammo_ar2_large|SetLightingOriginHack",
        "input|item_ammo_ar2_large|SetModel",
        "input|item_ammo_ar2_large|SetModelScale",
        "input|item_ammo_ar2_large|SetPlaybackRate",
        "input|item_ammo_ar2_large|fademaxdist",
        "input|item_ammo_ar2_large|fademindist",
        "input|item_ammo_ar2_large|skin",
        "input|item_ammo_ar2|BecomeRagdoll",
        "input|item_ammo_ar2|Ignite",
        "input|item_ammo_ar2|IgniteHitboxFireScale",
        "input|item_ammo_ar2|IgniteLifetime",
        "input|item_ammo_ar2|IgniteNumHitboxFires",
        "input|item_ammo_ar2|SetBodyGroup",
        "input|item_ammo_ar2|SetCycle",
        "input|item_ammo_ar2|SetLightingOrigin",
        "input|item_ammo_ar2|SetLightingOriginHack",
        "input|item_ammo_ar2|SetModel",
        "input|item_ammo_ar2|SetModelScale",
        "input|item_ammo_ar2|SetPlaybackRate",
        "input|item_ammo_ar2|fademaxdist",
        "input|item_ammo_ar2|fademindist",
        "input|item_ammo_ar2|skin",
        "input|item_ammo_pistol_large|BecomeRagdoll",
        "input|item_ammo_pistol_large|Ignite",
        "input|item_ammo_pistol_large|IgniteHitboxFireScale",
        "input|item_ammo_pistol_large|IgniteLifetime",
        "input|item_ammo_pistol_large|IgniteNumHitboxFires",
        "input|item_ammo_pistol_large|SetBodyGroup",
        "input|item_ammo_pistol_large|SetCycle",
        "input|item_ammo_pistol_large|SetLightingOrigin",
        "input|item_ammo_pistol_large|SetLightingOriginHack",
        "input|item_ammo_pistol_large|SetModel",
        "input|item_ammo_pistol_large|SetModelScale",
        "input|item_ammo_pistol_large|SetPlaybackRate",
        "input|item_ammo_pistol_large|fademaxdist",
        "input|item_ammo_pistol_large|fademindist",
        "input|item_ammo_pistol_large|skin",
        "input|item_ammo_pistol|BecomeRagdoll",
        "input|item_ammo_pistol|Ignite",
        "input|item_ammo_pistol|IgniteHitboxFireScale",
        "input|item_ammo_pistol|IgniteLifetime",
        "input|item_ammo_pistol|IgniteNumHitboxFires",
        "input|item_ammo_pistol|SetBodyGroup",
        "input|item_ammo_pistol|SetCycle",
        "input|item_ammo_pistol|SetLightingOrigin",
        "input|item_ammo_pistol|SetLightingOriginHack",
        "input|item_ammo_pistol|SetModel",
        "input|item_ammo_pistol|SetModelScale",
        "input|item_ammo_pistol|SetPlaybackRate",
        "input|item_ammo_pistol|fademaxdist",
        "input|item_ammo_pistol|fademindist",
        "input|item_ammo_pistol|skin",
        "input|item_ammo_smg1_grenade|BecomeRagdoll",
        "input|item_ammo_smg1_grenade|Ignite",
        "input|item_ammo_smg1_grenade|IgniteHitboxFireScale",
        "input|item_ammo_smg1_grenade|IgniteLifetime",
        "input|item_ammo_smg1_grenade|IgniteNumHitboxFires",
        "input|item_ammo_smg1_grenade|SetBodyGroup",
        "input|item_ammo_smg1_grenade|SetCycle",
        "input|item_ammo_smg1_grenade|SetLightingOrigin",
        "input|item_ammo_smg1_grenade|SetLightingOriginHack",
        "input|item_ammo_smg1_grenade|SetModel",
        "input|item_ammo_smg1_grenade|SetModelScale",
        "input|item_ammo_smg1_grenade|SetPlaybackRate",
        "input|item_ammo_smg1_grenade|fademaxdist",
        "input|item_ammo_smg1_grenade|fademindist",
        "input|item_ammo_smg1_grenade|skin",
        "input|item_ammo_smg1_large|BecomeRagdoll",
        "input|item_ammo_smg1_large|Ignite",
        "input|item_ammo_smg1_large|IgniteHitboxFireScale",
        "input|item_ammo_smg1_large|IgniteLifetime",
        "input|item_ammo_smg1_large|IgniteNumHitboxFires",
        "input|item_ammo_smg1_large|SetBodyGroup",
        "input|item_ammo_smg1_large|SetCycle",
        "input|item_ammo_smg1_large|SetLightingOrigin",
        "input|item_ammo_smg1_large|SetLightingOriginHack",
        "input|item_ammo_smg1_large|SetModel",
        "input|item_ammo_smg1_large|SetModelScale",
        "input|item_ammo_smg1_large|SetPlaybackRate",
        "input|item_ammo_smg1_large|fademaxdist",
        "input|item_ammo_smg1_large|fademindist",
        "input|item_ammo_smg1_large|skin",
        "input|item_ammo_smg1|BecomeRagdoll",
        "input|item_ammo_smg1|Ignite",
        "input|item_ammo_smg1|IgniteHitboxFireScale",
        "input|item_ammo_smg1|IgniteLifetime",
        "input|item_ammo_smg1|IgniteNumHitboxFires",
        "input|item_ammo_smg1|SetBodyGroup",
        "input|item_ammo_smg1|SetCycle",
        "input|item_ammo_smg1|SetLightingOrigin",
        "input|item_ammo_smg1|SetLightingOriginHack",
        "input|item_ammo_smg1|SetModel",
        "input|item_ammo_smg1|SetModelScale",
        "input|item_ammo_smg1|SetPlaybackRate",
        "input|item_ammo_smg1|fademaxdist",
        "input|item_ammo_smg1|fademindist",
        "input|item_ammo_smg1|skin",
        "input|item_battery|BecomeRagdoll",
        "input|item_battery|Ignite",
        "input|item_battery|IgniteHitboxFireScale",
        "input|item_battery|IgniteLifetime",
        "input|item_battery|IgniteNumHitboxFires",
        "input|item_battery|SetBodyGroup",
        "input|item_battery|SetCycle",
        "input|item_battery|SetLightingOrigin",
        "input|item_battery|SetLightingOriginHack",
        "input|item_battery|SetModel",
        "input|item_battery|SetModelScale",
        "input|item_battery|SetPlaybackRate",
        "input|item_battery|fademaxdist",
        "input|item_battery|fademindist",
        "input|item_battery|skin",
        "input|item_box_buckshot|BecomeRagdoll",
        "input|item_box_buckshot|Ignite",
        "input|item_box_buckshot|IgniteHitboxFireScale",
        "input|item_box_buckshot|IgniteLifetime",
        "input|item_box_buckshot|IgniteNumHitboxFires",
        "input|item_box_buckshot|SetBodyGroup",
        "input|item_box_buckshot|SetCycle",
        "input|item_box_buckshot|SetLightingOrigin",
        "input|item_box_buckshot|SetLightingOriginHack",
        "input|item_box_buckshot|SetModel",
        "input|item_box_buckshot|SetModelScale",
        "input|item_box_buckshot|SetPlaybackRate",
        "input|item_box_buckshot|fademaxdist",
        "input|item_box_buckshot|fademindist",
        "input|item_box_buckshot|skin",
        "input|item_healthcharger|BecomeRagdoll",
        "input|item_healthcharger|Ignite",
        "input|item_healthcharger|IgniteHitboxFireScale",
        "input|item_healthcharger|IgniteLifetime",
        "input|item_healthcharger|IgniteNumHitboxFires",
        "input|item_healthcharger|SetBodyGroup",
        "input|item_healthcharger|SetCycle",
        "input|item_healthcharger|SetLightingOrigin",
        "input|item_healthcharger|SetLightingOriginHack",
        "input|item_healthcharger|SetModel",
        "input|item_healthcharger|SetModelScale",
        "input|item_healthcharger|SetPlaybackRate",
        "input|item_healthcharger|fademaxdist",
        "input|item_healthcharger|fademindist",
        "input|item_healthcharger|skin",
        "input|item_healthkit|BecomeRagdoll",
        "input|item_healthkit|Ignite",
        "input|item_healthkit|IgniteHitboxFireScale",
        "input|item_healthkit|IgniteLifetime",
        "input|item_healthkit|IgniteNumHitboxFires",
        "input|item_healthkit|SetBodyGroup",
        "input|item_healthkit|SetCycle",
        "input|item_healthkit|SetLightingOrigin",
        "input|item_healthkit|SetLightingOriginHack",
        "input|item_healthkit|SetModel",
        "input|item_healthkit|SetModelScale",
        "input|item_healthkit|SetPlaybackRate",
        "input|item_healthkit|fademaxdist",
        "input|item_healthkit|fademindist",
        "input|item_healthkit|skin",
        "input|item_healthvial|BecomeRagdoll",
        "input|item_healthvial|Ignite",
        "input|item_healthvial|IgniteHitboxFireScale",
        "input|item_healthvial|IgniteLifetime",
        "input|item_healthvial|IgniteNumHitboxFires",
        "input|item_healthvial|SetBodyGroup",
        "input|item_healthvial|SetCycle",
        "input|item_healthvial|SetLightingOrigin",
        "input|item_healthvial|SetLightingOriginHack",
        "input|item_healthvial|SetModel",
        "input|item_healthvial|SetModelScale",
        "input|item_healthvial|SetPlaybackRate",
        "input|item_healthvial|fademaxdist",
        "input|item_healthvial|fademindist",
        "input|item_healthvial|skin",
        "input|item_rpg_round|BecomeRagdoll",
        "input|item_rpg_round|Ignite",
        "input|item_rpg_round|IgniteHitboxFireScale",
        "input|item_rpg_round|IgniteLifetime",
        "input|item_rpg_round|IgniteNumHitboxFires",
        "input|item_rpg_round|SetBodyGroup",
        "input|item_rpg_round|SetCycle",
        "input|item_rpg_round|SetLightingOrigin",
        "input|item_rpg_round|SetLightingOriginHack",
        "input|item_rpg_round|SetModel",
        "input|item_rpg_round|SetModelScale",
        "input|item_rpg_round|SetPlaybackRate",
        "input|item_rpg_round|fademaxdist",
        "input|item_rpg_round|fademindist",
        "input|item_rpg_round|skin",
        "input|item_sodacan|BecomeRagdoll",
        "input|item_sodacan|Ignite",
        "input|item_sodacan|IgniteHitboxFireScale",
        "input|item_sodacan|IgniteLifetime",
        "input|item_sodacan|IgniteNumHitboxFires",
        "input|item_sodacan|SetBodyGroup",
        "input|item_sodacan|SetCycle",
        "input|item_sodacan|SetLightingOrigin",
        "input|item_sodacan|SetLightingOriginHack",
        "input|item_sodacan|SetModel",
        "input|item_sodacan|SetModelScale",
        "input|item_sodacan|SetPlaybackRate",
        "input|item_sodacan|fademaxdist",
        "input|item_sodacan|fademindist",
        "input|item_sodacan|skin",
        "input|item_suitcharger|BecomeRagdoll",
        "input|item_suitcharger|Ignite",
        "input|item_suitcharger|IgniteHitboxFireScale",
        "input|item_suitcharger|IgniteLifetime",
        "input|item_suitcharger|IgniteNumHitboxFires",
        "input|item_suitcharger|SetBodyGroup",
        "input|item_suitcharger|SetCycle",
        "input|item_suitcharger|SetLightingOrigin",
        "input|item_suitcharger|SetLightingOriginHack",
        "input|item_suitcharger|SetModel",
        "input|item_suitcharger|SetModelScale",
        "input|item_suitcharger|SetPlaybackRate",
        "input|item_suitcharger|fademaxdist",
        "input|item_suitcharger|fademindist",
        "input|item_suitcharger|skin",
        "input|keyframe_rope|Break",
        "input|keyframe_rope|SetForce",
        "input|keyframe_rope|SetScrollSpeed",
        "input|light_dynamic|_cone",
        "input|light_dynamic|_inner_cone",
        "input|light_dynamic|brightness",
        "input|light_dynamic|distance",
        "input|light_dynamic|spotlight_radius",
        "input|light_dynamic|style",
        "input|logic_active_autosave|Save",
        "input|logic_active_autosave|SaveDangerous",
        "input|logic_active_autosave|SetMinHitpointsThreshold",
        "input|logic_autosave|SetMinHitpointsThreshold",
        "input|logic_branch_listener|_OnLogicBranchRemoved",
        "input|logic_timer|LowerRandomBound",
        "input|logic_timer|UpperRandomBound",
        "input|logic_timer|UseRandomTime",
        "input|momentary_rot_button|Press",
        "input|momentary_rot_button|PressIn",
        "input|momentary_rot_button|PressOut",
        "input|move_rope|Break",
        "input|move_rope|SetForce",
        "input|move_rope|SetScrollSpeed",
        "input|npc_grenade_frag|BecomeRagdoll",
        "input|npc_grenade_frag|Ignite",
        "input|npc_grenade_frag|IgniteHitboxFireScale",
        "input|npc_grenade_frag|IgniteLifetime",
        "input|npc_grenade_frag|IgniteNumHitboxFires",
        "input|npc_grenade_frag|SetBodyGroup",
        "input|npc_grenade_frag|SetCycle",
        "input|npc_grenade_frag|SetLightingOrigin",
        "input|npc_grenade_frag|SetLightingOriginHack",
        "input|npc_grenade_frag|SetModel",
        "input|npc_grenade_frag|SetModelScale",
        "input|npc_grenade_frag|SetPlaybackRate",
        "input|npc_grenade_frag|SetTimer",
        "input|npc_grenade_frag|fademaxdist",
        "input|npc_grenade_frag|fademindist",
        "input|npc_grenade_frag|skin",
        "input|phys_ballsocket|ConstraintBroken",
        "input|phys_constraint|ConstraintBroken",
        "input|phys_hinge|ConstraintBroken",
        "input|phys_lengthconstraint|ConstraintBroken",
        "input|phys_magnet|BecomeRagdoll",
        "input|phys_magnet|Ignite",
        "input|phys_magnet|IgniteHitboxFireScale",
        "input|phys_magnet|IgniteLifetime",
        "input|phys_magnet|IgniteNumHitboxFires",
        "input|phys_magnet|SetBodyGroup",
        "input|phys_magnet|SetCycle",
        "input|phys_magnet|SetLightingOrigin",
        "input|phys_magnet|SetLightingOriginHack",
        "input|phys_magnet|SetModel",
        "input|phys_magnet|SetModelScale",
        "input|phys_magnet|SetPlaybackRate",
        "input|phys_magnet|fademaxdist",
        "input|phys_magnet|fademindist",
        "input|phys_magnet|skin",
        "input|phys_pulleyconstraint|ConstraintBroken",
        "input|phys_ragdollconstraint|ConstraintBroken",
        "input|phys_slideconstraint|ConstraintBroken",
        "input|physics_cannister|BecomeRagdoll",
        "input|physics_cannister|Ignite",
        "input|physics_cannister|IgniteHitboxFireScale",
        "input|physics_cannister|IgniteLifetime",
        "input|physics_cannister|IgniteNumHitboxFires",
        "input|physics_cannister|KilledNPC",
        "input|physics_cannister|SetBodyGroup",
        "input|physics_cannister|SetCycle",
        "input|physics_cannister|SetLightingOrigin",
        "input|physics_cannister|SetLightingOriginHack",
        "input|physics_cannister|SetModel",
        "input|physics_cannister|SetModelScale",
        "input|physics_cannister|SetPlaybackRate",
        "input|physics_cannister|fademaxdist",
        "input|physics_cannister|fademindist",
        "input|physics_cannister|physdamagescale",
        "input|physics_cannister|skin",
        "input|physics_prop|AddHealth",
        "input|physics_prop|BecomeRagdoll",
        "input|physics_prop|DisableFloating",
        "input|physics_prop|DisablePhyscannonPickup",
        "input|physics_prop|DisablePuntSound",
        "input|physics_prop|EnablePhyscannonPickup",
        "input|physics_prop|EnablePuntSound",
        "input|physics_prop|Ignite",
        "input|physics_prop|IgniteHitboxFireScale",
        "input|physics_prop|IgniteLifetime",
        "input|physics_prop|IgniteNumHitboxFires",
        "input|physics_prop|RemoveHealth",
        "input|physics_prop|SetBodyGroup",
        "input|physics_prop|SetCycle",
        "input|physics_prop|SetLightingOrigin",
        "input|physics_prop|SetLightingOriginHack",
        "input|physics_prop|SetModel",
        "input|physics_prop|SetModelScale",
        "input|physics_prop|SetPlaybackRate",
        "input|physics_prop|fademaxdist",
        "input|physics_prop|fademindist",
        "input|physics_prop|skin",
        "input|player|BecomeRagdoll",
        "input|player|DisableFlashlight",
        "input|player|EnableFlashlight",
        "input|player|ForceDropPhysObjects",
        "input|player|HandleMapEvent",
        "input|player|Ignite",
        "input|player|IgniteHitboxFireScale",
        "input|player|IgniteLifetime",
        "input|player|IgniteNumHitboxFires",
        "input|player|IgnoreFallDamage",
        "input|player|IgnoreFallDamageWithoutReset",
        "input|player|KilledNPC",
        "input|player|OnSquadMemberKilled",
        "input|player|SetBodyGroup",
        "input|player|SetCycle",
        "input|player|SetFogController",
        "input|player|SetHUDVisibility",
        "input|player|SetHealth",
        "input|player|SetLightingOrigin",
        "input|player|SetLightingOriginHack",
        "input|player|SetModel",
        "input|player|SetModelScale",
        "input|player|SetPlaybackRate",
        "input|player|SetScriptOverlayMaterial",
        "input|player|fademaxdist",
        "input|player|fademindist",
        "input|player|physdamagescale",
        "input|player|skin",
        "input|predicted_viewmodel|BecomeRagdoll",
        "input|predicted_viewmodel|Ignite",
        "input|predicted_viewmodel|IgniteHitboxFireScale",
        "input|predicted_viewmodel|IgniteLifetime",
        "input|predicted_viewmodel|IgniteNumHitboxFires",
        "input|predicted_viewmodel|SetBodyGroup",
        "input|predicted_viewmodel|SetCycle",
        "input|predicted_viewmodel|SetLightingOrigin",
        "input|predicted_viewmodel|SetLightingOriginHack",
        "input|predicted_viewmodel|SetModel",
        "input|predicted_viewmodel|SetModelScale",
        "input|predicted_viewmodel|SetPlaybackRate",
        "input|predicted_viewmodel|fademaxdist",
        "input|predicted_viewmodel|fademindist",
        "input|predicted_viewmodel|skin",
        "input|prop_combine_ball|BecomeRagdoll",
        "input|prop_combine_ball|Explode",
        "input|prop_combine_ball|FadeAndRespawn",
        "input|prop_combine_ball|Ignite",
        "input|prop_combine_ball|IgniteHitboxFireScale",
        "input|prop_combine_ball|IgniteLifetime",
        "input|prop_combine_ball|IgniteNumHitboxFires",
        "input|prop_combine_ball|SetBodyGroup",
        "input|prop_combine_ball|SetCycle",
        "input|prop_combine_ball|SetLightingOrigin",
        "input|prop_combine_ball|SetLightingOriginHack",
        "input|prop_combine_ball|SetModel",
        "input|prop_combine_ball|SetModelScale",
        "input|prop_combine_ball|SetPlaybackRate",
        "input|prop_combine_ball|Socketed",
        "input|prop_combine_ball|fademaxdist",
        "input|prop_combine_ball|fademindist",
        "input|prop_combine_ball|skin",
        "input|prop_door_rotating|AddHealth",
        "input|prop_door_rotating|BecomeRagdoll",
        "input|prop_door_rotating|Break",
        "input|prop_door_rotating|Disable",
        "input|prop_door_rotating|DisableCollision",
        "input|prop_door_rotating|DisablePhyscannonPickup",
        "input|prop_door_rotating|DisablePuntSound",
        "input|prop_door_rotating|Enable",
        "input|prop_door_rotating|EnableCollision",
        "input|prop_door_rotating|EnablePhyscannonPickup",
        "input|prop_door_rotating|EnablePuntSound",
        "input|prop_door_rotating|Ignite",
        "input|prop_door_rotating|IgniteHitboxFireScale",
        "input|prop_door_rotating|IgniteLifetime",
        "input|prop_door_rotating|IgniteNumHitboxFires",
        "input|prop_door_rotating|Lock",
        "input|prop_door_rotating|OpenAwayFrom",
        "input|prop_door_rotating|RemoveHealth",
        "input|prop_door_rotating|SetAnimation",
        "input|prop_door_rotating|SetBodyGroup",
        "input|prop_door_rotating|SetCycle",
        "input|prop_door_rotating|SetDefaultAnimation",
        "input|prop_door_rotating|SetHealth",
        "input|prop_door_rotating|SetLightingOrigin",
        "input|prop_door_rotating|SetLightingOriginHack",
        "input|prop_door_rotating|SetModel",
        "input|prop_door_rotating|SetModelScale",
        "input|prop_door_rotating|SetPlaybackRate",
        "input|prop_door_rotating|TurnOff",
        "input|prop_door_rotating|TurnOn",
        "input|prop_door_rotating|Unlock",
        "input|prop_door_rotating|fademaxdist",
        "input|prop_door_rotating|fademindist",
        "input|prop_door_rotating|physdamagescale",
        "input|prop_door_rotating|skin",
        "input|prop_dynamic_ornament|AddHealth",
        "input|prop_dynamic_ornament|BecomeRagdoll",
        "input|prop_dynamic_ornament|Break",
        "input|prop_dynamic_ornament|Disable",
        "input|prop_dynamic_ornament|DisableCollision",
        "input|prop_dynamic_ornament|DisablePhyscannonPickup",
        "input|prop_dynamic_ornament|DisablePuntSound",
        "input|prop_dynamic_ornament|Enable",
        "input|prop_dynamic_ornament|EnableCollision",
        "input|prop_dynamic_ornament|EnablePhyscannonPickup",
        "input|prop_dynamic_ornament|EnablePuntSound",
        "input|prop_dynamic_ornament|Ignite",
        "input|prop_dynamic_ornament|IgniteHitboxFireScale",
        "input|prop_dynamic_ornament|IgniteLifetime",
        "input|prop_dynamic_ornament|IgniteNumHitboxFires",
        "input|prop_dynamic_ornament|RemoveHealth",
        "input|prop_dynamic_ornament|SetAnimation",
        "input|prop_dynamic_ornament|SetBodyGroup",
        "input|prop_dynamic_ornament|SetCycle",
        "input|prop_dynamic_ornament|SetDefaultAnimation",
        "input|prop_dynamic_ornament|SetHealth",
        "input|prop_dynamic_ornament|SetLightingOrigin",
        "input|prop_dynamic_ornament|SetLightingOriginHack",
        "input|prop_dynamic_ornament|SetModel",
        "input|prop_dynamic_ornament|SetModelScale",
        "input|prop_dynamic_ornament|SetPlaybackRate",
        "input|prop_dynamic_ornament|TurnOff",
        "input|prop_dynamic_ornament|TurnOn",
        "input|prop_dynamic_ornament|fademaxdist",
        "input|prop_dynamic_ornament|fademindist",
        "input|prop_dynamic_ornament|physdamagescale",
        "input|prop_dynamic_ornament|skin",
        "input|prop_dynamic_override|AddHealth",
        "input|prop_dynamic_override|BecomeRagdoll",
        "input|prop_dynamic_override|Break",
        "input|prop_dynamic_override|DisablePhyscannonPickup",
        "input|prop_dynamic_override|DisablePuntSound",
        "input|prop_dynamic_override|EnablePhyscannonPickup",
        "input|prop_dynamic_override|EnablePuntSound",
        "input|prop_dynamic_override|Ignite",
        "input|prop_dynamic_override|IgniteHitboxFireScale",
        "input|prop_dynamic_override|IgniteLifetime",
        "input|prop_dynamic_override|IgniteNumHitboxFires",
        "input|prop_dynamic_override|RemoveHealth",
        "input|prop_dynamic_override|SetBodyGroup",
        "input|prop_dynamic_override|SetCycle",
        "input|prop_dynamic_override|SetHealth",
        "input|prop_dynamic_override|SetLightingOrigin",
        "input|prop_dynamic_override|SetLightingOriginHack",
        "input|prop_dynamic_override|SetModel",
        "input|prop_dynamic_override|SetModelScale",
        "input|prop_dynamic_override|SetPlaybackRate",
        "input|prop_dynamic_override|fademaxdist",
        "input|prop_dynamic_override|fademindist",
        "input|prop_dynamic_override|physdamagescale",
        "input|prop_dynamic_override|skin",
        "input|prop_dynamic|AddHealth",
        "input|prop_dynamic|BecomeRagdoll",
        "input|prop_dynamic|Break",
        "input|prop_dynamic|DisablePhyscannonPickup",
        "input|prop_dynamic|DisablePuntSound",
        "input|prop_dynamic|EnablePhyscannonPickup",
        "input|prop_dynamic|EnablePuntSound",
        "input|prop_dynamic|Ignite",
        "input|prop_dynamic|IgniteHitboxFireScale",
        "input|prop_dynamic|IgniteLifetime",
        "input|prop_dynamic|IgniteNumHitboxFires",
        "input|prop_dynamic|RemoveHealth",
        "input|prop_dynamic|SetBodyGroup",
        "input|prop_dynamic|SetCycle",
        "input|prop_dynamic|SetHealth",
        "input|prop_dynamic|SetLightingOrigin",
        "input|prop_dynamic|SetLightingOriginHack",
        "input|prop_dynamic|SetModel",
        "input|prop_dynamic|SetModelScale",
        "input|prop_dynamic|SetPlaybackRate",
        "input|prop_dynamic|fademaxdist",
        "input|prop_dynamic|fademindist",
        "input|prop_dynamic|physdamagescale",
        "input|prop_dynamic|skin",
        "input|prop_physics_multiplayer|AddHealth",
        "input|prop_physics_multiplayer|BecomeRagdoll",
        "input|prop_physics_multiplayer|DisableFloating",
        "input|prop_physics_multiplayer|DisablePhyscannonPickup",
        "input|prop_physics_multiplayer|DisablePuntSound",
        "input|prop_physics_multiplayer|EnablePhyscannonPickup",
        "input|prop_physics_multiplayer|EnablePuntSound",
        "input|prop_physics_multiplayer|Ignite",
        "input|prop_physics_multiplayer|IgniteHitboxFireScale",
        "input|prop_physics_multiplayer|IgniteLifetime",
        "input|prop_physics_multiplayer|IgniteNumHitboxFires",
        "input|prop_physics_multiplayer|RemoveHealth",
        "input|prop_physics_multiplayer|SetBodyGroup",
        "input|prop_physics_multiplayer|SetCycle",
        "input|prop_physics_multiplayer|SetLightingOrigin",
        "input|prop_physics_multiplayer|SetLightingOriginHack",
        "input|prop_physics_multiplayer|SetModel",
        "input|prop_physics_multiplayer|SetModelScale",
        "input|prop_physics_multiplayer|SetPlaybackRate",
        "input|prop_physics_multiplayer|fademaxdist",
        "input|prop_physics_multiplayer|fademindist",
        "input|prop_physics_multiplayer|skin",
        "input|prop_physics_override|AddHealth",
        "input|prop_physics_override|BecomeRagdoll",
        "input|prop_physics_override|DisableFloating",
        "input|prop_physics_override|DisablePhyscannonPickup",
        "input|prop_physics_override|DisablePuntSound",
        "input|prop_physics_override|EnablePhyscannonPickup",
        "input|prop_physics_override|EnablePuntSound",
        "input|prop_physics_override|Ignite",
        "input|prop_physics_override|IgniteHitboxFireScale",
        "input|prop_physics_override|IgniteLifetime",
        "input|prop_physics_override|IgniteNumHitboxFires",
        "input|prop_physics_override|RemoveHealth",
        "input|prop_physics_override|SetBodyGroup",
        "input|prop_physics_override|SetCycle",
        "input|prop_physics_override|SetLightingOrigin",
        "input|prop_physics_override|SetLightingOriginHack",
        "input|prop_physics_override|SetModel",
        "input|prop_physics_override|SetModelScale",
        "input|prop_physics_override|SetPlaybackRate",
        "input|prop_physics_override|fademaxdist",
        "input|prop_physics_override|fademindist",
        "input|prop_physics_override|skin",
        "input|prop_physics_respawnable|AddHealth",
        "input|prop_physics_respawnable|BecomeRagdoll",
        "input|prop_physics_respawnable|DisableFloating",
        "input|prop_physics_respawnable|DisablePhyscannonPickup",
        "input|prop_physics_respawnable|DisablePuntSound",
        "input|prop_physics_respawnable|EnablePhyscannonPickup",
        "input|prop_physics_respawnable|EnablePuntSound",
        "input|prop_physics_respawnable|Ignite",
        "input|prop_physics_respawnable|IgniteHitboxFireScale",
        "input|prop_physics_respawnable|IgniteLifetime",
        "input|prop_physics_respawnable|IgniteNumHitboxFires",
        "input|prop_physics_respawnable|RemoveHealth",
        "input|prop_physics_respawnable|SetBodyGroup",
        "input|prop_physics_respawnable|SetCycle",
        "input|prop_physics_respawnable|SetLightingOrigin",
        "input|prop_physics_respawnable|SetLightingOriginHack",
        "input|prop_physics_respawnable|SetModel",
        "input|prop_physics_respawnable|SetModelScale",
        "input|prop_physics_respawnable|SetPlaybackRate",
        "input|prop_physics_respawnable|fademaxdist",
        "input|prop_physics_respawnable|fademindist",
        "input|prop_physics_respawnable|skin",
        "input|prop_physics|AddHealth",
        "input|prop_physics|BecomeRagdoll",
        "input|prop_physics|DisableFloating",
        "input|prop_physics|DisablePhyscannonPickup",
        "input|prop_physics|DisablePuntSound",
        "input|prop_physics|EnablePhyscannonPickup",
        "input|prop_physics|EnablePuntSound",
        "input|prop_physics|Ignite",
        "input|prop_physics|IgniteHitboxFireScale",
        "input|prop_physics|IgniteLifetime",
        "input|prop_physics|IgniteNumHitboxFires",
        "input|prop_physics|RemoveHealth",
        "input|prop_physics|SetBodyGroup",
        "input|prop_physics|SetCycle",
        "input|prop_physics|SetLightingOrigin",
        "input|prop_physics|SetLightingOriginHack",
        "input|prop_physics|SetModel",
        "input|prop_physics|SetModelScale",
        "input|prop_physics|SetPlaybackRate",
        "input|prop_physics|fademaxdist",
        "input|prop_physics|fademindist",
        "input|prop_physics|skin",
        "input|prop_sphere|AddHealth",
        "input|prop_sphere|BecomeRagdoll",
        "input|prop_sphere|Break",
        "input|prop_sphere|DisableFloating",
        "input|prop_sphere|DisableMotion",
        "input|prop_sphere|DisablePhyscannonPickup",
        "input|prop_sphere|DisablePuntSound",
        "input|prop_sphere|EnableMotion",
        "input|prop_sphere|EnablePhyscannonPickup",
        "input|prop_sphere|EnablePuntSound",
        "input|prop_sphere|Ignite",
        "input|prop_sphere|IgniteHitboxFireScale",
        "input|prop_sphere|IgniteLifetime",
        "input|prop_sphere|IgniteNumHitboxFires",
        "input|prop_sphere|RemoveHealth",
        "input|prop_sphere|SetBodyGroup",
        "input|prop_sphere|SetCycle",
        "input|prop_sphere|SetHealth",
        "input|prop_sphere|SetLightingOrigin",
        "input|prop_sphere|SetLightingOriginHack",
        "input|prop_sphere|SetModel",
        "input|prop_sphere|SetModelScale",
        "input|prop_sphere|SetPlaybackRate",
        "input|prop_sphere|Sleep",
        "input|prop_sphere|Wake",
        "input|prop_sphere|fademaxdist",
        "input|prop_sphere|fademindist",
        "input|prop_sphere|physdamagescale",
        "input|prop_sphere|skin",
        "input|rpg_missile|BecomeRagdoll",
        "input|rpg_missile|Ignite",
        "input|rpg_missile|IgniteHitboxFireScale",
        "input|rpg_missile|IgniteLifetime",
        "input|rpg_missile|IgniteNumHitboxFires",
        "input|rpg_missile|KilledNPC",
        "input|rpg_missile|SetBodyGroup",
        "input|rpg_missile|SetCycle",
        "input|rpg_missile|SetLightingOrigin",
        "input|rpg_missile|SetLightingOriginHack",
        "input|rpg_missile|SetModel",
        "input|rpg_missile|SetModelScale",
        "input|rpg_missile|SetPlaybackRate",
        "input|rpg_missile|fademaxdist",
        "input|rpg_missile|fademindist",
        "input|rpg_missile|physdamagescale",
        "input|rpg_missile|skin",
        "input|simple_physics_prop|BecomeRagdoll",
        "input|simple_physics_prop|Ignite",
        "input|simple_physics_prop|IgniteHitboxFireScale",
        "input|simple_physics_prop|IgniteLifetime",
        "input|simple_physics_prop|IgniteNumHitboxFires",
        "input|simple_physics_prop|SetBodyGroup",
        "input|simple_physics_prop|SetCycle",
        "input|simple_physics_prop|SetLightingOrigin",
        "input|simple_physics_prop|SetLightingOriginHack",
        "input|simple_physics_prop|SetModel",
        "input|simple_physics_prop|SetModelScale",
        "input|simple_physics_prop|SetPlaybackRate",
        "input|simple_physics_prop|fademaxdist",
        "input|simple_physics_prop|fademindist",
        "input|simple_physics_prop|skin",
        "input|trigger_apply_impulse|DisableAndEndTouch",
        "input|trigger_apply_impulse|EndTouch",
        "input|trigger_apply_impulse|StartTouch",
        "input|trigger_apply_impulse|TouchTest",
        "input|trigger_cdaudio|DisableAndEndTouch",
        "input|trigger_cdaudio|EndTouch",
        "input|trigger_cdaudio|StartTouch",
        "input|trigger_cdaudio|TouchTest",
        "input|trigger_gravity|DisableAndEndTouch",
        "input|trigger_gravity|EndTouch",
        "input|trigger_gravity|StartTouch",
        "input|trigger_gravity|TouchTest",
        "input|trigger_hurt|DisableAndEndTouch",
        "input|trigger_hurt|EndTouch",
        "input|trigger_hurt|StartTouch",
        "input|trigger_hurt|TouchTest",
        "input|trigger_impact|DisableAndEndTouch",
        "input|trigger_impact|EndTouch",
        "input|trigger_impact|StartTouch",
        "input|trigger_impact|TouchTest",
        "input|trigger_look|DisableAndEndTouch",
        "input|trigger_look|EndTouch",
        "input|trigger_look|FieldOfView",
        "input|trigger_look|LookTime",
        "input|trigger_look|StartTouch",
        "input|trigger_look|TouchTest",
        "input|trigger_multiple|DisableAndEndTouch",
        "input|trigger_multiple|EndTouch",
        "input|trigger_multiple|StartTouch",
        "input|trigger_multiple|TouchTest",
        "input|trigger_once|DisableAndEndTouch",
        "input|trigger_once|EndTouch",
        "input|trigger_once|StartTouch",
        "input|trigger_once|TouchTest",
        "input|trigger_playermovement|DisableAndEndTouch",
        "input|trigger_playermovement|EndTouch",
        "input|trigger_playermovement|StartTouch",
        "input|trigger_playermovement|TouchTest",
        "input|trigger_proximity|DisableAndEndTouch",
        "input|trigger_proximity|EndTouch",
        "input|trigger_proximity|StartTouch",
        "input|trigger_proximity|TouchTest",
        "input|trigger_push|DisableAndEndTouch",
        "input|trigger_push|EndTouch",
        "input|trigger_push|StartTouch",
        "input|trigger_push|TouchTest",
        "input|trigger_remove|DisableAndEndTouch",
        "input|trigger_remove|EndTouch",
        "input|trigger_remove|StartTouch",
        "input|trigger_remove|TouchTest",
        "input|trigger_serverragdoll|DisableAndEndTouch",
        "input|trigger_serverragdoll|EndTouch",
        "input|trigger_serverragdoll|StartTouch",
        "input|trigger_serverragdoll|TouchTest",
        "input|trigger_soundscape|DisableAndEndTouch",
        "input|trigger_soundscape|EndTouch",
        "input|trigger_soundscape|StartTouch",
        "input|trigger_soundscape|TouchTest",
        "input|trigger_teleport_relative|DisableAndEndTouch",
        "input|trigger_teleport_relative|EndTouch",
        "input|trigger_teleport_relative|StartTouch",
        "input|trigger_teleport_relative|TouchTest",
        "input|trigger_teleport|DisableAndEndTouch",
        "input|trigger_teleport|EndTouch",
        "input|trigger_teleport|StartTouch",
        "input|trigger_teleport|TouchTest",
        "input|trigger|Disable",
        "input|trigger|DisableAndEndTouch",
        "input|trigger|Enable",
        "input|trigger|EndTouch",
        "input|trigger|StartTouch",
        "input|trigger|Toggle",
        "input|trigger|TouchTest",
        "input|weapon_357|HideWeapon",
        "input|weapon_ar2|HideWeapon",
        "input|weapon_crossbow|HideWeapon",
        "input|weapon_crowbar|HideWeapon",
        "input|weapon_frag|HideWeapon",
        "input|weapon_pistol|HideWeapon",
        "input|weapon_rpg|HideWeapon",
        "input|weapon_shotgun|HideWeapon",
        "input|weapon_smg1|HideWeapon",
        "key|color_correction_volume|enabled",
        "key|color_correction_volume|filtername",
        "key|color_correction|enabled",
        "key|crossbow_bolt|LightingOrigin",
        "key|crossbow_bolt|LightingOriginHack",
        "key|crossbow_bolt|Relationship",
        "key|crossbow_bolt|SetBodyGroup",
        "key|crossbow_bolt|body",
        "key|crossbow_bolt|cycle",
        "key|crossbow_bolt|fademaxdist",
        "key|crossbow_bolt|fademindist",
        "key|crossbow_bolt|fadescale",
        "key|crossbow_bolt|hitboxset",
        "key|crossbow_bolt|modelscale",
        "key|crossbow_bolt|physdamagescale",
        "key|crossbow_bolt|playbackrate",
        "key|crossbow_bolt|sequence",
        "key|crossbow_bolt|skin",
        "key|dynamic_prop|BreakModelMessage",
        "key|dynamic_prop|DisableBoneFollowers",
        "key|dynamic_prop|ExplodeDamage",
        "key|dynamic_prop|ExplodeRadius",
        "key|dynamic_prop|LightingOrigin",
        "key|dynamic_prop|LightingOriginHack",
        "key|dynamic_prop|MaxAnimTime",
        "key|dynamic_prop|MinAnimTime",
        "key|dynamic_prop|PerformanceMode",
        "key|dynamic_prop|PressureDelay",
        "key|dynamic_prop|RandomAnimation",
        "key|dynamic_prop|SetBodyGroup",
        "key|dynamic_prop|body",
        "key|dynamic_prop|cycle",
        "key|dynamic_prop|fademaxdist",
        "key|dynamic_prop|fademindist",
        "key|dynamic_prop|fadescale",
        "key|dynamic_prop|hitboxset",
        "key|dynamic_prop|minhealthdmg",
        "key|dynamic_prop|modelscale",
        "key|dynamic_prop|physdamagescale",
        "key|dynamic_prop|playbackrate",
        "key|dynamic_prop|puntsound",
        "key|dynamic_prop|sequence",
        "key|dynamic_prop|skin",
        "key|entityflame|lifetime",
        "key|env_beam|ScrollSpeed",
        "key|env_explosion|ignoredEntity",
        "key|env_glow|SetScale",
        "key|env_glow|frame",
        "key|env_gunfire|tracertype",
        "key|env_laser|ScrollSpeed",
        "key|env_microphone|speaker_dsp_preset",
        "key|env_shooter|LightingOrigin",
        "key|env_shooter|massoverride",
        "key|env_shooter|nogibshadows",
        // env_sprite_oriented's two lines went when the fork deleted
        // `CSpriteOriented : public CSprite` from game/shared/Sprite.h (lane
        // the-close 2/N) and the farm-read scrape lost the chain -- the "a
        // truncated stock side reports FEWER differences" hazard. The diff reads
        // the pristine upstream clone now (ContractDiffOracleTests), where the
        // declaration is intact, so they are back.
        "key|env_sprite_oriented|SetScale",
        "key|env_sprite_oriented|frame",
        "key|env_spritetrail|SetScale",
        "key|env_spritetrail|frame",
        "key|env_sprite|SetScale",
        "key|env_sprite|frame",
        "key|func_breakable_surf|PerformanceMode",
        "key|func_breakable_surf|PressureDelay",
        "key|func_breakable_surf|explodemagnitude",
        "key|func_breakable_surf|explosion",
        "key|func_breakable_surf|gibdir",
        "key|func_breakable_surf|physdamagescale",
        "key|func_breakable_surf|team_number",
        "key|func_breakable|PerformanceMode",
        "key|func_breakable|PressureDelay",
        "key|func_breakable|explodemagnitude",
        "key|func_breakable|explosion",
        "key|func_breakable|gibdir",
        "key|func_breakable|physdamagescale",
        "key|func_breakable|team_number",
        "key|func_brush|excludednpc",
        "key|func_brush|invert_exclusion",
        "key|func_brush|solidbsp",
        "key|func_button|movedir",
        "key|func_button|sounds",
        "key|func_clip_vphysics|filtername",
        "key|func_door_rotating|WaveHeight",
        "key|func_door_rotating|chainstodoor",
        "key|func_door_rotating|closesound",
        "key|func_door_rotating|dmg",
        "key|func_door_rotating|filtername",
        "key|func_door_rotating|forceclosed",
        "key|func_door_rotating|ignoredebris",
        "key|func_door_rotating|locked_sound",
        "key|func_door_rotating|loopmovesound",
        "key|func_door_rotating|movedir",
        "key|func_door_rotating|solidbsp",
        "key|func_door_rotating|spawnpos",
        "key|func_door_rotating|startclosesound",
        "key|func_door_rotating|unlocked_sound",
        "key|func_door|WaveHeight",
        "key|func_door|chainstodoor",
        "key|func_door|closesound",
        "key|func_door|dmg",
        "key|func_door|filtername",
        "key|func_door|forceclosed",
        "key|func_door|ignoredebris",
        "key|func_door|locked_sound",
        "key|func_door|loopmovesound",
        "key|func_door|spawnpos",
        "key|func_door|startclosesound",
        "key|func_door|unlocked_sound",
        "key|func_movelinear|BlockDamage",
        "key|func_movelinear|StartSound",
        "key|func_movelinear|StopSound",
        "key|func_physbox_multiplayer|Damagetype",
        "key|func_physbox_multiplayer|PerformanceMode",
        "key|func_physbox_multiplayer|PressureDelay",
        "key|func_physbox_multiplayer|explodemagnitude",
        "key|func_physbox_multiplayer|explosion",
        "key|func_physbox_multiplayer|forcetoenablemotion",
        "key|func_physbox_multiplayer|gibdir",
        "key|func_physbox_multiplayer|overridescript",
        "key|func_physbox_multiplayer|physdamagescale",
        "key|func_physbox_multiplayer|team_number",
        "key|func_physbox|Damagetype",
        "key|func_physbox|PerformanceMode",
        "key|func_physbox|PressureDelay",
        "key|func_physbox|explodemagnitude",
        "key|func_physbox|explosion",
        "key|func_physbox|forcetoenablemotion",
        "key|func_physbox|gibdir",
        "key|func_physbox|overridescript",
        "key|func_physbox|physdamagescale",
        "key|func_physbox|team_number",
        "key|func_platrot|lip",
        "key|func_platrot|movesnd",
        "key|func_platrot|stopsnd",
        "key|func_platrot|wait",
        "key|func_plat|lip",
        "key|func_plat|movesnd",
        "key|func_plat|noise1",
        "key|func_plat|noise2",
        "key|func_plat|stopsnd",
        "key|func_plat|volume",
        "key|func_plat|wait",
        "key|func_pushable|PerformanceMode",
        "key|func_pushable|PressureDelay",
        "key|func_pushable|explodemagnitude",
        "key|func_pushable|explosion",
        "key|func_pushable|gibdir",
        "key|func_pushable|physdamagescale",
        "key|func_pushable|team_number",
        "key|func_reflective_glass|excludednpc",
        "key|func_reflective_glass|invert_exclusion",
        "key|func_reflective_glass|solidbsp",
        "key|func_rot_button|movedir",
        "key|func_rot_button|sounds",
        "key|func_rotating|dmg",
        "key|func_rotating|message",
        "key|func_rotating|solidbsp",
        "key|func_smokevolume|SetDensity",
        "key|func_smokevolume|SetMovementSpeed",
        "key|func_smokevolume|SetRotationSpeed",
        "key|func_trackautochange|lip",
        "key|func_trackautochange|movesnd",
        "key|func_trackautochange|stopsnd",
        "key|func_trackautochange|wait",
        "key|func_trackchange|lip",
        "key|func_trackchange|movesnd",
        "key|func_trackchange|stopsnd",
        "key|func_trackchange|wait",
        "key|func_train|height",
        "key|func_train|lip",
        "key|func_train|movesnd",
        "key|func_train|stopsnd",
        "key|func_train|wait",
        "key|func_water_analog|BlockDamage",
        "key|func_water_analog|StartSound",
        "key|func_water_analog|StopSound",
        "key|func_water|WaveHeight",
        "key|func_water|chainstodoor",
        "key|func_water|closesound",
        "key|func_water|dmg",
        "key|func_water|filtername",
        "key|func_water|forceclosed",
        "key|func_water|ignoredebris",
        "key|func_water|locked_sound",
        "key|func_water|loopmovesound",
        "key|func_water|spawnpos",
        "key|func_water|startclosesound",
        "key|func_water|unlocked_sound",
        "key|game_end|master",
        "key|game_player_equip|master",
        "key|game_player_team|master",
        "key|game_score|master",
        "key|game_text|master",
        "key|game_zone_player|master",
        "key|gibshooter|LightingOrigin",
        "key|gibshooter|nogibshadows",
        "key|gib|LightingOrigin",
        "key|gib|LightingOriginHack",
        "key|gib|SetBodyGroup",
        "key|gib|body",
        "key|gib|cycle",
        "key|gib|fademaxdist",
        "key|gib|fademindist",
        "key|gib|fadescale",
        "key|gib|hitboxset",
        "key|gib|modelscale",
        "key|gib|playbackrate",
        "key|gib|sequence",
        "key|gib|skin",
        "key|info_constraint_anchor|massScale",
        "key|info_player_deathmatch|master",
        "key|item_ammo_357_large|LightingOrigin",
        "key|item_ammo_357_large|LightingOriginHack",
        "key|item_ammo_357_large|SetBodyGroup",
        "key|item_ammo_357_large|body",
        "key|item_ammo_357_large|cycle",
        "key|item_ammo_357_large|fademaxdist",
        "key|item_ammo_357_large|fademindist",
        "key|item_ammo_357_large|fadescale",
        "key|item_ammo_357_large|hitboxset",
        "key|item_ammo_357_large|modelscale",
        "key|item_ammo_357_large|playbackrate",
        "key|item_ammo_357_large|sequence",
        "key|item_ammo_357_large|skin",
        "key|item_ammo_357|LightingOrigin",
        "key|item_ammo_357|LightingOriginHack",
        "key|item_ammo_357|SetBodyGroup",
        "key|item_ammo_357|body",
        "key|item_ammo_357|cycle",
        "key|item_ammo_357|fademaxdist",
        "key|item_ammo_357|fademindist",
        "key|item_ammo_357|fadescale",
        "key|item_ammo_357|hitboxset",
        "key|item_ammo_357|modelscale",
        "key|item_ammo_357|playbackrate",
        "key|item_ammo_357|sequence",
        "key|item_ammo_357|skin",
        "key|item_ammo_ar2_altfire|LightingOrigin",
        "key|item_ammo_ar2_altfire|LightingOriginHack",
        "key|item_ammo_ar2_altfire|SetBodyGroup",
        "key|item_ammo_ar2_altfire|body",
        "key|item_ammo_ar2_altfire|cycle",
        "key|item_ammo_ar2_altfire|fademaxdist",
        "key|item_ammo_ar2_altfire|fademindist",
        "key|item_ammo_ar2_altfire|fadescale",
        "key|item_ammo_ar2_altfire|hitboxset",
        "key|item_ammo_ar2_altfire|modelscale",
        "key|item_ammo_ar2_altfire|playbackrate",
        "key|item_ammo_ar2_altfire|sequence",
        "key|item_ammo_ar2_altfire|skin",
        "key|item_ammo_ar2_large|LightingOrigin",
        "key|item_ammo_ar2_large|LightingOriginHack",
        "key|item_ammo_ar2_large|SetBodyGroup",
        "key|item_ammo_ar2_large|body",
        "key|item_ammo_ar2_large|cycle",
        "key|item_ammo_ar2_large|fademaxdist",
        "key|item_ammo_ar2_large|fademindist",
        "key|item_ammo_ar2_large|fadescale",
        "key|item_ammo_ar2_large|hitboxset",
        "key|item_ammo_ar2_large|modelscale",
        "key|item_ammo_ar2_large|playbackrate",
        "key|item_ammo_ar2_large|sequence",
        "key|item_ammo_ar2_large|skin",
        "key|item_ammo_ar2|LightingOrigin",
        "key|item_ammo_ar2|LightingOriginHack",
        "key|item_ammo_ar2|SetBodyGroup",
        "key|item_ammo_ar2|body",
        "key|item_ammo_ar2|cycle",
        "key|item_ammo_ar2|fademaxdist",
        "key|item_ammo_ar2|fademindist",
        "key|item_ammo_ar2|fadescale",
        "key|item_ammo_ar2|hitboxset",
        "key|item_ammo_ar2|modelscale",
        "key|item_ammo_ar2|playbackrate",
        "key|item_ammo_ar2|sequence",
        "key|item_ammo_ar2|skin",
        "key|item_ammo_pistol_large|LightingOrigin",
        "key|item_ammo_pistol_large|LightingOriginHack",
        "key|item_ammo_pistol_large|SetBodyGroup",
        "key|item_ammo_pistol_large|body",
        "key|item_ammo_pistol_large|cycle",
        "key|item_ammo_pistol_large|fademaxdist",
        "key|item_ammo_pistol_large|fademindist",
        "key|item_ammo_pistol_large|fadescale",
        "key|item_ammo_pistol_large|hitboxset",
        "key|item_ammo_pistol_large|modelscale",
        "key|item_ammo_pistol_large|playbackrate",
        "key|item_ammo_pistol_large|sequence",
        "key|item_ammo_pistol_large|skin",
        "key|item_ammo_pistol|LightingOrigin",
        "key|item_ammo_pistol|LightingOriginHack",
        "key|item_ammo_pistol|SetBodyGroup",
        "key|item_ammo_pistol|body",
        "key|item_ammo_pistol|cycle",
        "key|item_ammo_pistol|fademaxdist",
        "key|item_ammo_pistol|fademindist",
        "key|item_ammo_pistol|fadescale",
        "key|item_ammo_pistol|hitboxset",
        "key|item_ammo_pistol|modelscale",
        "key|item_ammo_pistol|playbackrate",
        "key|item_ammo_pistol|sequence",
        "key|item_ammo_pistol|skin",
        "key|item_ammo_smg1_grenade|LightingOrigin",
        "key|item_ammo_smg1_grenade|LightingOriginHack",
        "key|item_ammo_smg1_grenade|SetBodyGroup",
        "key|item_ammo_smg1_grenade|body",
        "key|item_ammo_smg1_grenade|cycle",
        "key|item_ammo_smg1_grenade|fademaxdist",
        "key|item_ammo_smg1_grenade|fademindist",
        "key|item_ammo_smg1_grenade|fadescale",
        "key|item_ammo_smg1_grenade|hitboxset",
        "key|item_ammo_smg1_grenade|modelscale",
        "key|item_ammo_smg1_grenade|playbackrate",
        "key|item_ammo_smg1_grenade|sequence",
        "key|item_ammo_smg1_grenade|skin",
        "key|item_ammo_smg1_large|LightingOrigin",
        "key|item_ammo_smg1_large|LightingOriginHack",
        "key|item_ammo_smg1_large|SetBodyGroup",
        "key|item_ammo_smg1_large|body",
        "key|item_ammo_smg1_large|cycle",
        "key|item_ammo_smg1_large|fademaxdist",
        "key|item_ammo_smg1_large|fademindist",
        "key|item_ammo_smg1_large|fadescale",
        "key|item_ammo_smg1_large|hitboxset",
        "key|item_ammo_smg1_large|modelscale",
        "key|item_ammo_smg1_large|playbackrate",
        "key|item_ammo_smg1_large|sequence",
        "key|item_ammo_smg1_large|skin",
        "key|item_ammo_smg1|LightingOrigin",
        "key|item_ammo_smg1|LightingOriginHack",
        "key|item_ammo_smg1|SetBodyGroup",
        "key|item_ammo_smg1|body",
        "key|item_ammo_smg1|cycle",
        "key|item_ammo_smg1|fademaxdist",
        "key|item_ammo_smg1|fademindist",
        "key|item_ammo_smg1|fadescale",
        "key|item_ammo_smg1|hitboxset",
        "key|item_ammo_smg1|modelscale",
        "key|item_ammo_smg1|playbackrate",
        "key|item_ammo_smg1|sequence",
        "key|item_ammo_smg1|skin",
        "key|item_battery|LightingOrigin",
        "key|item_battery|LightingOriginHack",
        "key|item_battery|SetBodyGroup",
        "key|item_battery|body",
        "key|item_battery|cycle",
        "key|item_battery|fademaxdist",
        "key|item_battery|fademindist",
        "key|item_battery|fadescale",
        "key|item_battery|hitboxset",
        "key|item_battery|modelscale",
        "key|item_battery|playbackrate",
        "key|item_battery|sequence",
        "key|item_battery|skin",
        "key|item_box_buckshot|LightingOrigin",
        "key|item_box_buckshot|LightingOriginHack",
        "key|item_box_buckshot|SetBodyGroup",
        "key|item_box_buckshot|body",
        "key|item_box_buckshot|cycle",
        "key|item_box_buckshot|fademaxdist",
        "key|item_box_buckshot|fademindist",
        "key|item_box_buckshot|fadescale",
        "key|item_box_buckshot|hitboxset",
        "key|item_box_buckshot|modelscale",
        "key|item_box_buckshot|playbackrate",
        "key|item_box_buckshot|sequence",
        "key|item_box_buckshot|skin",
        "key|item_healthcharger|LightingOrigin",
        "key|item_healthcharger|LightingOriginHack",
        "key|item_healthcharger|SetBodyGroup",
        "key|item_healthcharger|body",
        "key|item_healthcharger|cycle",
        "key|item_healthcharger|fademaxdist",
        "key|item_healthcharger|fademindist",
        "key|item_healthcharger|fadescale",
        "key|item_healthcharger|hitboxset",
        "key|item_healthcharger|modelscale",
        "key|item_healthcharger|playbackrate",
        "key|item_healthcharger|sequence",
        "key|item_healthcharger|skin",
        "key|item_healthkit|LightingOrigin",
        "key|item_healthkit|LightingOriginHack",
        "key|item_healthkit|SetBodyGroup",
        "key|item_healthkit|body",
        "key|item_healthkit|cycle",
        "key|item_healthkit|fademaxdist",
        "key|item_healthkit|fademindist",
        "key|item_healthkit|fadescale",
        "key|item_healthkit|hitboxset",
        "key|item_healthkit|modelscale",
        "key|item_healthkit|playbackrate",
        "key|item_healthkit|sequence",
        "key|item_healthkit|skin",
        "key|item_healthvial|LightingOrigin",
        "key|item_healthvial|LightingOriginHack",
        "key|item_healthvial|SetBodyGroup",
        "key|item_healthvial|body",
        "key|item_healthvial|cycle",
        "key|item_healthvial|fademaxdist",
        "key|item_healthvial|fademindist",
        "key|item_healthvial|fadescale",
        "key|item_healthvial|hitboxset",
        "key|item_healthvial|modelscale",
        "key|item_healthvial|playbackrate",
        "key|item_healthvial|sequence",
        "key|item_healthvial|skin",
        "key|item_rpg_round|LightingOrigin",
        "key|item_rpg_round|LightingOriginHack",
        "key|item_rpg_round|SetBodyGroup",
        "key|item_rpg_round|body",
        "key|item_rpg_round|cycle",
        "key|item_rpg_round|fademaxdist",
        "key|item_rpg_round|fademindist",
        "key|item_rpg_round|fadescale",
        "key|item_rpg_round|hitboxset",
        "key|item_rpg_round|modelscale",
        "key|item_rpg_round|playbackrate",
        "key|item_rpg_round|sequence",
        "key|item_rpg_round|skin",
        "key|item_sodacan|LightingOrigin",
        "key|item_sodacan|LightingOriginHack",
        "key|item_sodacan|SetBodyGroup",
        "key|item_sodacan|body",
        "key|item_sodacan|cycle",
        "key|item_sodacan|fademaxdist",
        "key|item_sodacan|fademindist",
        "key|item_sodacan|fadescale",
        "key|item_sodacan|hitboxset",
        "key|item_sodacan|modelscale",
        "key|item_sodacan|playbackrate",
        "key|item_sodacan|sequence",
        "key|item_sodacan|skin",
        "key|item_suitcharger|LightingOrigin",
        "key|item_suitcharger|LightingOriginHack",
        "key|item_suitcharger|SetBodyGroup",
        "key|item_suitcharger|body",
        "key|item_suitcharger|cycle",
        "key|item_suitcharger|fademaxdist",
        "key|item_suitcharger|fademindist",
        "key|item_suitcharger|fadescale",
        "key|item_suitcharger|hitboxset",
        "key|item_suitcharger|modelscale",
        "key|item_suitcharger|playbackrate",
        "key|item_suitcharger|sequence",
        "key|item_suitcharger|skin",
        "key|logic_active_autosave|DangerousTime",
        "key|logic_active_autosave|MinHitPointsToCommit",
        "key|logic_active_autosave|MinimumHitPoints",
        "key|logic_active_autosave|NewLevelUnit",
        "key|logic_active_autosave|TimeToTrigger",
        "key|logic_active_autosave|TriggerHitPoints",
        "key|logic_autosave|MinHitPointsToCommit",
        "key|logic_autosave|MinimumHitPoints",
        "key|logic_autosave|NewLevelUnit",
        "key|logic_branch_listener|Branch01",
        "key|logic_branch_listener|Branch02",
        "key|logic_branch_listener|Branch03",
        "key|logic_branch_listener|Branch04",
        "key|logic_branch_listener|Branch05",
        "key|logic_branch_listener|Branch06",
        "key|logic_branch_listener|Branch07",
        "key|logic_branch_listener|Branch08",
        "key|logic_branch_listener|Branch09",
        "key|logic_branch_listener|Branch10",
        "key|logic_branch_listener|Branch11",
        "key|logic_branch_listener|Branch12",
        "key|logic_branch_listener|Branch13",
        "key|logic_branch_listener|Branch14",
        "key|logic_branch_listener|Branch15",
        "key|logic_branch_listener|Branch16",
        "key|logic_case|Case01",
        "key|logic_case|Case02",
        "key|logic_case|Case03",
        "key|logic_case|Case04",
        "key|logic_case|Case05",
        "key|logic_case|Case06",
        "key|logic_case|Case07",
        "key|logic_case|Case08",
        "key|logic_case|Case09",
        "key|logic_case|Case10",
        "key|logic_case|Case11",
        "key|logic_case|Case12",
        "key|logic_case|Case13",
        "key|logic_case|Case14",
        "key|logic_case|Case15",
        "key|logic_case|Case16",
        "key|logic_script|Group00",
        "key|logic_script|Group01",
        "key|logic_script|Group02",
        "key|logic_script|Group03",
        "key|logic_script|Group04",
        "key|logic_script|Group05",
        "key|logic_script|Group06",
        "key|logic_script|Group07",
        "key|logic_script|Group08",
        "key|logic_script|Group09",
        "key|logic_script|Group10",
        "key|logic_script|Group11",
        "key|logic_script|Group12",
        "key|logic_script|Group13",
        "key|logic_script|Group14",
        "key|logic_script|Group16",
        "key|momentary_door|BlockDamage",
        "key|momentary_door|StartSound",
        "key|momentary_door|StopSound",
        "key|momentary_rot_button|movedir",
        "key|momentary_rot_button|sounds",
        "key|npc_grenade_frag|LightingOrigin",
        "key|npc_grenade_frag|LightingOriginHack",
        "key|npc_grenade_frag|SetBodyGroup",
        "key|npc_grenade_frag|body",
        "key|npc_grenade_frag|cycle",
        "key|npc_grenade_frag|fademaxdist",
        "key|npc_grenade_frag|fademindist",
        "key|npc_grenade_frag|fadescale",
        "key|npc_grenade_frag|hitboxset",
        "key|npc_grenade_frag|modelscale",
        "key|npc_grenade_frag|playbackrate",
        "key|npc_grenade_frag|sequence",
        "key|npc_grenade_frag|skin",
        "key|phys_ballsocket|constraintsystem",
        "key|phys_ballsocket|teleportfollowdistance",
        "key|phys_constraintsystem|additionaliterations",
        "key|phys_constraint|constraintsystem",
        "key|phys_constraint|teleportfollowdistance",
        "key|phys_hinge|constraintsystem",
        "key|phys_hinge|maxSoundThreshold",
        "key|phys_hinge|minSoundThreshold",
        "key|phys_hinge|reversalsoundLarge",
        "key|phys_hinge|reversalsoundMedium",
        "key|phys_hinge|reversalsoundSmall",
        "key|phys_hinge|reversalsoundthresholdLarge",
        "key|phys_hinge|reversalsoundthresholdMedium",
        "key|phys_hinge|reversalsoundthresholdSmall",
        "key|phys_hinge|slidesoundback",
        "key|phys_hinge|slidesoundfwd",
        "key|phys_hinge|teleportfollowdistance",
        "key|phys_lengthconstraint|constraintsystem",
        "key|phys_lengthconstraint|teleportfollowdistance",
        "key|phys_magnet|LightingOrigin",
        "key|phys_magnet|LightingOriginHack",
        "key|phys_magnet|SetBodyGroup",
        "key|phys_magnet|body",
        "key|phys_magnet|cycle",
        "key|phys_magnet|fademaxdist",
        "key|phys_magnet|fademindist",
        "key|phys_magnet|fadescale",
        "key|phys_magnet|hitboxset",
        "key|phys_magnet|modelscale",
        "key|phys_magnet|overridescript",
        "key|phys_magnet|playbackrate",
        "key|phys_magnet|sequence",
        "key|phys_magnet|skin",
        "key|phys_pulleyconstraint|constraintsystem",
        "key|phys_pulleyconstraint|teleportfollowdistance",
        "key|phys_ragdollconstraint|constraintsystem",
        "key|phys_ragdollconstraint|teleportfollowdistance",
        "key|phys_slideconstraint|constraintsystem",
        "key|phys_slideconstraint|maxSoundThreshold",
        "key|phys_slideconstraint|minSoundThreshold",
        "key|phys_slideconstraint|reversalsoundLarge",
        "key|phys_slideconstraint|reversalsoundMedium",
        "key|phys_slideconstraint|reversalsoundSmall",
        "key|phys_slideconstraint|reversalsoundthresholdLarge",
        "key|phys_slideconstraint|reversalsoundthresholdMedium",
        "key|phys_slideconstraint|reversalsoundthresholdSmall",
        "key|phys_slideconstraint|slidesoundback",
        "key|phys_slideconstraint|slidesoundfwd",
        "key|phys_slideconstraint|teleportfollowdistance",
        "key|physics_cannister|LightingOrigin",
        "key|physics_cannister|LightingOriginHack",
        "key|physics_cannister|Relationship",
        "key|physics_cannister|SetBodyGroup",
        "key|physics_cannister|body",
        "key|physics_cannister|cycle",
        "key|physics_cannister|fademaxdist",
        "key|physics_cannister|fademindist",
        "key|physics_cannister|fadescale",
        "key|physics_cannister|hitboxset",
        "key|physics_cannister|modelscale",
        "key|physics_cannister|physdamagescale",
        "key|physics_cannister|playbackrate",
        "key|physics_cannister|sequence",
        "key|physics_cannister|skin",
        "key|physics_prop|BreakModelMessage",
        "key|physics_prop|Damagetype",
        "key|physics_prop|ExplodeDamage",
        "key|physics_prop|ExplodeRadius",
        "key|physics_prop|LightingOrigin",
        "key|physics_prop|LightingOriginHack",
        "key|physics_prop|PerformanceMode",
        "key|physics_prop|PressureDelay",
        "key|physics_prop|SetBodyGroup",
        "key|physics_prop|body",
        "key|physics_prop|cycle",
        "key|physics_prop|fademaxdist",
        "key|physics_prop|fademindist",
        "key|physics_prop|fadescale",
        "key|physics_prop|hitboxset",
        "key|physics_prop|inertiascale",
        "key|physics_prop|modelscale",
        "key|physics_prop|overridescript",
        "key|physics_prop|playbackrate",
        "key|physics_prop|puntsound",
        "key|physics_prop|sequence",
        "key|physics_prop|skin",
        "key|player|LightingOrigin",
        "key|player|LightingOriginHack",
        "key|player|Relationship",
        "key|player|SetBodyGroup",
        "key|player|body",
        "key|player|cycle",
        "key|player|fademaxdist",
        "key|player|fademindist",
        "key|player|fadescale",
        "key|player|hitboxset",
        "key|player|modelscale",
        "key|player|physdamagescale",
        "key|player|playbackrate",
        "key|player|sequence",
        "key|player|skin",
        "key|point_spotlight|mindxlevel",
        "key|point_template|Template01",
        "key|point_template|Template02",
        "key|point_template|Template03",
        "key|point_template|Template04",
        "key|point_template|Template05",
        "key|point_template|Template06",
        "key|point_template|Template07",
        "key|point_template|Template08",
        "key|point_template|Template09",
        "key|point_template|Template10",
        "key|point_template|Template11",
        "key|point_template|Template12",
        "key|point_template|Template13",
        "key|point_template|Template14",
        "key|point_template|Template15",
        "key|point_template|Template16",
        "key|predicted_viewmodel|LightingOrigin",
        "key|predicted_viewmodel|LightingOriginHack",
        "key|predicted_viewmodel|SetBodyGroup",
        "key|predicted_viewmodel|body",
        "key|predicted_viewmodel|cycle",
        "key|predicted_viewmodel|fademaxdist",
        "key|predicted_viewmodel|fademindist",
        "key|predicted_viewmodel|fadescale",
        "key|predicted_viewmodel|hitboxset",
        "key|predicted_viewmodel|modelscale",
        "key|predicted_viewmodel|playbackrate",
        "key|predicted_viewmodel|sequence",
        "key|predicted_viewmodel|skin",
        "key|prop_combine_ball|LightingOrigin",
        "key|prop_combine_ball|LightingOriginHack",
        "key|prop_combine_ball|SetBodyGroup",
        "key|prop_combine_ball|body",
        "key|prop_combine_ball|cycle",
        "key|prop_combine_ball|fademaxdist",
        "key|prop_combine_ball|fademindist",
        "key|prop_combine_ball|fadescale",
        "key|prop_combine_ball|hitboxset",
        "key|prop_combine_ball|modelscale",
        "key|prop_combine_ball|playbackrate",
        "key|prop_combine_ball|sequence",
        "key|prop_combine_ball|skin",
        "key|prop_door_rotating|BreakModelMessage",
        "key|prop_door_rotating|DefaultAnim",
        "key|prop_door_rotating|DisableBoneFollowers",
        "key|prop_door_rotating|ExplodeDamage",
        "key|prop_door_rotating|ExplodeRadius",
        "key|prop_door_rotating|LightingOrigin",
        "key|prop_door_rotating|LightingOriginHack",
        "key|prop_door_rotating|MaxAnimTime",
        "key|prop_door_rotating|MinAnimTime",
        "key|prop_door_rotating|PerformanceMode",
        "key|prop_door_rotating|PressureDelay",
        "key|prop_door_rotating|RandomAnimation",
        "key|prop_door_rotating|SetBodyGroup",
        "key|prop_door_rotating|StartDisabled",
        "key|prop_door_rotating|ajarangles",
        "key|prop_door_rotating|axis",
        "key|prop_door_rotating|body",
        "key|prop_door_rotating|cycle",
        "key|prop_door_rotating|fademaxdist",
        "key|prop_door_rotating|fademindist",
        "key|prop_door_rotating|fadescale",
        "key|prop_door_rotating|forceclosed",
        "key|prop_door_rotating|hardware",
        "key|prop_door_rotating|hitboxset",
        "key|prop_door_rotating|minhealthdmg",
        "key|prop_door_rotating|modelscale",
        "key|prop_door_rotating|opendir",
        "key|prop_door_rotating|physdamagescale",
        "key|prop_door_rotating|playbackrate",
        "key|prop_door_rotating|puntsound",
        "key|prop_door_rotating|sequence",
        "key|prop_door_rotating|skin",
        "key|prop_door_rotating|slavename",
        "key|prop_door_rotating|soundcloseoverride",
        "key|prop_door_rotating|soundlockedoverride",
        "key|prop_door_rotating|soundmoveoverride",
        "key|prop_door_rotating|soundopenoverride",
        "key|prop_door_rotating|soundunlockedoverride",
        "key|prop_dynamic_ornament|BreakModelMessage",
        "key|prop_dynamic_ornament|DefaultAnim",
        "key|prop_dynamic_ornament|DisableBoneFollowers",
        "key|prop_dynamic_ornament|ExplodeDamage",
        "key|prop_dynamic_ornament|ExplodeRadius",
        "key|prop_dynamic_ornament|LightingOrigin",
        "key|prop_dynamic_ornament|LightingOriginHack",
        "key|prop_dynamic_ornament|MaxAnimTime",
        "key|prop_dynamic_ornament|MinAnimTime",
        "key|prop_dynamic_ornament|PerformanceMode",
        "key|prop_dynamic_ornament|PressureDelay",
        "key|prop_dynamic_ornament|RandomAnimation",
        "key|prop_dynamic_ornament|SetBodyGroup",
        "key|prop_dynamic_ornament|StartDisabled",
        "key|prop_dynamic_ornament|body",
        "key|prop_dynamic_ornament|cycle",
        "key|prop_dynamic_ornament|fademaxdist",
        "key|prop_dynamic_ornament|fademindist",
        "key|prop_dynamic_ornament|fadescale",
        "key|prop_dynamic_ornament|hitboxset",
        "key|prop_dynamic_ornament|minhealthdmg",
        "key|prop_dynamic_ornament|modelscale",
        "key|prop_dynamic_ornament|physdamagescale",
        "key|prop_dynamic_ornament|playbackrate",
        "key|prop_dynamic_ornament|puntsound",
        "key|prop_dynamic_ornament|sequence",
        "key|prop_dynamic_ornament|skin",
        "key|prop_dynamic_override|BreakModelMessage",
        "key|prop_dynamic_override|DisableBoneFollowers",
        "key|prop_dynamic_override|ExplodeDamage",
        "key|prop_dynamic_override|ExplodeRadius",
        "key|prop_dynamic_override|LightingOrigin",
        "key|prop_dynamic_override|LightingOriginHack",
        "key|prop_dynamic_override|MaxAnimTime",
        "key|prop_dynamic_override|MinAnimTime",
        "key|prop_dynamic_override|PerformanceMode",
        "key|prop_dynamic_override|PressureDelay",
        "key|prop_dynamic_override|RandomAnimation",
        "key|prop_dynamic_override|SetBodyGroup",
        "key|prop_dynamic_override|body",
        "key|prop_dynamic_override|cycle",
        "key|prop_dynamic_override|fademaxdist",
        "key|prop_dynamic_override|fademindist",
        "key|prop_dynamic_override|fadescale",
        "key|prop_dynamic_override|hitboxset",
        "key|prop_dynamic_override|minhealthdmg",
        "key|prop_dynamic_override|modelscale",
        "key|prop_dynamic_override|physdamagescale",
        "key|prop_dynamic_override|playbackrate",
        "key|prop_dynamic_override|puntsound",
        "key|prop_dynamic_override|sequence",
        "key|prop_dynamic_override|skin",
        "key|prop_dynamic|BreakModelMessage",
        "key|prop_dynamic|DisableBoneFollowers",
        "key|prop_dynamic|ExplodeDamage",
        "key|prop_dynamic|ExplodeRadius",
        "key|prop_dynamic|LightingOrigin",
        "key|prop_dynamic|LightingOriginHack",
        "key|prop_dynamic|MaxAnimTime",
        "key|prop_dynamic|MinAnimTime",
        "key|prop_dynamic|PerformanceMode",
        "key|prop_dynamic|PressureDelay",
        "key|prop_dynamic|RandomAnimation",
        "key|prop_dynamic|SetBodyGroup",
        "key|prop_dynamic|body",
        "key|prop_dynamic|cycle",
        "key|prop_dynamic|fademaxdist",
        "key|prop_dynamic|fademindist",
        "key|prop_dynamic|fadescale",
        "key|prop_dynamic|hitboxset",
        "key|prop_dynamic|minhealthdmg",
        "key|prop_dynamic|modelscale",
        "key|prop_dynamic|physdamagescale",
        "key|prop_dynamic|playbackrate",
        "key|prop_dynamic|puntsound",
        "key|prop_dynamic|sequence",
        "key|prop_dynamic|skin",
        "key|prop_physics_multiplayer|BreakModelMessage",
        "key|prop_physics_multiplayer|Damagetype",
        "key|prop_physics_multiplayer|ExplodeDamage",
        "key|prop_physics_multiplayer|ExplodeRadius",
        "key|prop_physics_multiplayer|LightingOrigin",
        "key|prop_physics_multiplayer|LightingOriginHack",
        "key|prop_physics_multiplayer|PerformanceMode",
        "key|prop_physics_multiplayer|PressureDelay",
        "key|prop_physics_multiplayer|SetBodyGroup",
        "key|prop_physics_multiplayer|body",
        "key|prop_physics_multiplayer|cycle",
        "key|prop_physics_multiplayer|fademaxdist",
        "key|prop_physics_multiplayer|fademindist",
        "key|prop_physics_multiplayer|fadescale",
        "key|prop_physics_multiplayer|hitboxset",
        "key|prop_physics_multiplayer|inertiascale",
        "key|prop_physics_multiplayer|modelscale",
        "key|prop_physics_multiplayer|overridescript",
        "key|prop_physics_multiplayer|playbackrate",
        "key|prop_physics_multiplayer|puntsound",
        "key|prop_physics_multiplayer|sequence",
        "key|prop_physics_multiplayer|skin",
        "key|prop_physics_override|BreakModelMessage",
        "key|prop_physics_override|Damagetype",
        "key|prop_physics_override|ExplodeDamage",
        "key|prop_physics_override|ExplodeRadius",
        "key|prop_physics_override|LightingOrigin",
        "key|prop_physics_override|LightingOriginHack",
        "key|prop_physics_override|PerformanceMode",
        "key|prop_physics_override|PressureDelay",
        "key|prop_physics_override|SetBodyGroup",
        "key|prop_physics_override|body",
        "key|prop_physics_override|cycle",
        "key|prop_physics_override|fademaxdist",
        "key|prop_physics_override|fademindist",
        "key|prop_physics_override|fadescale",
        "key|prop_physics_override|hitboxset",
        "key|prop_physics_override|inertiascale",
        "key|prop_physics_override|modelscale",
        "key|prop_physics_override|overridescript",
        "key|prop_physics_override|playbackrate",
        "key|prop_physics_override|puntsound",
        "key|prop_physics_override|sequence",
        "key|prop_physics_override|skin",
        "key|prop_physics_respawnable|BreakModelMessage",
        "key|prop_physics_respawnable|Damagetype",
        "key|prop_physics_respawnable|ExplodeDamage",
        "key|prop_physics_respawnable|ExplodeRadius",
        "key|prop_physics_respawnable|LightingOrigin",
        "key|prop_physics_respawnable|LightingOriginHack",
        "key|prop_physics_respawnable|PerformanceMode",
        "key|prop_physics_respawnable|PressureDelay",
        "key|prop_physics_respawnable|SetBodyGroup",
        "key|prop_physics_respawnable|body",
        "key|prop_physics_respawnable|cycle",
        "key|prop_physics_respawnable|fademaxdist",
        "key|prop_physics_respawnable|fademindist",
        "key|prop_physics_respawnable|fadescale",
        "key|prop_physics_respawnable|hitboxset",
        "key|prop_physics_respawnable|inertiascale",
        "key|prop_physics_respawnable|modelscale",
        "key|prop_physics_respawnable|overridescript",
        "key|prop_physics_respawnable|playbackrate",
        "key|prop_physics_respawnable|puntsound",
        "key|prop_physics_respawnable|sequence",
        "key|prop_physics_respawnable|skin",
        "key|prop_physics|BreakModelMessage",
        "key|prop_physics|Damagetype",
        "key|prop_physics|ExplodeDamage",
        "key|prop_physics|ExplodeRadius",
        "key|prop_physics|LightingOrigin",
        "key|prop_physics|LightingOriginHack",
        "key|prop_physics|PerformanceMode",
        "key|prop_physics|PressureDelay",
        "key|prop_physics|SetBodyGroup",
        "key|prop_physics|body",
        "key|prop_physics|cycle",
        "key|prop_physics|fademaxdist",
        "key|prop_physics|fademindist",
        "key|prop_physics|fadescale",
        "key|prop_physics|hitboxset",
        "key|prop_physics|inertiascale",
        "key|prop_physics|modelscale",
        "key|prop_physics|overridescript",
        "key|prop_physics|playbackrate",
        "key|prop_physics|puntsound",
        "key|prop_physics|sequence",
        "key|prop_physics|skin",
        "key|prop_sphere|BreakModelMessage",
        "key|prop_sphere|Damagetype",
        "key|prop_sphere|ExplodeDamage",
        "key|prop_sphere|ExplodeRadius",
        "key|prop_sphere|LightingOrigin",
        "key|prop_sphere|LightingOriginHack",
        "key|prop_sphere|PerformanceMode",
        "key|prop_sphere|PressureDelay",
        "key|prop_sphere|SetBodyGroup",
        "key|prop_sphere|body",
        "key|prop_sphere|cycle",
        "key|prop_sphere|damagetoenablemotion",
        "key|prop_sphere|fademaxdist",
        "key|prop_sphere|fademindist",
        "key|prop_sphere|fadescale",
        "key|prop_sphere|forcetoenablemotion",
        "key|prop_sphere|hitboxset",
        "key|prop_sphere|inertiascale",
        "key|prop_sphere|massscale",
        "key|prop_sphere|minhealthdmg",
        "key|prop_sphere|modelscale",
        "key|prop_sphere|overridescript",
        "key|prop_sphere|physdamagescale",
        "key|prop_sphere|playbackrate",
        "key|prop_sphere|puntsound",
        "key|prop_sphere|sequence",
        "key|prop_sphere|skin",
        "key|rpg_missile|LightingOrigin",
        "key|rpg_missile|LightingOriginHack",
        "key|rpg_missile|Relationship",
        "key|rpg_missile|SetBodyGroup",
        "key|rpg_missile|body",
        "key|rpg_missile|cycle",
        "key|rpg_missile|fademaxdist",
        "key|rpg_missile|fademindist",
        "key|rpg_missile|fadescale",
        "key|rpg_missile|hitboxset",
        "key|rpg_missile|modelscale",
        "key|rpg_missile|physdamagescale",
        "key|rpg_missile|playbackrate",
        "key|rpg_missile|sequence",
        "key|rpg_missile|skin",
        "key|shadow_control|SetDistance",
        "key|shadow_control|SetShadowsDisabled",
        "key|simple_physics_prop|LightingOrigin",
        "key|simple_physics_prop|LightingOriginHack",
        "key|simple_physics_prop|SetBodyGroup",
        "key|simple_physics_prop|body",
        "key|simple_physics_prop|cycle",
        "key|simple_physics_prop|fademaxdist",
        "key|simple_physics_prop|fademindist",
        "key|simple_physics_prop|fadescale",
        "key|simple_physics_prop|hitboxset",
        "key|simple_physics_prop|modelscale",
        "key|simple_physics_prop|playbackrate",
        "key|simple_physics_prop|sequence",
        "key|simple_physics_prop|skin",
        "key|sky_camera|fogcolor",
        "key|sky_camera|fogcolor2",
        "key|trigger_hurt|SetDamage",
        "key|trigger_push|alternateticksfix",
        "key|trigger|StartDisabled",
        "key|trigger|filtername",
        "key|water_lod_control|SetCheapWaterEndDistance",
        "key|water_lod_control|SetCheapWaterStartDistance",
        "output|color_correction_volume|OnEndTouch",
        "output|color_correction_volume|OnEndTouchAll",
        "output|color_correction_volume|OnNotTouching",
        "output|color_correction_volume|OnStartTouch",
        "output|color_correction_volume|OnStartTouchAll",
        "output|color_correction_volume|OnTouching",
        "output|crossbow_bolt|OnIgnite",
        "output|dynamic_prop|OnBreak",
        "output|dynamic_prop|OnHealthChanged",
        "output|dynamic_prop|OnIgnite",
        "output|dynamic_prop|OnPhysCannonAnimatePostStarted",
        "output|dynamic_prop|OnPhysCannonAnimatePreStarted",
        "output|dynamic_prop|OnPhysCannonAnimatePullStarted",
        "output|dynamic_prop|OnPhysCannonDetach",
        "output|dynamic_prop|OnPhysCannonPullAnimFinished",
        "output|dynamic_prop|OnTakeDamage",
        "output|env_microphone|OnRoutedSound",
        "output|env_wind|OnGustEnd",
        "output|env_wind|OnGustStart",
        "output|func_breakable_surf|OnHealthChanged",
        "output|func_door_rotating|OnLockedUse",
        "output|func_door_rotating|OnUnblockedClosing",
        "output|func_door_rotating|OnUnblockedOpening",
        "output|func_door|OnLockedUse",
        "output|func_door|OnUnblockedClosing",
        "output|func_door|OnUnblockedOpening",
        "output|func_friction|OnEndTouch",
        "output|func_friction|OnNotTouching",
        "output|func_friction|OnStartTouchAll",
        "output|func_friction|OnTouching",
        "output|func_physbox_multiplayer|OnDamaged",
        "output|func_physbox|OnDamaged",
        "output|func_rot_button|OnDamaged",
        "output|func_water|OnLockedUse",
        "output|func_water|OnUnblockedClosing",
        "output|func_water|OnUnblockedOpening",
        "output|gib|OnIgnite",
        "output|item_ammo_357_large|OnIgnite",
        "output|item_ammo_357|OnIgnite",
        "output|item_ammo_ar2_altfire|OnIgnite",
        "output|item_ammo_ar2_large|OnIgnite",
        "output|item_ammo_ar2|OnIgnite",
        "output|item_ammo_pistol_large|OnIgnite",
        "output|item_ammo_pistol|OnIgnite",
        "output|item_ammo_smg1_grenade|OnIgnite",
        "output|item_ammo_smg1_large|OnIgnite",
        "output|item_ammo_smg1|OnIgnite",
        "output|item_battery|OnIgnite",
        "output|item_box_buckshot|OnIgnite",
        "output|item_healthcharger|OnIgnite",
        "output|item_healthkit|OnIgnite",
        "output|item_healthvial|OnIgnite",
        "output|item_rpg_round|OnIgnite",
        "output|item_sodacan|OnIgnite",
        "output|item_suitcharger|OnIgnite",
        "output|momentary_rot_button|OnDamaged",
        "output|momentary_rot_button|OnIn",
        "output|momentary_rot_button|OnOut",
        "output|npc_grenade_frag|OnIgnite",
        "output|phys_magnet|OnIgnite",
        "output|physics_cannister|OnIgnite",
        "output|physics_prop|OnIgnite",
        "output|physics_prop|OnPhysCannonAnimatePostStarted",
        "output|physics_prop|OnPhysCannonAnimatePreStarted",
        "output|physics_prop|OnPhysCannonAnimatePullStarted",
        "output|physics_prop|OnPhysCannonDetach",
        "output|physics_prop|OnPhysCannonPullAnimFinished",
        "output|physics_prop|OnTakeDamage",
        "output|player|OnIgnite",
        "output|predicted_viewmodel|OnIgnite",
        "output|prop_combine_ball|OnIgnite",
        "output|prop_door_rotating|OnAnimationBegun",
        "output|prop_door_rotating|OnAnimationDone",
        "output|prop_door_rotating|OnBreak",
        "output|prop_door_rotating|OnHealthChanged",
        "output|prop_door_rotating|OnIgnite",
        "output|prop_door_rotating|OnLockedUse",
        "output|prop_door_rotating|OnPhysCannonAnimatePostStarted",
        "output|prop_door_rotating|OnPhysCannonAnimatePreStarted",
        "output|prop_door_rotating|OnPhysCannonAnimatePullStarted",
        "output|prop_door_rotating|OnPhysCannonDetach",
        "output|prop_door_rotating|OnPhysCannonPullAnimFinished",
        "output|prop_door_rotating|OnTakeDamage",
        "output|prop_door_rotating|OnUnblockedClosing",
        "output|prop_door_rotating|OnUnblockedOpening",
        "output|prop_dynamic_ornament|OnAnimationBegun",
        "output|prop_dynamic_ornament|OnAnimationDone",
        "output|prop_dynamic_ornament|OnBreak",
        "output|prop_dynamic_ornament|OnHealthChanged",
        "output|prop_dynamic_ornament|OnIgnite",
        "output|prop_dynamic_ornament|OnPhysCannonAnimatePostStarted",
        "output|prop_dynamic_ornament|OnPhysCannonAnimatePreStarted",
        "output|prop_dynamic_ornament|OnPhysCannonAnimatePullStarted",
        "output|prop_dynamic_ornament|OnPhysCannonDetach",
        "output|prop_dynamic_ornament|OnPhysCannonPullAnimFinished",
        "output|prop_dynamic_ornament|OnTakeDamage",
        "output|prop_dynamic_override|OnBreak",
        "output|prop_dynamic_override|OnHealthChanged",
        "output|prop_dynamic_override|OnIgnite",
        "output|prop_dynamic_override|OnPhysCannonAnimatePostStarted",
        "output|prop_dynamic_override|OnPhysCannonAnimatePreStarted",
        "output|prop_dynamic_override|OnPhysCannonAnimatePullStarted",
        "output|prop_dynamic_override|OnPhysCannonDetach",
        "output|prop_dynamic_override|OnPhysCannonPullAnimFinished",
        "output|prop_dynamic_override|OnTakeDamage",
        "output|prop_dynamic|OnBreak",
        "output|prop_dynamic|OnHealthChanged",
        "output|prop_dynamic|OnIgnite",
        "output|prop_dynamic|OnPhysCannonAnimatePostStarted",
        "output|prop_dynamic|OnPhysCannonAnimatePreStarted",
        "output|prop_dynamic|OnPhysCannonAnimatePullStarted",
        "output|prop_dynamic|OnPhysCannonDetach",
        "output|prop_dynamic|OnPhysCannonPullAnimFinished",
        "output|prop_dynamic|OnTakeDamage",
        "output|prop_physics_multiplayer|OnIgnite",
        "output|prop_physics_multiplayer|OnPhysCannonAnimatePostStarted",
        "output|prop_physics_multiplayer|OnPhysCannonAnimatePreStarted",
        "output|prop_physics_multiplayer|OnPhysCannonAnimatePullStarted",
        "output|prop_physics_multiplayer|OnPhysCannonDetach",
        "output|prop_physics_multiplayer|OnPhysCannonPullAnimFinished",
        "output|prop_physics_multiplayer|OnTakeDamage",
        "output|prop_physics_override|OnIgnite",
        "output|prop_physics_override|OnPhysCannonAnimatePostStarted",
        "output|prop_physics_override|OnPhysCannonAnimatePreStarted",
        "output|prop_physics_override|OnPhysCannonAnimatePullStarted",
        "output|prop_physics_override|OnPhysCannonDetach",
        "output|prop_physics_override|OnPhysCannonPullAnimFinished",
        "output|prop_physics_override|OnTakeDamage",
        "output|prop_physics_respawnable|OnIgnite",
        "output|prop_physics_respawnable|OnPhysCannonAnimatePostStarted",
        "output|prop_physics_respawnable|OnPhysCannonAnimatePreStarted",
        "output|prop_physics_respawnable|OnPhysCannonAnimatePullStarted",
        "output|prop_physics_respawnable|OnPhysCannonDetach",
        "output|prop_physics_respawnable|OnPhysCannonPullAnimFinished",
        "output|prop_physics_respawnable|OnTakeDamage",
        "output|prop_physics|OnIgnite",
        "output|prop_physics|OnPhysCannonAnimatePostStarted",
        "output|prop_physics|OnPhysCannonAnimatePreStarted",
        "output|prop_physics|OnPhysCannonAnimatePullStarted",
        "output|prop_physics|OnPhysCannonDetach",
        "output|prop_physics|OnPhysCannonPullAnimFinished",
        "output|prop_physics|OnTakeDamage",
        "output|prop_sphere|OnAwakened",
        "output|prop_sphere|OnBreak",
        "output|prop_sphere|OnHealthChanged",
        "output|prop_sphere|OnIgnite",
        "output|prop_sphere|OnMotionEnabled",
        "output|prop_sphere|OnOutOfWorld",
        "output|prop_sphere|OnPhysCannonAnimatePostStarted",
        "output|prop_sphere|OnPhysCannonAnimatePreStarted",
        "output|prop_sphere|OnPhysCannonAnimatePullStarted",
        "output|prop_sphere|OnPhysCannonDetach",
        "output|prop_sphere|OnPhysCannonPullAnimFinished",
        "output|prop_sphere|OnPlayerPickup",
        "output|prop_sphere|OnPlayerUse",
        "output|prop_sphere|OnTakeDamage",
        "output|rpg_missile|OnIgnite",
        "output|simple_physics_prop|OnIgnite",
        "output|trigger_apply_impulse|OnEndTouch",
        "output|trigger_apply_impulse|OnNotTouching",
        "output|trigger_apply_impulse|OnStartTouchAll",
        "output|trigger_apply_impulse|OnTouching",
        "output|trigger_brush|OnEndTouch",
        "output|trigger_cdaudio|OnEndTouch",
        "output|trigger_cdaudio|OnNotTouching",
        "output|trigger_cdaudio|OnStartTouchAll",
        "output|trigger_cdaudio|OnTouching",
        "output|trigger_gravity|OnEndTouch",
        "output|trigger_gravity|OnNotTouching",
        "output|trigger_gravity|OnStartTouchAll",
        "output|trigger_gravity|OnTouching",
        "output|trigger_hurt|OnEndTouch",
        "output|trigger_hurt|OnNotTouching",
        "output|trigger_hurt|OnStartTouchAll",
        "output|trigger_hurt|OnTouching",
        "output|trigger_impact|OnEndTouch",
        "output|trigger_impact|OnNotTouching",
        "output|trigger_impact|OnStartTouchAll",
        "output|trigger_impact|OnTouching",
        "output|trigger_look|OnEndTouch",
        "output|trigger_look|OnNotTouching",
        "output|trigger_look|OnStartTouchAll",
        "output|trigger_look|OnTouching",
        "output|trigger_multiple|OnEndTouch",
        "output|trigger_multiple|OnNotTouching",
        "output|trigger_multiple|OnStartTouchAll",
        "output|trigger_multiple|OnTouching",
        "output|trigger_once|OnEndTouch",
        "output|trigger_once|OnNotTouching",
        "output|trigger_once|OnStartTouchAll",
        "output|trigger_once|OnTouching",
        "output|trigger_playermovement|OnEndTouch",
        "output|trigger_playermovement|OnNotTouching",
        "output|trigger_playermovement|OnStartTouchAll",
        "output|trigger_playermovement|OnTouching",
        "output|trigger_proximity|OnEndTouch",
        "output|trigger_proximity|OnNotTouching",
        "output|trigger_proximity|OnStartTouchAll",
        "output|trigger_proximity|OnTouching",
        "output|trigger_push|OnEndTouch",
        "output|trigger_push|OnNotTouching",
        "output|trigger_push|OnStartTouchAll",
        "output|trigger_push|OnTouching",
        "output|trigger_remove|OnEndTouch",
        "output|trigger_remove|OnNotTouching",
        "output|trigger_remove|OnStartTouchAll",
        "output|trigger_remove|OnTouching",
        "output|trigger_serverragdoll|OnEndTouch",
        "output|trigger_serverragdoll|OnNotTouching",
        "output|trigger_serverragdoll|OnStartTouchAll",
        "output|trigger_serverragdoll|OnTouching",
        "output|trigger_soundscape|OnEndTouch",
        "output|trigger_soundscape|OnNotTouching",
        "output|trigger_soundscape|OnStartTouchAll",
        "output|trigger_soundscape|OnTouching",
        "output|trigger_teleport_relative|OnEndTouch",
        "output|trigger_teleport_relative|OnNotTouching",
        "output|trigger_teleport_relative|OnStartTouchAll",
        "output|trigger_teleport_relative|OnTouching",
        "output|trigger_teleport|OnEndTouch",
        "output|trigger_teleport|OnNotTouching",
        "output|trigger_teleport|OnStartTouchAll",
        "output|trigger_teleport|OnTouching",
        "output|trigger|OnEndTouch",
        "output|trigger|OnEndTouchAll",
        "output|trigger|OnNotTouching",
        "output|trigger|OnStartTouch",
        "output|trigger|OnStartTouchAll",
        "output|trigger|OnTouching",
        "output|weapon_357|OnNPCPickup",
        "output|weapon_357|OnPlayerPickup",
        "output|weapon_357|OnPlayerUse",
        "output|weapon_ar2|OnNPCPickup",
        "output|weapon_ar2|OnPlayerPickup",
        "output|weapon_ar2|OnPlayerUse",
        "output|weapon_crossbow|OnNPCPickup",
        "output|weapon_crossbow|OnPlayerPickup",
        "output|weapon_crossbow|OnPlayerUse",
        "output|weapon_crowbar|OnNPCPickup",
        "output|weapon_crowbar|OnPlayerPickup",
        "output|weapon_crowbar|OnPlayerUse",
        "output|weapon_frag|OnNPCPickup",
        "output|weapon_frag|OnPlayerPickup",
        "output|weapon_frag|OnPlayerUse",
        "output|weapon_pistol|OnNPCPickup",
        "output|weapon_pistol|OnPlayerPickup",
        "output|weapon_pistol|OnPlayerUse",
        "output|weapon_rpg|OnNPCPickup",
        "output|weapon_rpg|OnPlayerPickup",
        "output|weapon_rpg|OnPlayerUse",
        "output|weapon_shotgun|OnNPCPickup",
        "output|weapon_shotgun|OnPlayerPickup",
        "output|weapon_shotgun|OnPlayerUse",
        "output|weapon_smg1|OnNPCPickup",
        "output|weapon_smg1|OnPlayerPickup",
        "output|weapon_smg1|OnPlayerUse",
        "input|grenade_ar2|BecomeRagdoll",
        "input|grenade_ar2|Ignite",
        "input|grenade_ar2|IgniteHitboxFireScale",
        "input|grenade_ar2|IgniteLifetime",
        "input|grenade_ar2|IgniteNumHitboxFires",
        "input|grenade_ar2|SetBodyGroup",
        "input|grenade_ar2|SetCycle",
        "input|grenade_ar2|SetLightingOrigin",
        "input|grenade_ar2|SetLightingOriginHack",
        "input|grenade_ar2|SetModel",
        "input|grenade_ar2|SetModelScale",
        "input|grenade_ar2|SetPlaybackRate",
        "input|grenade_ar2|fademaxdist",
        "input|grenade_ar2|fademindist",
        "input|grenade_ar2|skin",
        "input|hl2mp_ragdoll|BecomeRagdoll",
        "input|hl2mp_ragdoll|Ignite",
        "input|hl2mp_ragdoll|IgniteHitboxFireScale",
        "input|hl2mp_ragdoll|IgniteLifetime",
        "input|hl2mp_ragdoll|IgniteNumHitboxFires",
        "input|hl2mp_ragdoll|SetBodyGroup",
        "input|hl2mp_ragdoll|SetCycle",
        "input|hl2mp_ragdoll|SetLightingOrigin",
        "input|hl2mp_ragdoll|SetLightingOriginHack",
        "input|hl2mp_ragdoll|SetModel",
        "input|hl2mp_ragdoll|SetModelScale",
        "input|hl2mp_ragdoll|SetPlaybackRate",
        "input|hl2mp_ragdoll|fademaxdist",
        "input|hl2mp_ragdoll|fademindist",
        "input|hl2mp_ragdoll|skin",
        "input|npc_satchel|BecomeRagdoll",
        "input|npc_satchel|Ignite",
        "input|npc_satchel|IgniteHitboxFireScale",
        "input|npc_satchel|IgniteLifetime",
        "input|npc_satchel|IgniteNumHitboxFires",
        "input|npc_satchel|SetBodyGroup",
        "input|npc_satchel|SetCycle",
        "input|npc_satchel|SetLightingOrigin",
        "input|npc_satchel|SetLightingOriginHack",
        "input|npc_satchel|SetModel",
        "input|npc_satchel|SetModelScale",
        "input|npc_satchel|SetPlaybackRate",
        "input|npc_satchel|fademaxdist",
        "input|npc_satchel|fademindist",
        "input|npc_satchel|skin",
        "input|npc_tripmine|BecomeRagdoll",
        "input|npc_tripmine|Ignite",
        "input|npc_tripmine|IgniteHitboxFireScale",
        "input|npc_tripmine|IgniteLifetime",
        "input|npc_tripmine|IgniteNumHitboxFires",
        "input|npc_tripmine|SetBodyGroup",
        "input|npc_tripmine|SetCycle",
        "input|npc_tripmine|SetLightingOrigin",
        "input|npc_tripmine|SetLightingOriginHack",
        "input|npc_tripmine|SetModel",
        "input|npc_tripmine|SetModelScale",
        "input|npc_tripmine|SetPlaybackRate",
        "input|npc_tripmine|fademaxdist",
        "input|npc_tripmine|fademindist",
        "input|npc_tripmine|skin",
        "input|weapon_physcannon|HideWeapon",
        "input|weapon_slam|HideWeapon",
        "input|weapon_stunstick|HideWeapon",
        "key|beam|HDRColorScale",
        "key|beam|ScrollSpeed",
        "key|beam|damage",
        "key|beam|dissolvetype",
        "key|grenade_ar2|LightingOrigin",
        "key|grenade_ar2|LightingOriginHack",
        "key|grenade_ar2|SetBodyGroup",
        "key|grenade_ar2|body",
        "key|grenade_ar2|cycle",
        "key|grenade_ar2|fademaxdist",
        "key|grenade_ar2|fademindist",
        "key|grenade_ar2|fadescale",
        "key|grenade_ar2|hitboxset",
        "key|grenade_ar2|modelscale",
        "key|grenade_ar2|playbackrate",
        "key|grenade_ar2|sequence",
        "key|grenade_ar2|skin",
        "key|hl2mp_ragdoll|LightingOrigin",
        "key|hl2mp_ragdoll|LightingOriginHack",
        "key|hl2mp_ragdoll|SetBodyGroup",
        "key|hl2mp_ragdoll|body",
        "key|hl2mp_ragdoll|cycle",
        "key|hl2mp_ragdoll|fademaxdist",
        "key|hl2mp_ragdoll|fademindist",
        "key|hl2mp_ragdoll|fadescale",
        "key|hl2mp_ragdoll|hitboxset",
        "key|hl2mp_ragdoll|modelscale",
        "key|hl2mp_ragdoll|playbackrate",
        "key|hl2mp_ragdoll|sequence",
        "key|hl2mp_ragdoll|skin",
        "key|npc_satchel|LightingOrigin",
        "key|npc_satchel|LightingOriginHack",
        "key|npc_satchel|SetBodyGroup",
        "key|npc_satchel|body",
        "key|npc_satchel|cycle",
        "key|npc_satchel|fademaxdist",
        "key|npc_satchel|fademindist",
        "key|npc_satchel|fadescale",
        "key|npc_satchel|hitboxset",
        "key|npc_satchel|modelscale",
        "key|npc_satchel|playbackrate",
        "key|npc_satchel|sequence",
        "key|npc_satchel|skin",
        "key|npc_tripmine|LightingOrigin",
        "key|npc_tripmine|LightingOriginHack",
        "key|npc_tripmine|SetBodyGroup",
        "key|npc_tripmine|body",
        "key|npc_tripmine|cycle",
        "key|npc_tripmine|fademaxdist",
        "key|npc_tripmine|fademindist",
        "key|npc_tripmine|fadescale",
        "key|npc_tripmine|hitboxset",
        "key|npc_tripmine|modelscale",
        "key|npc_tripmine|playbackrate",
        "key|npc_tripmine|sequence",
        "key|npc_tripmine|skin",
        "output|grenade_ar2|OnIgnite",
        "output|hl2mp_ragdoll|OnIgnite",
        "output|npc_satchel|OnIgnite",
        "output|npc_tripmine|OnIgnite",
        "output|weapon_physcannon|OnNPCPickup",
        "output|weapon_physcannon|OnPlayerPickup",
        "output|weapon_physcannon|OnPlayerUse",
        "output|weapon_slam|OnNPCPickup",
        "output|weapon_slam|OnPlayerPickup",
        "output|weapon_slam|OnPlayerUse",
        "output|weapon_stunstick|OnNPCPickup",
        "output|weapon_stunstick|OnPlayerPickup",
        "output|weapon_stunstick|OnPlayerUse",
    };
}
