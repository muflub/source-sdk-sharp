//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// The development console, one class per spelling.
//
// See DevCommands.cs for why these are ordinary [ConCommand] classes rather
// than a verb table, and for the sv_cheats rule they all share.
//
// The help text on each attribute is the text stock's own registration carried,
// so `help ent_create` at the console says what it said in HL2 — which is a
// thing the verb table could not do at all, because a table entry has no help.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Creation.
//-----------------------------------------------------------------------------

/// <summary>The create/keyvalue/place/spawn sequence all three creators share.</summary>
public abstract class SpawningCommand : DevCommand
{
    protected SpawningCommand(DevContext context) : base(context)
    {
    }

    /// <summary>
    /// Whether an aim that hits nothing still creates the entity, at its
    /// keyvalue origin: stock's <c>ent_create</c> did (baseentity.cpp:8564);
    /// its <c>prop_*_create</c> returned first (props.cpp:5835).
    /// </summary>
    protected virtual bool CreatesWithoutAHit => false;

    protected void Spawn(EntityHandle issuer, string className,
                         List<(string Key, string Value)> keyValues)
    {
        // WHAT ANSWERS TO THIS NAME, asked of the factory dictionary rather
        // than guessed at.
        //
        // This block used to read "IsRegistered only knows the managed registry,
        // and the native factory dictionary is not reachable from here — so this
        // is a warning about a name that looks wrong, not a refusal". It is
        // reachable now: EntityFactories pulls the dictionary's own list (lane
        // sub-factory), and OwnerOf joins it with the managed registry.
        //
        // STILL ATTEMPTED IN EVERY CASE, and deliberately. The pull needs ABI
        // slots that are requested and not yet appended
        // (.claude/entity-port/abi-requests/sub-factory.md), so on this build
        // OwnerOf cannot answer Nothing at all and the text below is the same
        // hedge it always was. Refusing here on an unasked table would block
        // every native class; the create is what establishes the truth, and the
        // line after it reports what actually happened.
        var owner = Context.Factories.OwnerOf(className, Context.Directory.IsRegistered);

        // THE LIVE-ENTITY WALK IS SKIPPED FOR A CLASS WE ALREADY KNOW ANSWERS,
        // which is the short-circuit the old `&&` did silently. Evaluating both
        // sides eagerly spends an engine call on every ent_create of a class we
        // own, and nine DevCommandsTests facts said so the moment it did: their
        // strict engine has no stub for a call the command never used to make.
        if (Context.Factories.NeedsLiveCheck(owner)
            && !Context.Entities.FindAllByClassname(className).Any())
        {
            string? advice = Context.Factories.CreateAdvice(Verb, className, owner);

            if (advice is not null)
                Log.Msg(advice);
        }

        var where = AimedSurface(issuer);

        if (where is null && !CreatesWithoutAHit)
        {
            Log.Msg($"{Verb}: the aim hit nothing, so nothing was created.\n");
            return;
        }

        var entity = Context.Entities.Create(className);

        if (entity == EntityHandle.None)
        {
            Log.Msg($"{Verb}: nothing was created for '{className}' — no factory answers to that name.\n");
            return;
        }

        foreach (var (key, value) in keyValues)
        {
            if (!Context.Entities.SetKeyValue(entity, key, value))
                Log.Msg($"{Verb}: '{className}' has no key '{key}' (value '{value}' was dropped).\n");
        }

        if (where is Vector surface)
            Context.Entities.SetOrigin(entity, surface);

        Context.Entities.SetAngles(entity, FacingAwayFrom(issuer));
        Context.Entities.Spawn(entity);

        if (where is null)
        {
            Log.Msg($"{Verb}: created {className} #{Context.Entities.GetIndex(entity)} at its own origin; the aim hit nothing\n");
            return;
        }

        // AND THEN LIFT IT CLEAR OF THE SURFACE IT WAS AIMED AT, which is the
        // half of stock's rule this command lost and the reason a spawned
        // physics prop sometimes goes through the floor.
        //
        // Stock's CC_Ent_Create did NOT create the entity at the trace point.
        // `CC_Ent_Create_DropPoint` (baseentity.cpp:8355) returned the trace
        // endpoint RAISED BY 12, and the caller then teleported the entity
        // there and called `UTIL_DropToFloor` on it. Under SOURCESHARP that
        // helper returns false and `UTIL_DropToFloor` was deleted outright
        // (util.cpp:357, "dead in this build, measured rather than reasoned"),
        // so what is left here puts the entity's ORIGIN exactly on the surface
        // — and an origin is not a footprint.
        //
        // This is not a cosmetic difference. `models/props_junk/wood_crate001a.mdl`
        // rests with its origin about 28 units above the surface it stands on —
        // measured, on dm_lockdown, as the gap between the aim point (z -1.33)
        // and where the crate settles once this lift is in (z 26.3 to 26.8, over
        // sixteen draws) — so creating it AT the surface buries 28 units of a
        // solid crate inside a solid world and hands vphysics a penetration to
        // resolve. Over 36 pre-fix draws it is thrown 29 to 164 units sideways,
        // and in six of them it comes out the WRONG SIDE and rests at z = -5935:
        // "one newly spawned prop falls, and then every one after it in that
        // game", which is lane 3f-shadow's open bug. Over 40 post-fix draws the
        // same crate moves 0.1 to 13.3 units. The two ranges do not overlap.
        //
        // The engine ABI already says this in as many words: EntityGetBounds
        // exists precisely because "a size can only be centred on the origin,
        // and most prop models put their origin on the BASE. Centring one puts
        // half the hull through the floor" (sourcesharp.h:2776).
        //
        // So the drop point is stock's — CLEARANCE units above the surface —
        // measured from where the entity's box would REST on that surface
        // rather than from the origin. For an entity whose origin is already on
        // its base (stock's NPCs and items, mins.Z == 0) that is stock's twelve
        // units exactly, and it falls twelve units the way it always did. For a
        // centre-origin prop it is the same twelve units of air, which is what
        // stock's UTIL_DropToFloor was trying to arrange and could not: its
        // downward sweep starts inside the floor, comes back allsolid, returns
        // -1, and leaves the prop buried.
        var (mins, _) = Context.Entities.GetBounds(entity);
        var dropPoint = DropPointFor(where.Value, mins);

        // ZERO VELOCITY, because a physics prop that has already been spawned
        // has a physics object and may have a tick of gravity on it; carrying
        // that into the lift would make where it lands depend on how long Spawn
        // took, which is the kind of dependency this whole change removes.
        Context.Entities.Teleport(entity, dropPoint, null, zeroVelocity: true);

        Log.Msg($"{Verb}: created {className} #{Context.Entities.GetIndex(entity)} at {dropPoint}\n");
    }

