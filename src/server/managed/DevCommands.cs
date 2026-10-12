//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Reflection;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The development console's shared state — stock's <c>ent_*</c> debugging
/// family, <c>give</c>, <c>fov</c> and the rest of the commands whose handlers
/// resolved their subject as a <c>CBasePlayer</c>, got NULL, and returned
/// without a word.
///
/// <para>
/// Forty-three registered server ConCommands were in that state, catalogued in
/// docs/divergences.md under "Verdict A". Not one of them was broken by its own
/// logic — the logic never ran, because two lines in
/// <c>ToBasePlayer( UTIL_GetCommandClient() )</c> answered NULL for a standalone
/// player and every one of them returned. From a console that is
/// indistinguishable from a broken game: the word is accepted and nothing at all
/// happens.
/// </para>
///
/// <para>
/// THE RULE THIS FILE ENFORCES: <b>no command answers with silence.</b> Every
/// branch — did it, refused it, cannot do it in this build — prints a line
/// naming the command and the reason. A retired command says why it is retired
/// rather than being left registered and dead, because "registered and dead" is
/// exactly the state that made this class of bug cost six separate
/// investigations.
/// </para>
///
/// <para>
/// ORDINARY <see cref="ConCommandAttribute"/> CLASSES, one per spelling, and
/// that is a correction. The first version of this file was a verb table and a
/// switch reached through the CLIENT COMMAND crossing — a second console-command
/// mechanism beside the one this project already had, which made these
/// forty-three the only commands in the mod <c>./debug-api commands managed</c>
/// could not see and the only ones with no help text of their own. It was built
/// that way because of one missing fact: <c>ConCommandDispatch</c> carries an id
/// and an argument string, so a managed command could not name the player who
/// ran it. One appended engine slot (<see cref="EngineSide.GetCommandClient"/>)
/// supplies it, and the table, the switch, the crossing and thirty-nine C++
/// registrations all go.
/// </para>
///
/// <para>
/// Managed rather than C++ because everything these commands need is already on
/// the managed engine surface — <see cref="IEngineEntities.Create"/>,
/// <see cref="IEngineEntities.SetKeyValue"/>,
/// <see cref="IEngineWorldQuery.TracePlayerAim"/> — and all of it reaches BOTH
/// hierarchies, which is the whole difficulty the C++ versions had. What is left
/// in C++ is the <c>#ifndef SOURCESHARP</c> guard at each stock registration
/// site, and nothing else.
/// </para>
///
/// <para>
/// THE sv_cheats GATE IS IN <see cref="DevCommand.Execute"/>, per the
/// <see cref="ImpulseCheats"/> and <see cref="DamageCheats"/> precedent, and
/// never on a command crossing — a crossing's gate swallowed ordinary commands
/// like <c>lastinv</c> silently for a while, and putting it back there would
/// repeat that. It is per command, because stock's own flags differ:
/// <c>give</c> is allowed in single player without cheats exactly as stock
/// allowed it, and everything stock marked FCVAR_CHEAT stays gated here.
/// </para>
///
/// <para>
/// NO FCVAR_CHEAT on the engine registrations either, for the same reason it was
/// left off the C++ ones: the engine's flag check is a second, path-dependent
/// gate that refuses differently depending on whether the word came from a
/// client console, the server console or <c>engine-&gt;ServerCommand</c>, and it
/// refuses in SILENCE. The one gate these answer to is the explicit read below,
/// which refuses out loud.
/// </para>
/// </summary>
public sealed class DevContext
{
    /// <summary>
    /// The development commands' log: the composition's, handed in (statics
    /// to zero: it was the static Log).
    /// </summary>
    public ILog Log { get; }

    /// <summary>
    /// How far the picker looks, in units. Stock's <c>FindPickerEntity</c>
    /// traced MAX_COORD_RANGE; the same number <see cref="ImpulseCheats"/> uses,
    /// for the same reason — anything past it is scenery either way.
    /// </summary>
    public const float PickRange = 8192f;

    /// <summary>Stock's default for <c>ent_rotate</c> with no argument.</summary>
    public const float DefaultRotate = 7.5f;

    /// <summary>The half-extent of <c>cast_hull</c>'s box, as stock cast it.</summary>
    public const float CastHullExtent = 16f;

    public DevContext(IEngineEntities entities, IEngineWorldQuery world,
                      IEnginePlayers players, IEngineEntityRender render,
                      IWeaponGiver arsenal, IEntityDirectory directory,
                      EntityFactories factories, IEngineConsole console, ILog log)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
        CheatsEnabled = () => ImpulseCheats.CheatsOn(NativeConsole.ValueOf(console, "sv_cheats"));

        Entities = entities;
        Factories = factories;
        World = world;
        Players = players;
        Render = render;
        Arsenal = arsenal;
        Directory = directory;

        // The engine's own answer, through the players area so that a test can
        // replace it with the rest of the engine. Assigned here rather than as a
        // field initialiser because it closes over the injected area.
        Issuer = () => WhoTyped(Players);
    }

    public IEngineEntities Entities { get; }
    public IEngineWorldQuery World { get; }
    public IEnginePlayers Players { get; }
    public IEngineEntityRender Render { get; }
    public IWeaponGiver Arsenal { get; }
    public IEntityDirectory Directory { get; }

    /// <summary>
    /// This composition's entity factory table: what <c>ent_create</c> asks
    /// before it hedges about a classname (plan_abi_hotreload D7).
    /// </summary>
    public EntityFactories Factories { get; }

    /// <summary>
    /// The sv_cheats gate. A seam so a test can hold it open or shut; in the
    /// game it reads the engine's convar live, per command, through the same
    /// fail-closed reading the impulses and the damage cheats use — missing or
    /// garbage means SHUT.
    /// </summary>
    public Func<bool> CheatsEnabled { get; set; }

    /// <summary>
    /// Whether this is a single-player game — the half of stock's <c>give</c>
    /// gate that is not FCVAR_CHEAT:
    /// <c>pPlayer &amp;&amp; (gpGlobals-&gt;maxClients == 1 || sv_cheats-&gt;GetBool())</c>.
    ///
    /// <para>
    /// UNANSWERABLE IN THIS BUILD, AND THEREFORE NO. <c>maxClients</c> is a
    /// gpGlobals field, not a ConVar — the engine registers <c>maxplayers</c> as
    /// a COMMAND, which has no value to read (verified live: the convar endpoint
    /// answers "a command has no value to read") — and there is no maxclients on
    /// the managed engine surface. An access question nobody can answer is a
    /// refusal, which is the direction <c>ent_fire_access.h</c> takes with the
    /// dedicated server's autokick test for exactly the same reason.
    /// </para>
    ///
    /// <para>
    /// The seam stays because the SHAPE is right and a test needs both answers;
    /// when a maxclients crossing exists, this one line is the whole change.
    /// </para>
    /// </summary>
    public Func<bool> SinglePlayer { get; set; } = () => false;

    /// <summary>
    /// Forgetting a player's current soundscape, so the next frame picks again.
    /// Set by <see cref="ServerModule"/>; null when nothing owns soundscapes,
    /// which <c>soundscape_flush</c> reports rather than pretending.
    /// </summary>
    public Action<EntityHandle>? FlushSoundscape { get; set; }

    /// <summary>
    /// Posting one entity input into the I/O queue, at a time of the caller's
    /// choosing. <c>ent_fire</c>'s whole body, and nothing else uses it.
    ///
    /// <para>
    /// <b>THE QUEUE, NOT <see cref="IEngineEntities.FireInput"/>, AND THE
    /// DIFFERENCE IS THE COMMAND.</b> <c>FireInput</c> delivers to ONE handle
    /// that the caller has already resolved, immediately. <c>ent_fire</c> takes
    /// a target that may be a name, a classname, a wildcard or one of the
    /// procedural names (<c>!activator</c>, <c>!self</c>, <c>!player</c>), may
    /// match several entities at once, may be due seconds from now, and falls
    /// through to the SDK hierarchy when the managed directory answers nothing.
    /// Every one of those is <see cref="EntityDirectory"/>'s delivery, already
    /// written and already the path a map's own connection takes — so the
    /// command posts an event and resolves nothing itself. A second resolver
    /// here would be a second implementation of the queue's, free to drift.
    /// </para>
    ///
    /// <para>
    /// A SEAM rather than a constructor dependency, for
    /// <see cref="FlushSoundscape"/>'s reason: the queue lives on the concrete
    /// <see cref="EntityDirectory"/> and this context is built from interfaces
    /// so that a test can supply all of them. Null means nothing owns a queue,
    /// which <c>ent_fire</c> reports rather than pretending.
    /// </para>
    ///
    /// <para>
    /// The arguments are the queue's own, in its order: target, input, value,
    /// the DELAY in seconds (not an absolute time — the command does not know
    /// what clock the queue keeps), and the activator.
    /// </para>
    /// </summary>
    public Action<string, string, string?, float, EntityHandle>? PostInput { get; set; }

    /// <summary>
    /// Who typed it — stock's <c>UTIL_GetCommandClient</c>, which is the line
    /// every one of these commands opened with. A seam for the same reason the
    /// cheat gate is one: outside a game there is no command client, and a test
    /// that could not name an issuer could not exercise a single one of them.
    /// </summary>
    public Func<EntityHandle> Issuer { get; set; }

    /// <summary>
    /// The player a development command acts for: the console that ran it, or
    /// the only player there is.
    ///
    /// <para>
    /// THE COMMAND CLIENT FIRST, always. That is stock's rule and the only one
    /// that is right with two clients connected — a second player's
    /// <c>ent_create</c> must not act as the first.
    /// </para>
    ///
    /// <para>
    /// THE SOLE PLAYER SECOND, and that half is new. The engine sets no command
    /// client for a word typed at the SERVER console or pushed through
    /// <c>engine-&gt;ServerCommand</c> — which is every tool in this project,
    /// the development API included. Stock's answer there was NULL and silence,
    /// and the version of these commands that preceded this one inherited it
    /// exactly: <c>./debug-api cmd "ent_create prop_physics"</c> answered "no player
    /// issued that" and always would have. A debugging family that cannot be
    /// driven by the only headless driver there is is a debugging family nobody
    /// can check.
    /// </para>
    ///
    /// <para>
    /// It is not a guess. With ONE player connected there is no other candidate,
    /// and "the player" is what a person typing at the server console of their
    /// own listen server means. With none, or with several and nobody claiming
    /// the word, there is a real ambiguity and the answer is
    /// <see cref="EntityHandle.None"/> — refused out loud, as everything here is.
    /// </para>
    /// </summary>
    public static EntityHandle WhoTyped(IEnginePlayers players)
    {
        var typed = players.GetCommandClient();

        return typed != EntityHandle.None
            ? typed
            : SolePlayer(Slots(players));
    }

    /// <summary>Every player slot's answer, empty ones included.</summary>
    private static IEnumerable<EntityHandle> Slots(IEnginePlayers players)
    {
        for (int slot = 1; slot <= EngineLimits.MaxPlayerSlots; slot++)
            yield return players.GetHandle(slot);
    }

    /// <summary>
    /// The one player among a set of slots, or <see cref="EntityHandle.None"/>
    /// when there is not exactly one.
    ///
    /// Pure, and separate from the engine read, because the RULE is the part
    /// worth pinning: "none" and "several" are both None and for opposite
    /// reasons, and a test that had to stand up a server to say so would not be
    /// written.
    /// </summary>
    public static EntityHandle SolePlayer(IEnumerable<EntityHandle> slots)
    {
        var only = EntityHandle.None;

        foreach (var player in slots)
        {
            if (player == EntityHandle.None)
                continue;

            // A second one, so there is no "the player" to mean. Answered here
            // rather than after the walk: nothing further can change it.
            if (only != EntityHandle.None)
                return EntityHandle.None;

            only = player;
        }

        return only;
    }
}