    /// <summary>
    /// How far above the surface stock's <c>ent_create</c> dropped what it made:
    /// twelve units (<c>CC_Ent_Create_DropPoint</c>, baseentity.cpp:8355).
    /// </summary>
    public const float Clearance = 12f;

    /// <summary>
    /// Where a created entity's ORIGIN goes so that its BOX starts
    /// <see cref="Clearance"/> units above the surface the player is looking at.
    ///
    /// <para>
    /// <paramref name="mins"/> is the collision box as offsets from the origin
    /// (<c>EntityGetBounds</c>), so <c>-mins.Z</c> is how far the origin sits
    /// above the box's underside. A model whose origin is already on its base
    /// has <c>mins.Z == 0</c> and gets stock's twelve units and nothing else.
    /// </para>
    ///
    /// <para>
    /// A POSITIVE <c>mins.Z</c> — a box that floats entirely above its own
    /// origin — is clamped away rather than subtracted, because lowering an
    /// entity INTO the surface is the failure this whole function exists to
    /// stop, and no stock path ever asked for it.
    /// </para>
    /// </summary>
    public static Vector DropPointFor(Vector surface, Vector mins)
        => new Vector(surface.X, surface.Y,
                      surface.Z + Clearance - Math.Min(mins.Z, 0f));

    /// <summary>
    /// A created entity faces back at the player, which is stock's rule and is
    /// what makes a spawned prop's front visible rather than its back.
    /// </summary>
    private Vector FacingAwayFrom(EntityHandle issuer)
    {
        var eye = Context.Entities.GetEyeAngles(issuer);
        return new Vector(0f, eye.Y + 180f, 0f);
    }
}

/// <summary>
/// <c>ent_create &lt;classname&gt; [key value]...</c> — an entity of that
/// class, where the player is looking.
///
/// <para>
/// Warns about a classname no managed factory answers to, out loud, BEFORE
/// creating anything. Stock created and then quietly leaked a half-entity, which
/// is how "ent_create typo" looked like "ent_create is broken".
/// </para>
/// </summary>
[ConCommand("ent_create",
    Help = "Creates an entity of the given type where the player is looking.\n" +
           "\tent_create <classname> [key value]... (must have sv_cheats)")]
public sealed class EntCreateCommand : SpawningCommand
{
    public EntCreateCommand(DevContext context) : base(context)
    {
    }

    protected override bool CreatesWithoutAHit => true;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg("usage: ent_create <classname> [key value]...\n");
            return;
        }

        Spawn(issuer, argv[0], KeyValuePairs(argv, 1));
    }
}

/// <summary>
/// Stock's own shorthand for <c>ent_create</c> with a model keyvalue, kept
/// because it is the spelling anyone testing physics reaches for.
/// </summary>
public abstract class PropCreateCommand : SpawningCommand
{
    protected PropCreateCommand(DevContext context) : base(context)
    {
    }

    /// <summary>The entity class this spelling makes.</summary>
    protected abstract string PropClass { get; }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg($"usage: {Verb} <model path, with or without models/ and .mdl>\n");
            return;
        }

        Spawn(issuer, PropClass, new List<(string, string)> { ("model", ModelPath(argv[0])) });
    }
}

/// <summary><c>prop_physics_create &lt;model&gt;</c>.</summary>
[ConCommand("prop_physics_create",
    Help = "Creates a physics prop of the given model where the player is looking. (must have sv_cheats)")]
public sealed class PropPhysicsCreateCommand : PropCreateCommand
{
    public PropPhysicsCreateCommand(DevContext context) : base(context)
    {
    }

    protected override string PropClass => "prop_physics";
}

/// <summary><c>prop_dynamic_create &lt;model&gt;</c>.</summary>
[ConCommand("prop_dynamic_create",
    Help = "Creates a dynamic prop of the given model where the player is looking. (must have sv_cheats)")]
public sealed class PropDynamicCreateCommand : PropCreateCommand
{
    public PropDynamicCreateCommand(DevContext context) : base(context)
    {
    }

    protected override string PropClass => "prop_dynamic";
}

//-----------------------------------------------------------------------------
// Mutation.
//-----------------------------------------------------------------------------

/// <summary><c>ent_remove [target]</c> — the picked entity, or everything the target names.</summary>
[ConCommand("ent_remove",
    Help = "Removes the given entity(s).\n\tArguments: {entity name} / {class name} / " +
           "no argument picks what the player is looking at (must have sv_cheats)")]
public sealed class EntRemoveCommand : DevCommand
{
    public EntRemoveCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            // The issuer is refused: stock would UTIL_Remove the player who
            // typed it, which is a crash nobody meant to keep. Same rule as
            // impulse 203.
            if (subject == issuer)
            {
                Log.Msg("ent_remove: refusing to remove the player who typed it.\n");
                continue;
            }

            string what = Describe(subject);
            Context.Entities.Remove(subject);
            Log.Msg($"ent_remove: removed {what}\n");
        }
    }
}

/// <summary><c>ent_setname &lt;newname&gt; [target]</c>.</summary>
[ConCommand("ent_setname",
    Help = "Sets the targetname of the given entity(s).\n\tArguments: {new name} {entity name} / " +
           "{class name} / no argument picks what the player is looking at (must have sv_cheats)")]
public sealed class EntSetNameCommand : DevCommand
{
    public EntSetNameCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg("usage: ent_setname <new name> [entity name / class name; omit for what you are looking at]\n");
            return;
        }

        foreach (var subject in Select(issuer, Rest(argv, 1)))
        {
            if (Context.Entities.SetKeyValue(subject, "targetname", argv[0]))
                Log.Msg($"ent_setname: {Describe(subject)} is now named '{argv[0]}'\n");
            else
                Log.Msg($"ent_setname: {Describe(subject)} refused a targetname.\n");
        }
    }
}

/// <summary>
/// <c>ent_keyvalue &lt;target&gt; &lt;key&gt; &lt;value&gt; ...</c>.
///
/// <para>
/// Stock addressed the target by HAMMER ID, which is an editor identity the
/// engine only carries while the tools framework is attached — never in a
/// normal game, and never at all here. The target is a name or a class, like
/// every other command in this family, so the same word works in ent_fire,
/// ent_remove and this.
/// </para>
/// </summary>
[ConCommand("ent_keyvalue",
    Help = "Applies key/value pairs to the given entity(s).\n" +
           "\tFormat: ent_keyvalue <entity name / class name> <key> <value>... (must have sv_cheats)")]
public sealed class EntKeyValueCommand : DevCommand
{
    public EntKeyValueCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        // A target and at least one whole pair, so an odd count. The verb is no
        // longer one of these words — the engine keeps it — which is why the
        // parity is the opposite way round from a line that includes it.
        if (argv.Length < 3 || argv.Length % 2 != 1)
        {
            Log.Msg("usage: ent_keyvalue <entity name / class name> <key> <value> [<key> <value>]...\n");
            return;
        }