/// <summary>
/// One development console command: the argument split, the issuer, the cheat
/// gate and the shared selection rule, so that a command class is its own
/// behaviour and nothing else.
/// </summary>
public abstract class DevCommand : IConCommand
{
    /// <summary>This command's log: its context's console (statics to zero).</summary>
    protected ILog Log => Context.Log;

    protected DevCommand(DevContext context)
    {
        Context = context;

        // THE NAME COMES FROM THE REGISTRATION, never from a second string. It
        // is printed in front of every line this command writes, and the whole
        // point of the pass is that a person reading the console can tell which
        // word answered — so a class whose message said "ent_info" while the
        // console registered it as "ent_dump" would be the same confusion in a
        // new costume.
        Verb = GetType().GetCustomAttribute<ConCommandAttribute>()?.Name
               ?? GetType().Name.ToLowerInvariant();
    }

    internal DevContext Context { get; }

    /// <summary>The word a player types, as registered.</summary>
    public string Verb { get; }

    /// <summary>
    /// Whether stock marked this FCVAR_CHEAT. True for all but <c>give</c>,
    /// whose gate is stock's own "single player OR cheats" and lives in the
    /// command itself.
    /// </summary>
    protected virtual bool RequiresCheats => true;

    /// <summary>
    /// Whether the command acts on the player who ran it. Every one of these
    /// does — the picker, the aim trace and the arsenal all take an issuer — so
    /// the seam exists to be read, not to be turned off.
    /// </summary>
    protected virtual bool NeedsIssuer => true;