        var pairs = KeyValuePairs(argv, 1);

        foreach (var subject in Select(issuer, argv[0]))
        {
            foreach (var (key, value) in pairs)
            {
                bool took = new EntityKeyValues(Context.Entities).Apply(subject, key, value);
                Log.Msg($"ent_keyvalue: {Describe(subject)} {(took ? "took" : "REFUSED")} {key}='{value}'\n");
            }
        }
    }
}

/// <summary>
/// <c>ent_rotate [degrees] [target]</c> — turn about the vertical axis.
///
/// <para>
/// Stock built a rotation matrix and multiplied it into the entity's
/// orientation, which for a Z rotation is exactly "add to the yaw" — the
/// matrix was there to compose with pitch and roll, and composing a pure Z
/// rotation on the left is the same addition. Kept as the addition because
/// it is the whole content of the command and it can be read.
/// </para>
/// </summary>
[ConCommand("ent_rotate",
    Help = "Rotates an entity about its vertical axis by a number of degrees. (must have sv_cheats)")]
public sealed class EntRotateCommand : DevCommand
{
    public EntRotateCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        float degrees = DevContext.DefaultRotate;
        int targetFrom = 0;

        if (argv.Length >= 1 && float.TryParse(argv[0], out float written))
        {
            degrees = written;
            targetFrom = 1;
        }

        foreach (var subject in Select(issuer, Rest(argv, targetFrom)))
        {
            var angles = Context.Entities.GetAngles(subject);
            var turned = new Vector(angles.X, angles.Y + degrees, angles.Z);
            Context.Entities.SetAngles(subject, turned);
            Log.Msg($"ent_rotate: {Describe(subject)} {angles} -> {turned}\n");
        }
    }
}

/// <summary>
/// WHO MAY <c>ent_fire</c>, decided in slots rather than in pointers — the
/// managed half of <c>game/server/ent_fire_access.h</c>, which came across with
/// the command and is the one piece of it that is a RULE rather than a spelling.
///
/// <para>
/// <b>WHAT THE RULE IS FOR, in stock's own words:</b> a client who is not the
/// host must not reach <c>point_servercommand</c> through <c>ent_fire</c> —
/// <c>give point_servercommand</c>, then
/// <c>ent_fire point_servercommand command "rcon_password mynewpassword"</c>,
/// which <c>sv_cheats 1</c> alone does not stop. Stock wrote both halves as
/// <c>CBasePlayer</c> pointer comparisons; under SourceSharp the player is not
/// one, so the listen-server half compared NULL with NULL and PASSED BY
/// ACCIDENT. The header replaced both with arithmetic over slots, and this is
/// the same arithmetic, engine-free for the same reason: it can be tested
/// without a running game.
/// </para>
///
/// <para>
/// <b>TWO OF THE HEADER'S FOUR INPUTS ARE NOT ON THE MANAGED SURFACE, AND
/// NEITHER CAN CHANGE AN ANSWER.</b> Stated rather than quietly dropped:
/// </para>
///
/// <list type="bullet">
/// <item><c>maxClients</c> — the header's <c>maxClients &lt;= 1</c> branch
/// answers "whoever is here is the host by definition". It is SUBSUMED by the
/// branch below it: with one slot the only occupant IS slot 1, so
/// <c>slot == 1</c> gives the same answer for every reachable input. There is
/// no maxclients on the managed engine surface (see
/// <see cref="DevContext.SinglePlayer"/> for the reading that established
/// that), and here it costs nothing.</item>
/// <item><c>autoKickDisabled</c> — the header's dedicated-server test, which it
/// documents as answerable only from a <c>CBasePlayer</c>. In THIS mod the
/// player is never a <c>CBasePlayer</c>, so <c>pPlayer &amp;&amp;
/// pPlayer-&gt;IsAutoKickDisabled()</c> at the C++ call site is already
/// ALWAYS FALSE — "a dedicated server refuses ent_fire" is what the C++ does
/// today, and refusing here is the same behaviour and not a new one. It is also
/// the header's own stated direction: an unanswerable access question is a NO.
/// </item>
/// </list>
/// </summary>
public static class EntFireAccess
{
    /// <summary>The slot number that means no client issued the command.</summary>
    public const int NoIssuer = 0;

    /// <summary>
    /// Which slot the command is attributed to — <c>EntFire_IssuerSlot</c>.
    ///
    /// <para>
    /// A client console names its own slot. The SERVER console names nobody —
    /// and on a LISTEN server that console belongs to the host, who is slot 1.
    /// <c>engine-&gt;ServerCommand</c> is the console every tool drives this
    /// game through, the development API included, so refusing it would be a
    /// second way for <c>ent_fire</c> to look dead to everything that is not a
    /// human at a keyboard. A DEDICATED server console is deliberately not given
    /// the host: there is no host there, and the dedicated-server case is
    /// exactly the escalation the rule guards against.
    /// </para>
    /// </summary>
    public static int IssuerSlot(int commandClientSlot, bool dedicatedServer)
    {
        if (commandClientSlot > 0)
            return commandClientSlot;

        return dedicatedServer ? NoIssuer : 1;
    }

    /// <summary>
    /// May that slot fire — <c>EntFire_SlotMayFire</c>, with the two inputs this
    /// build cannot answer resolved as the type's remarks explain.
    /// </summary>
    public static bool SlotMayFire(int issuerSlot, bool dedicatedServer)
    {
        if (issuerSlot <= NoIssuer)
            return false;

        // A dedicated server asks about autokick and nothing else, and nothing
        // here can answer it. See the remarks: the C++ cannot answer it in this
        // mod either, so this is its behaviour and not a new refusal.
        if (dedicatedServer)
            return false;

        // A listen server: only the host. (Single player is the same answer —
        // the only occupied slot is 1.)
        return issuerSlot == 1;
    }

    /// <summary>
    /// The slot a handle sits in, or <see cref="NoIssuer"/>.
    ///
    /// <para>
    /// The managed <c>UTIL_GetCommandClientIndex</c>. The engine area answers
    /// "who typed it" as a HANDLE, and the rule above is about a SLOT, so the
    /// occupied slots are asked which one holds it. A walk rather than a field
    /// because a slot is the engine's fact and a handle is ours, and inventing
    /// a mapping between them is how the pointer version went wrong.
    /// </para>
    /// </summary>
    public static int SlotOf(IEnginePlayers players, EntityHandle who)
    {
        if (who == EntityHandle.None)
            return NoIssuer;

        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
        {
            if (players.GetHandle(slot) == who)
                return slot;
        }

        return NoIssuer;
    }
}

/// <summary>
/// <c>ent_fire &lt;target&gt; [action] [value] [delay]</c> — the console's way
/// into the entity I/O queue, and the one word every map author and every
/// debugging tool in this tree reaches for.
///
/// <para>
/// <b>WHY IT IS HERE AND NOT IN baseentity.cpp.</b> Stock registered it there,
/// beside <c>ent_create</c>, <c>ent_remove</c> and <c>firetarget</c> — all of
/// which are already in this file under the rule DevCommands.cs states. This
/// one was left behind, and the omission was INVISIBLE until the entity alias
/// was armed: unarmed, baseentity.cpp is still compiled and its registration is
/// the only one, so the command works and nobody looks. Armed, that whole TU
/// leaves the vpc and the command leaves with it — and nothing replaced it.
/// Typing it then does nothing at all, and does not even say
/// <c>Unknown command</c> to a tool that pushes it through
/// <c>engine-&gt;ServerCommand</c>, which is every tool here.
/// </para>
///
/// <para>
/// Measured on 2026-09-15, one game each, the same three cells:
/// armed, <c>ent_fire &lt;a&gt; Kill</c> left the entity standing and
/// <c>ent_fire &lt;a&gt; AddOutput</c> left the connection count where it was;
/// unarmed, both worked. It is what the live gate's hot-reload step had been
/// failing on in both passes, in every run, since the flip branch existed.
/// </para>
///
/// <para>
/// <b>IT POSTS AND RESOLVES NOTHING.</b> The target may be a name, a classname,
/// a wildcard or <c>!activator</c> / <c>!self</c> / <c>!player</c>; it may match
/// many entities; it may be due seconds from now; and when the managed
/// directory holds none of them the SDK hierarchy is asked. All of that is
/// <see cref="EntityDirectory"/>'s delivery already — the same path a map's own
/// connection takes — so this posts one event through
/// <see cref="DevContext.PostInput"/> and lets the queue do its job. Stock's
/// <c>ent_fire</c> did exactly this too: it never delivered anything itself, it
/// called <c>g_EventQueue.AddEvent</c>.
/// </para>
///
/// <para>
/// <b>NO "nothing is called that" PRE-CHECK, and that is an improvement rather
/// than a loss.</b> The C++ version asked four searches ahead of the queue so it
/// could say when nothing answered. The managed queue says it at DELIVERY —
/// <c>EntityDirectory.Deliver</c>'s unhandled branch, a Warning, once per
/// (target, input) pair — which is strictly better for the case the pre-check
/// was worst at: a delayed fire at an entity that does not exist YET, which
/// stock had to word as "nothing is called that <i>yet</i>" and guess about.
/// Here the question is asked at the only moment it has an answer.
/// </para>
///
/// <para>
/// Stock's default action is <c>Use</c>, its default value is empty and its
/// default delay is 0, and <c>NeedsIssuer</c> is FALSE: <c>ent_fire</c> is
/// legal with nobody connected (stock passed a NULL <c>pPlayer</c> straight
/// into the queue as the activator), and refusing it for want of a player would
/// make a server console unable to drive its own map.
/// </para>
/// </summary>
[ConCommand("ent_fire",
    Help = "Usage:\n   ent_fire <target> [action] [value] [delay]\n" +
           "\tThe target is an entity name, a class name, a wildcard or " +
           "!activator / !self / !player. (must have sv_cheats)")]
public sealed class EntFireCommand : DevCommand
{
    /// <summary>Stock's default action when only a target is given.</summary>
    public const string DefaultAction = "Use";

    private readonly IEngineHost _host;

    public EntFireCommand(DevContext context, IEngineHost host) : base(context)
    {
        _host = host;
    }

    /// <summary>
    /// False, and see the type's remarks: stock queued the event with a NULL
    /// player rather than refusing, and the server console — which is how every
    /// tool in this tree types — never has a command client.
    /// </summary>
    protected override bool NeedsIssuer => false;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1 || string.IsNullOrWhiteSpace(argv[0]))
        {
            Log.Msg("usage: ent_fire <target> [action] [value] [delay]\n");
            return;
        }

        // WHO MAY FIRE, before anything is posted and before the usage line --
        // stock's order exactly: it refused on the issuer first, printed usage
        // second and applied the point_servercommand rule third. The two are
        // merged here only because this command has no ClientPrint to route the
        // usage line to a particular player.
        bool dedicated = _host.IsDedicatedServer;
        int slot = EntFireAccess.IssuerSlot(
            EntFireAccess.SlotOf(Context.Players, Context.Issuer()), dedicated);

        if (!EntFireAccess.SlotMayFire(slot, dedicated))
        {
            // REFUSED OUT LOUD, whichever rule refused it. A refusal nobody can
            // see is the bug, not the fix.
            Log.Msg(slot <= EntFireAccess.NoIssuer
                ? "ent_fire: no console client issued this, so nothing was queued.\n"
                : dedicated
                    ? "ent_fire: refused \u2014 a dedicated server only accepts ent_fire " +
                      "from a client with autokick disabled, which this build cannot ask.\n"
                    : "ent_fire: refused \u2014 only the listen server host may fire inputs.\n");
            return;
        }

        if (Context.PostInput is null)
        {
            // OUT LOUD. A command that silently did nothing is precisely the
            // failure this class exists to end, and repeating it one layer up
            // would be the same hole with a C# accent.
            Log.Msg("ent_fire: nothing owns an entity I/O queue in this build, " +
                    "so the input was not posted.\n");
            return;
        }

        string target = argv[0];
        string action = argv.Length >= 2 && argv[1].Length > 0 ? argv[1] : DefaultAction;
        string value = argv.Length >= 3 ? argv[2] : string.Empty;
        float delay = argv.Length >= 4 ? ParseDelay(argv[3]) : 0f;

        Context.PostInput(target, action, value, delay, issuer);

        Log.Msg($"ent_fire: posted {action}{(value.Length > 0 ? $" '{value}'" : string.Empty)} " +
                $"-> {target}{(delay > 0f ? $" in {delay}s" : string.Empty)}\n");
    }

    /// <summary>
    /// The delay, in seconds. Stock read it with <c>atoi</c>, which is whole
    /// seconds and answers 0 for anything it cannot read; this reads a float,
    /// because the queue keeps float times and a map's own connections have
    /// carried fractional delays all along — <c>0.5</c> meaning "immediately"
    /// would be a silent wrong answer, which is the class of bug this whole
    /// command is a fix for.
    ///
    /// <para>Negative is 0: an event due in the past is due now.</para>
    /// </summary>
    public static float ParseDelay(string? text)
    {
        float parsed = VariantParse.Atof(text);

        return parsed > 0f ? parsed : 0f;
    }
}

/// <summary>
/// <c>firetarget [target]</c> — stock's Use-by-name, which is what a
/// mapper's "does this button do anything" test is.
/// </summary>
[ConCommand("firetarget",
    Help = "Sends Use to the given entity(s).\n\tArguments: {entity name} / {class name} / " +
           "no argument picks what the player is looking at (must have sv_cheats)")]