    public void Execute(string args)
    {
        var issuer = Context.Issuer();

        // SAID, not swallowed. Reached with nobody connected, and with several
        // players and a word that came from the server console rather than from
        // any of their consoles — see DevContext.WhoTyped for why the second is
        // a refusal rather than a guess.
        if (NeedsIssuer && issuer == EntityHandle.None)
        {
            Log.Msg($"{Verb}: no player issued that and there is no single player to act for, " +
                    $"so nothing was done.\n");
            return;
        }

        if (RequiresCheats && !Context.CheatsEnabled())
        {
            Log.Msg($"Can't use cheat command {Verb}, unless the server has sv_cheats set to 1.\n");
            return;
        }

        Run(issuer, CommandTokens.Split(args));
    }

    /// <summary>
    /// The command itself. <paramref name="argv"/> is the line AFTER the verb —
    /// <c>CCommand::ArgS</c> is what the engine hands a dispatch, quotes and all,
    /// which is why it still goes through <see cref="CommandTokens.Split"/>.
    /// </summary>
    protected abstract void Run(EntityHandle issuer, string[] argv);

    //-------------------------------------------------------------------------
    // Finding what a command acts on.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's <c>GetNextCommandEntity</c>, on both hierarchies: an empty target
    /// is what the player is LOOKING at, and a written one is a targetname
    /// first and a classname second.
    ///
    /// <para>
    /// Name before class, and class only when the name matched nothing — that is
    /// <c>CEventQueue::ServiceEvents</c>'s order and <c>ent_fire</c>'s, so one
    /// word means the same thing in every command in this family.
    /// </para>
    ///
    /// <para>
    /// EVERY empty answer is said out loud. Stock's returned NULL and its
    /// callers' while loops simply did not run — "nothing is called that" and
    /// "the entity ignored you" were the same silence, which is the bug this
    /// whole pass exists to end.
    /// </para>
    /// </summary>
    public List<EntityHandle> Select(EntityHandle issuer, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            var picked = Context.World.TracePlayerAim(issuer, DevContext.PickRange, out _);

            if (picked == EntityHandle.None)
            {
                Log.Msg($"{Verb}: you are not looking at anything, and no target was named.\n");
                return [];
            }

            return [picked];
        }

        // A NUMBER is stock's third reading — an entity index. There is no
        // index-to-handle crossing on the managed engine surface, so this says
        // so rather than silently reading it as a classname that will never
        // match. A name that happens to be numeric still works, because the
        // name search runs first.
        var byName = Context.Entities.FindAllByName(target);

        if (byName.Count > 0)
            return byName;

        var byClass = Context.Entities.FindAllByClassname(target).ToList();

        if (byClass.Count > 0)
            return byClass;

        Log.Msg(int.TryParse(target, out _)
            ? $"{Verb}: nothing is named '{target}' and nothing is of that class. " +
              $"Stock also read a bare number as an entity index; this build has no " +
              $"index lookup, so name it or use its classname.\n"
            : $"{Verb}: nothing is named '{target}' and nothing is of that class.\n");

        return [];
    }

    /// <summary>An entity in one phrase, for a console line.</summary>
    protected string Describe(EntityHandle entity)
    {
        if (entity == EntityHandle.None || !Context.Entities.IsValid(entity))
            return "nothing";

        string name = Context.Entities.GetName(entity);
        string className = Context.Entities.GetClassName(entity);

        return name.Length > 0
            ? $"{className} '{name}' #{Context.Entities.GetIndex(entity)}"
            : $"{className} #{Context.Entities.GetIndex(entity)}";
    }

    /// <summary>
    /// Where a created entity goes: the point the player is looking at, or
    /// null when the aim hit nothing. A trace that ran its whole range is
    /// stock's <c>tr.fraction == 1.0</c>, and it is measured by distance, not by
    /// what the trace names: a player standing inside solid traces start-solid,
    /// which names the world and still ends at the far end.
    /// </summary>
    protected Vector? AimedSurface(EntityHandle issuer)
    {
        Context.World.TracePlayerAim(issuer, DevContext.PickRange, out var hitPoint);
        float reached = (hitPoint - Context.Entities.GetEyePosition(issuer)).Length;
        return reached < DevContext.PickRange - 1f ? hitPoint : null;
    }

    /// <summary>Everything from an index on, rejoined — the target half of a command line.</summary>
    protected static string Rest(string[] argv, int from)
        => from < argv.Length ? string.Join(' ', argv.Skip(from)) : string.Empty;

    /// <summary>Alternating key/value arguments from an index on. A trailing odd one is dropped.</summary>
    public static List<(string Key, string Value)> KeyValuePairs(string[] argv, int from)
    {
        var pairs = new List<(string, string)>();

        for (int i = from; i + 1 < argv.Length; i += 2)
            pairs.Add((argv[i], argv[i + 1]));

        return pairs;
    }

    /// <summary>
    /// Stock's <c>prop_physics_create</c> model rule: a bare name is under
    /// <c>models/</c> and ends in <c>.mdl</c>, and a full path is left alone.
    /// Separated out because it is the part that is arithmetic on a string and
    /// therefore the part worth a test.
    /// </summary>
    public static string ModelPath(string written)
    {
        string path = written.Replace('\\', '/');

        if (!path.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            path = "models/" + path;

        if (!path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            path += ".mdl";

        return path;
    }

    /// <summary>
    /// A game material as both the letter the engine stores and the name this
    /// mod has for it, or just the letter when it has none.
    /// </summary>
    public static string MaterialName(SurfaceMaterial material)
    {
        int value = (int)material;
        string letter = value is > 32 and < 127 ? $"'{(char)value}'" : value.ToString();

        return Enum.IsDefined(material) ? $"{letter} ({material})" : letter;
    }
}

/// <summary>
/// A command this build cannot honour, and the reason it is gone.
///
/// <para>
/// REGISTERED AND ANSWERING, not registered and silent. Every one of these
/// served a class or a subsystem that left the build; the alternative — toggling
/// a flag nothing reads, or dispatching an effect no renderer receives — is
/// <c>notarget</c>'s mistake, "a lie with a confirmation message". Deleting the
/// registration is not better either: the console would answer "Unknown
/// command", which reads as a typo rather than as a deliberate absence.
/// </para>
///
/// <para>
/// BEFORE THE CHEAT GATE AND BEFORE THE ISSUER CHECK, deliberately: "this build
/// cannot do that" is a more useful answer than "turn on sv_cheats first" for
/// something that will not work either way. A retired command takes no services
/// and touches no engine at all, which makes that true by construction rather
/// than by care.
/// </para>
/// </summary>
public abstract class RetiredDevCommand : IConCommand
{
    /// <summary>Where the retirement is said (statics to zero).</summary>
    protected ILog Log { get; }

    protected RetiredDevCommand(ILog log)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
        Verb = GetType().GetCustomAttribute<ConCommandAttribute>()?.Name
               ?? GetType().Name.ToLowerInvariant();
    }

    /// <summary>The word a player types, as registered.</summary>
    public string Verb { get; }

    /// <summary>Why it is gone. Long enough to be an actual reason.</summary>
    public abstract string Reason { get; }

    public void Execute(string args)
        => Log.Msg($"{Verb}: retired in this build — {Reason}\n");
}