public sealed class FireTargetCommand : DevCommand
{
    public FireTargetCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            bool took = Context.Entities.FireInput(subject, "Use", null, issuer);
            Log.Msg($"firetarget: {Describe(subject)} {(took ? "accepted" : "REFUSED")} Use\n");
        }
    }
}

//-----------------------------------------------------------------------------
// Inspection. Stock drew these in the world with NDebugOverlay, which has no
// managed crossing; the same facts go to the console instead. A DIVERGENCE and
// named as one in every line these print, rather than a bit toggled on an
// entity nothing will ever draw.
//-----------------------------------------------------------------------------

/// <summary><c>ent_bbox</c> (local) and <c>ent_absbox</c> / <c>ent_rbox</c> (world).</summary>
public abstract class BoundsCommand : DevCommand
{
    protected BoundsCommand(DevContext context) : base(context)
    {
    }

    /// <summary>Whether the box is reported in world space.</summary>
    protected abstract bool Absolute { get; }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            var (mins, maxs) = Context.Entities.GetBounds(subject);

            if (Absolute)
            {
                var origin = Context.Entities.GetOrigin(subject);
                mins += origin;
                maxs += origin;
            }

            Log.Msg($"{Verb}: {Describe(subject)} {(Absolute ? "world" : "local")} box {mins} .. {maxs} " +
                    $"(size {Context.Entities.GetBoundsSize(subject)}) — printed, not drawn: " +
                    $"NDebugOverlay has no managed crossing\n");
        }
    }
}

/// <summary><c>ent_bbox</c> — the movement bounding box, in the entity's own space.</summary>
[ConCommand("ent_bbox", Help = "Prints the movement bounding box of the given entity(s). (must have sv_cheats)")]
public sealed class EntBboxCommand : BoundsCommand
{
    public EntBboxCommand(DevContext context) : base(context)
    {
    }

    protected override bool Absolute => false;
}

/// <summary><c>ent_absbox</c> — the same box in world space.</summary>
[ConCommand("ent_absbox", Help = "Prints the world-space bounding box of the given entity(s). (must have sv_cheats)")]
public sealed class EntAbsboxCommand : BoundsCommand
{
    public EntAbsboxCommand(DevContext context) : base(context)
    {
    }

    protected override bool Absolute => true;
}

/// <summary><c>ent_rbox</c> — stock's render-bounds spelling of the same report.</summary>
[ConCommand("ent_rbox", Help = "Prints the world-space bounding box of the given entity(s). (must have sv_cheats)")]
public sealed class EntRboxCommand : BoundsCommand
{
    public EntRboxCommand(DevContext context) : base(context)
    {
    }

    protected override bool Absolute => true;
}

/// <summary><c>ent_pivot</c> — where the entity's origin is and which way it faces.</summary>
[ConCommand("ent_pivot", Help = "Prints the pivot and axes of the given entity(s). (must have sv_cheats)")]
public sealed class EntPivotCommand : DevCommand
{
    public EntPivotCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            var origin = Context.Entities.GetOrigin(subject);
            var basis = Angles.Vectors(Context.Entities.GetAngles(subject));

            Log.Msg($"ent_pivot: {Describe(subject)} at {origin}\n" +
                    $"    forward {basis.Forward}  right {basis.Right}  up {basis.Up}\n");
        }
    }
}

/// <summary><c>ent_viewoffset</c> — the eye position, and its offset from the origin.</summary>
[ConCommand("ent_viewoffset", Help = "Prints the eye position of the given entity(s). (must have sv_cheats)")]
public sealed class EntViewOffsetCommand : DevCommand
{
    public EntViewOffsetCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            var origin = Context.Entities.GetOrigin(subject);
            var eye = Context.Entities.GetEyePosition(subject);

            Log.Msg($"ent_viewoffset: {Describe(subject)} eye {eye}, offset {eye - origin}\n");
        }
    }
}

//-----------------------------------------------------------------------------
// The player.
//-----------------------------------------------------------------------------

/// <summary>
/// <c>give &lt;weapon or ammo&gt;</c>.
///
/// <para>
/// Stock's <c>GiveNamedItem</c> spawned an entity of the classname and had
/// the player touch it. What this mod HAS to give is what the two tables
/// hold — <see cref="WeaponTypes.All"/> and <see cref="AmmoTypes.All"/> —
/// so give addresses those directly rather than spawning a pickup and
/// hoping the touch lands. Anything else NAMES WHAT IT COULD HAVE GIVEN,
/// which is the answer a person typing a half-remembered classname wants;
/// use ent_create for an arbitrary entity.
/// </para>
///
/// <para>
/// The gate is stock's own, and it is not FCVAR_CHEAT: single player OR
/// sv_cheats. That difference is deliberate in stock and is kept — which is
/// what <see cref="RequiresCheats"/> being false is for. It does not mean
/// ungated.
/// </para>
/// </summary>
[ConCommand("give",
    Help = "Give a weapon or ammunition to the player.\n\tArguments: <weapon or ammo name>")]
public sealed class GiveCommand : DevCommand
{
    public GiveCommand(DevContext context) : base(context)
    {
    }

    protected override bool RequiresCheats => false;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg($"usage: give <{string.Join(" | ", WeaponTypes.All.Select(w => w.Name))}>\n" +
                    $"       give <{string.Join(" | ", AmmoTypes.All.Select(a => a.Name))}> [count]\n");
            return;
        }

        if (!Context.SinglePlayer() && !Context.CheatsEnabled())
        {
            // Named in full rather than as stock's one-liner, because the reason
            // is not the obvious one: stock ALSO allowed this without cheats in
            // single player, and this build cannot tell whether it is one. See
            // DevContext.SinglePlayer — a gate that cannot read half its own
            // switch says which half.
            Log.Msg("give: refused. Stock also allowed it without cheats in single player, and " +
                    "this build cannot tell whether it is one — maxClients is a gpGlobals field " +
                    "with no ConVar and no managed crossing, so that half of the gate is a no. " +
                    "Set sv_cheats 1.\n");
            return;
        }

        string wanted = argv[0];

        if (WeaponTypes.For(wanted) is { } weapon)
        {
            if (Context.Arsenal.GiveWeapon(issuer, weapon.Name))
                Log.Msg($"give: {weapon.Name}\n");
            else
                Log.Msg($"give: {weapon.Name} was not taken — the player is dead, or already carries it.\n");

            return;
        }

        if (AmmoTypes.For(wanted) is { } ammo)
        {
            int count = argv.Length >= 2 && int.TryParse(argv[1], out int written) ? written : ammo.MaxCarry;
            int given = Context.Arsenal.GiveAmmo(issuer, count, ammo.Name);

            Log.Msg(given > 0
                ? $"give: {given} {ammo.Name}\n"
                : $"give: no {ammo.Name} was taken — the pool is full, or the player is dead.\n");

            return;
        }

        Log.Msg($"give: this mod has nothing called '{wanted}'.\n" +
                $"    weapons: {string.Join(", ", WeaponTypes.All.Select(w => w.Name))}\n" +
                $"    ammo:    {string.Join(", ", AmmoTypes.All.Select(a => a.Name))}\n" +
                $"    anything else is an entity: ent_create {wanted}\n");
    }
}

/// <summary>
/// <c>fov [degrees]</c> — read it or set it.
///
/// <para>
/// The requester is the player themself, which is what makes
/// <c>fov 0</c> release this zoom without disturbing an <c>env_zoom</c>'s:
/// SetFov is keyed by requester precisely so two of them cannot release each
/// other (divergences.md, "Two env_zooms on one player").
/// </para>
/// </summary>
[ConCommand("fov", Help = "Change the player's FOV. (must have sv_cheats)")]
public sealed class FovCommand : DevCommand
{
    public FovCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg("usage: fov <degrees>, or fov 0 to release the console's own zoom\n");
            return;
        }

        if (!int.TryParse(argv[0], out int degrees))
        {
            Log.Msg($"fov: '{argv[0]}' is not a number of degrees.\n");
            return;
        }

        Context.Players.SetFov(issuer, issuer, degrees);
        Log.Msg(degrees == 0
            ? "fov: released the console's zoom; whatever else holds one still holds it\n"
            : $"fov: {degrees}\n");
    }
}

/// <summary><c>setmodel &lt;name&gt;</c> — the player's world model.</summary>
[ConCommand("setmodel", Help = "Changes the player's model. (must have sv_cheats)")]
public sealed class SetModelCommand : DevCommand
{
    public SetModelCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1)
        {
            Log.Msg("usage: setmodel <model path, with or without models/ and .mdl>\n");
            return;
        }

        string path = ModelPath(argv[0]);
        Context.Render.SetModel(issuer, path);
        Log.Msg($"setmodel: {path}\n");
    }
}

//-----------------------------------------------------------------------------
// The world.
//-----------------------------------------------------------------------------

/// <summary>
/// <c>cast_ray</c> — what is in front of the player, and how far.
///
/// <para>
/// Both cast commands had already been retired with a bare <c>return;</c> in
/// client.cpp when the CBasePlayer left, which is the same silence with a
/// comment on it. They are a two-line trace and they answer a question the
/// picker commands cannot ("what is my aim actually hitting, and at what
/// distance"), so they come back rather than staying retired.
/// </para>
/// </summary>
[ConCommand("cast_ray", Help = "Reports what the player's aim hits, and how far away. (must have sv_cheats)")]
public sealed class CastRayCommand : DevCommand
{
    public CastRayCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        var start = Context.Entities.GetEyePosition(issuer);
        var forward = Angles.Vectors(Context.Entities.GetEyeAngles(issuer)).Forward;
        var end = start + forward * DevContext.PickRange;

        var hit = Context.World.TraceLine(start, end, TraceMask.Shot, issuer);

        if (!hit.Hit)
        {
            Log.Msg("cast_ray: hit nothing\n");
            return;
        }

        Log.Msg($"cast_ray: hit {Describe(hit.Entity)}\n" +
                $"    at       {hit.End}, {(hit.End - start).Length:F1} units away\n" +
                $"    normal   {hit.Normal}\n");
    }
}

/// <summary><c>cast_hull</c> — the same aim, swept as a box rather than a ray.</summary>
[ConCommand("cast_hull", Help = "Reports what a swept box along the player's aim hits. (must have sv_cheats)")]
public sealed class CastHullCommand : DevCommand
{
    public CastHullCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        var start = Context.Entities.GetEyePosition(issuer);
        var forward = Angles.Vectors(Context.Entities.GetEyeAngles(issuer)).Forward;
        var end = start + forward * DevContext.PickRange;

        var extent = new Vector(DevContext.CastHullExtent, DevContext.CastHullExtent,
                                DevContext.CastHullExtent);
        var swept = Context.World.TraceHull(start, end, Vector.Zero - extent, extent,
                                            TraceMask.Shot, issuer);

        Log.Msg(swept.Blocked || swept.StartSolid
            ? $"cast_hull: {(swept.StartSolid ? "started solid in" : "blocked by")} " +
              $"{Describe(swept.Entity)}\n"
            : "cast_hull: clear for the whole sweep\n");
    }
}

/// <summary><c>surfaceprop</c> — what the surface under the crosshair is made of.</summary>
[ConCommand("surfaceprop", Help = "Reports the surface properties at the cursor. (must have sv_cheats)")]
public sealed class SurfacePropCommand : DevCommand
{
    public SurfacePropCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        var hit = Context.World.TracePlayerAim(issuer, DevContext.PickRange, out var where);

        if (hit == EntityHandle.None)
        {
            Log.Msg("surfaceprop: the crosshair reaches nothing.\n");
            return;
        }

        int prop = Context.World.EntityModelSurfaceProp(hit);
        var material = Context.World.SurfacePropGameMaterial(prop);

        // The LETTER as well as the name. SurfaceMaterial's values are ASCII
        // characters and it names only the ones this mod acts on, so anything
        // else — concrete, dirt, tile, most of a map — prints as a bare number
        // that means nothing to a reader. 67 is 'C'.
        Log.Msg($"surfaceprop: {Describe(hit)} at {where}\n" +
                $"    surfaceprop index {prop}, game material {MaterialName(material)}\n");
    }
}

/// <summary>
/// <c>soundscape_flush</c> — forget what the player is hearing so the next
/// frame picks again.
///
/// <para>
/// Stock reloaded scripts/soundscapes_manifest.txt and re-initialised the
/// system. Soundscapes here are <c>env_soundscape</c> ENTITIES, placed by
/// the map and owned by <see cref="Soundscapes"/>, so there is no manifest
/// to reload — what a person typing this wants is for a soundscape edit to
/// take effect without a map reload, and forgetting the current pick is
/// what does that.
/// </para>
/// </summary>
[ConCommand("soundscape_flush",
    Help = "Forgets the player's current soundscape so the next frame picks again. (must have sv_cheats)")]
public sealed class SoundscapeFlushCommand : DevCommand
{
    public SoundscapeFlushCommand(DevContext context) : base(context)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (Context.FlushSoundscape is null)
        {
            Log.Msg("soundscape_flush: nothing owns soundscapes on this server.\n");
            return;
        }

        Context.FlushSoundscape(issuer);
        Log.Msg("soundscape_flush: forgot the current soundscape; the next frame picks again.\n");
    }
}