/// <summary>
/// What the development console consists of, read back from the registrations
/// themselves.
///
/// <para>
/// NOT A TABLE BESIDE THEM. The predecessor kept a <c>Verbs</c> set and a
/// <c>Retirements</c> dictionary that its dispatcher consulted, and those could
/// disagree with what the console had actually registered — which is precisely
/// how <c>use</c>, <c>particle_test_start</c> and <c>particle_test_stop</c> came
/// to be guarded out of the stock files, absent from the console, and still
/// listed as answered. This reflects over the classes, so the listing IS the
/// registration.
/// </para>
/// </summary>
public static class DevConsole
{
    /// <summary>Every development command class in this assembly, live or retired.</summary>
    public static IReadOnlyList<Type> All { get; } = typeof(DevConsole).Assembly
        .GetTypes()
        .Where(t => !t.IsAbstract
                 && (typeof(DevCommand).IsAssignableFrom(t) || typeof(RetiredDevCommand).IsAssignableFrom(t))
                 && t.GetCustomAttribute<ConCommandAttribute>() is not null)
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The name a command class registered under.</summary>
    public static string NameOf(Type type)
        => type.GetCustomAttribute<ConCommandAttribute>()!.Name;

    /// <summary>The help a command class registered.</summary>
    public static string HelpOf(Type type)
        => type.GetCustomAttribute<ConCommandAttribute>()!.Help;

    /// <summary>Every command that does something.</summary>
    public static IReadOnlySet<string> Verbs { get; } = All
        .Where(t => typeof(DevCommand).IsAssignableFrom(t))
        .Select(NameOf)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every retired command, and the reason it gives.</summary>
    public static IReadOnlyDictionary<string, string> Retirements { get; } = All
        .Where(t => typeof(RetiredDevCommand).IsAssignableFrom(t))
        .Select(t => (RetiredDevCommand)Activator.CreateInstance(t, new StandardErrorLog())!)
        .ToDictionary(c => c.Verb, c => c.Reason, StringComparer.Ordinal);
}

/// <summary>
/// A console line, split the way a console splits one.
///
/// <para>
/// A dispatch hands managed code <c>CCommand::ArgS</c> — the untouched argument
/// text, quotes and all — so the tokenising the engine already did has to be
/// redone here. Splitting on spaces is what a plain crossing does and it is
/// wrong for anything with a quoted argument —
/// <c>ent_keyvalue mydoor speed "40"</c> and every model path with a space in it
/// — so this is the quote-aware version, and it is a pure function over a string
/// so it can be tested.
/// </para>
///
/// <para>
/// Deliberately NOT a full shell: no escapes, no single quotes, no variable
/// expansion. Source's own <c>CCommand::Tokenize</c> has none of those either,
/// and matching it exactly is the point — a line that works when typed at the
/// game console must mean the same thing here.
/// </para>
/// </summary>
public static class CommandTokens
{
    /// <summary>
    /// The words of a console line. Quotes group; unterminated quotes run to the
    /// end of the line, which is what the engine's tokeniser does rather than
    /// discarding the argument.
    /// </summary>
    public static string[] Split(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return [];

        var tokens = new List<string>();
        var token = new StringBuilder();
        bool quoted = false;
        bool started = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                // A quote always begins a token, so give "" its own empty
                // argument rather than dropping it: a keyvalue set to the empty
                // string is a thing a person means.
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && (c == ' ' || c == '\t' || c == '\n' || c == '\r'))
            {
                if (started)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                    started = false;
                }

                continue;
            }

            token.Append(c);
            started = true;
        }

        if (started)
            tokens.Add(token.ToString());

        return tokens.ToArray();
    }
}