/// <summary>
/// <c>bloodspray &lt;entity&gt;</c> — sprays blood out of whatever is named.
///
/// <para>
/// Stock's was <c>CC_BloodSpray</c> in <c>game/server/effects.cpp</c>, and it
/// came here when that file left the build. It is here rather than in
/// DevCommandsRetired.cs because nothing about it is out of reach: it walked
/// <c>gEntList.FindEntityGeneric</c>, which is <see cref="DevCommand.Select"/>'s
/// name-then-classname order over the SDK hierarchy alone, and read
/// <c>EyePosition</c> and <c>WorldSpaceCenter</c>, both of which the managed
/// engine surface answers for BOTH hierarchies. On ss_sandbox 880 of 884
/// entities are managed, so the native command could reach four of them; this
/// one reaches all of them.
/// </para>
///
/// <para>
/// ONE DIVERGENCE, said out loud rather than left to be discovered. Stock's
/// command called <c>UTIL_BloodSpray</c> alone. The managed crossing is
/// <see cref="IEngineEffects.BloodEffect"/>, which emits the drips (or the
/// stream) and THEN the spray in one call — deliberately, because that order is
/// stock's env_blood order and splitting it would put a decision nobody makes
/// into a place two callers could come to disagree about. So this sprays what
/// stock sprayed and adds stock's drips underneath it. The alternative was a
/// second engine slot for a cheat command, which is a worse trade than four
/// extra blood drops.
/// </para>
///
/// <para>
/// THE HELP TEXT IS NOT STOCK'S, and that is the one place this file's rule of
/// carrying stock's own registration text is broken deliberately. Stock's was
/// the single word "blood", which says neither what it acts on nor that it is
/// gated — and <c>EveryDevelopmentCommandIsLowerCaseAndCarriesHelp</c> refuses
/// help that short, for the reason it was written: a console that prints five
/// letters has told a reader nothing.
/// </para>
/// </summary>
[ConCommand("bloodspray",
    Help = "Sprays blood out of the given entity(s). (must have sv_cheats)")]
public sealed class BloodSprayCommand : DevCommand
{
    public BloodSprayCommand(DevContext context, IEngineEffects effects) : base(context)
    {
        _effects = effects;
    }

    private readonly IEngineEffects _effects;

    /// <summary>
    /// Stock passed <c>FX_BLOODSPRAY_ALL</c>, which is <c>0xFF</c>
    /// (shareddefs.h). Only three of those bits mean anything: the client's
    /// <c>FX_BloodSpray</c> in <c>game/client/fx_blood.cpp</c> tests
    /// <c>DROPS</c>, <c>GORE</c> and <c>CLOUD</c> and nothing else, so all three
    /// named together IS 0xFF as far as anything that reads it is concerned.
    /// Named rather than spelled <c>0xFF</c> because <see cref="BloodSpray"/>
    /// has no <c>All</c>, and inventing one would imply a fourth effect exists.
    /// </summary>
    internal const BloodSpray Everything = BloodSpray.Drops | BloodSpray.Gore | BloodSpray.Cloud;

    /// <summary>Stock's amount, which is a particle count rather than damage.</summary>
    internal const float Amount = 4f;

    /// <summary>
    /// Where stock put the spray: four units out in front of the point half way
    /// between the eyes and the middle of the body. A separate function because
    /// it is the only arithmetic in the command, and the only part of it a test
    /// can pin without an engine.
    /// </summary>
    internal static Vector SprayOrigin(Vector eye, Vector center, Vector forward)
        => forward * 4f + (eye + center) * 0.5f;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        foreach (var subject in Select(issuer, Rest(argv, 0)))
        {
            var forward = Angles.Vectors(Context.Entities.GetAngles(subject)).Forward;
            var where = SprayOrigin(Context.Entities.GetEyePosition(subject),
                                    Context.Entities.GetWorldSpaceCenter(subject),
                                    forward);

            _effects.BloodEffect(where, forward, BloodColor.Red, Amount,
                                 stream: false, Everything);

            Log.Msg($"bloodspray: {Describe(subject)} bled at {where}.\n");
        }
    }
}

/// <summary>
/// <c>playentitysound &lt;soundscript&gt; [entity]</c> — emits a soundscript
/// entry from whatever is named, or from what the player is looking at.
///
/// <para>
/// NOT STOCK'S. Stock has <c>playgamesound</c>, which plays a script from the
/// LOCAL PLAYER's own position on the client, and nothing at all that emits one
/// from a chosen entity on the server. This is plan_entity.md step 4b's gate
/// driver, and it exists because the thing that step had to demonstrate has no
/// other way to be reached: <b>a sound emitted from a SHELL-LESS entity's
/// position</b>.
/// </para>
///
/// <para>
/// WHY THAT NEEDED A COMMAND AT ALL, measured rather than assumed. On
/// dm_lockdown 81 of the 473 live entities are shell-less — no edict, no entity
/// index, invisible to the engine's own entity list — and NONE of them emits a
/// sound today: the soundscapes use the soundscape path, and every managed
/// class that calls <see cref="IEngineSound.EntityEmitSound"/> is a brush, an
/// item or an effect, all of which have shells. <c>ent_fire</c> cannot reach a
/// shell-less entity either, being the ENGINE's command walking the engine's
/// entity list. So the case existed in the code and could not be produced in a
/// running game.
/// </para>
///
/// <para>
/// It reaches BOTH hierarchies because <see cref="DevCommand.Select"/> does,
/// and because the crossing takes a HANDLE — which is the whole of what step 4b
/// changed underneath it. The line it prints carries the subject's origin and
/// its world-space centre, because those are the two candidate answers for
/// "where did that come from" and a brush entity's origin is usually the map
/// origin rather than anywhere near the brush.
/// </para>
/// </summary>
[ConCommand("playentitysound",
    Help = "Emits a soundscript entry from the given entity(s), or from what you are looking at. (must have sv_cheats)")]
public sealed class PlayEntitySoundCommand : DevCommand
{
    public PlayEntitySoundCommand(DevContext context, IEngineSound sound) : base(context)
    {
        _sound = sound;
    }

    private readonly IEngineSound _sound;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 1 || string.IsNullOrWhiteSpace(argv[0]))
        {
            Log.Msg("playentitysound: usage is `playentitysound <soundscript> [entity]`; " +
                    "with no entity it uses what you are looking at.\n");
            return;
        }

        var script = argv[0];

        foreach (var subject in Select(issuer, Rest(argv, 1)))
        {
            _sound.EntityEmitSound(subject, script);

            Log.Msg($"playentitysound: {Describe(subject)} emitted '{script}' " +
                    $"— origin {Context.Entities.GetOrigin(subject)}, " +
                    $"centre {Context.Entities.GetWorldSpaceCenter(subject)}.\n");
        }
    }
}

//-----------------------------------------------------------------------------
// THE TWO DEBUG-DRAW COMMANDS, LIVE AGAIN.
//
// `drawline` and `drawcross` registered from game/server/client.cpp, came
// across into shell_client_orphans.cpp at the Phase 4 exit as working native
// commands, and became RetiredDevCommands in Phase 1b when that file was
// deleted -- because what they call, UTIL_AddDebugLine, puts a line into the
// ENGINE's debug overlay and this ABI published no line primitive at all.
// DevCommandsRetired.cs's banner named the one appended slot that would lift
// them: `AddLineOverlay`. Lane 1e-abi appended it, so here they are.
//
// THE DECISION IS ON THIS SIDE AND THE MECHANISM IS ON THE OTHER, which is what
// the split in UTIL_AddDebugLine turned out to be about. That function traced
// MASK_BLOCKLOS to decide white-or-red and then handed the colour to the
// overlay; the trace is a DECISION and the overlay is a MECHANISM. So the trace
// runs here, through IEngineWorldQuery.TraceLine with TraceMask.BlockLos, and
// the slot takes the colour it produced. The native trace's return is
// `tr.fraction < 1.0f || tr.startsolid`, which is stock's colour condition
// written out, so TraceResult.Hit is the same bit stock branched on.
//
// NO DURATION, WHICH IS STOCK'S LIFETIME. AddLineOverlay reads a duration of 0
// as "the twenty-slot pool" -- redrawn every frame, oldest reused, emptied by
// clear_debug_overlays -- which is where UTIL_AddDebugLine put its line. The
// engine's own overlay duration of 0 means ONE FRAME, and a one-frame line from
// a console command is indistinguishable from a command that did nothing.
//
// NeedsIssuer IS FALSE, and that is stock too: neither command reads a player.
// Six floats go in and lines come out, and the server console -- which is how
// every tool in this tree types -- has no command client to name.
//-----------------------------------------------------------------------------

/// <summary>
/// What <c>drawline</c> and <c>drawcross</c> share: the coordinate reading,
/// stock's clamp, and one coloured line.
/// </summary>
public abstract class DebugDrawCommand : DevCommand
{
    protected DebugDrawCommand(DevContext context, IEngineHud hud) : base(context)
    {
        Hud = hud;
    }

    /// <summary>The overlay area. Server-side; the slot is null on the client.</summary>
    protected IEngineHud Hud { get; }

    /// <inheritdoc/>
    protected override bool NeedsIssuer => false;

    /// <summary>
    /// <c>MAX_COORD_FLOAT</c> — half the world, from <c>public/worldsize.h</c>.
    /// Stock clamped every coordinate to ±this before drawing, and so does
    /// this: an overlay asked for at 1e30 is not a drawing, it is a number
    /// that will come out of a float somewhere else as garbage.
    /// </summary>
    public const float MaxCoord = 16384f;

    /// <summary>
    /// One argument as a world coordinate. <c>atof</c> semantics, exactly as
    /// stock read these with: anything unreadable is 0 rather than a refusal,
    /// because <c>drawline 0 0 0 0 0 x</c> drew a line in HL2 too.
    /// </summary>
    protected static float Coord(string? text)
    {
        float parsed = VariantParse.Atof(text);

        return parsed < -MaxCoord ? -MaxCoord
             : parsed >  MaxCoord ?  MaxCoord
             : parsed;
    }

    /// <summary>
    /// Three arguments from <paramref name="argv"/> as a point.
    /// </summary>
    protected static Vector Point(string[] argv, int at)
        => new(Coord(At(argv, at)), Coord(At(argv, at + 1)), Coord(At(argv, at + 2)));

    private static string? At(string[] argv, int index)
        => index >= 0 && index < argv.Length ? argv[index] : null;

    /// <summary>
    /// One line, coloured by whether anything is in the way — white if the
    /// sight line is clear, red if it is not. Returns which it was, so the
    /// command can SAY it: a debug overlay is drawn where the person who asked
    /// for it may not be looking, and "nothing happened" and "it drew and you
    /// cannot see it" are different diseases.
    /// </summary>
    protected bool Line(Vector start, Vector end)
    {
        bool blocked = Context.World.TraceLine(start, end, TraceMask.BlockLos).Hit;

        Hud.AddLineOverlay(start, end,
                           r: 255, g: blocked ? 0 : 255, b: blocked ? 0 : 255,
                           noDepthTest: true, duration: 0f);

        return blocked;
    }
}

/// <summary>
/// Stock's <c>CC_DrawLine</c>: one line between two points, white if nothing is
/// in the way and red if something is.
/// </summary>
[ConCommand("drawline",
    Help = "Draws line between two 3D Points.\n\tWhite if no collision\n" +
           "\tRed if collides with something\n\tArguments: x1 y1 z1 x2 y2 z2")]
public sealed class DrawLineCommand : DebugDrawCommand
{
    public DrawLineCommand(DevContext context, IEngineHud hud) : base(context, hud)
    {
    }

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 6)
        {
            Log.Msg("drawline: usage is `drawline x1 y1 z1 x2 y2 z2`.\n");
            return;
        }

        var start = Point(argv, 0);
        var end = Point(argv, 3);

        bool blocked = Line(start, end);

        Log.Msg($"drawline: {start} -> {end}, " +
                $"{(blocked ? "red (something is in the way)" : "white (clear)")}.\n");
    }
}

/// <summary>
/// Stock's <c>CC_DrawCross</c>: the four body diagonals of a ten-unit cube
/// around a point, which is what a "cross" is here.
/// </summary>
[ConCommand("drawcross",
    Help = "Draws a cross at the given location\n\tArguments: x y z")]
public sealed class DrawCrossCommand : DebugDrawCommand
{
    public DrawCrossCommand(DevContext context, IEngineHud hud) : base(context, hud)
    {
    }

    /// <summary>
    /// The half-extent stock used: <c>mins = (-5,-5,-5)</c>,
    /// <c>maxs = (5,5,5)</c>.
    /// </summary>
    public const float Extent = 5f;

    protected override void Run(EntityHandle issuer, string[] argv)
    {
        if (argv.Length < 3)
        {
            Log.Msg("drawcross: usage is `drawcross x y z`.\n");
            return;
        }

        var at = Point(argv, 0);

        // Stock walked one start/end pair around the cube by adding and
        // subtracting the full extent, which comes out as the four body
        // diagonals. They are written out here rather than walked, because the
        // walk's value was that it was short and its cost was that the third
        // line's endpoints are not obvious from reading it.
        const float e = Extent;

        int blocked = 0;

        blocked += Line(at + new Vector(-e, -e, -e), at + new Vector( e,  e,  e)) ? 1 : 0;
        blocked += Line(at + new Vector( e, -e, -e), at + new Vector(-e,  e,  e)) ? 1 : 0;
        blocked += Line(at + new Vector( e,  e, -e), at + new Vector(-e, -e,  e)) ? 1 : 0;
        blocked += Line(at + new Vector(-e,  e, -e), at + new Vector( e, -e,  e)) ? 1 : 0;

        Log.Msg($"drawcross: 4 lines at {at}, {blocked} of them red.\n");
    }
}
