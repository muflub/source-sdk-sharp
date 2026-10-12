using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_hierarchy</c> — every parent link: both ends, the frame, and both
/// transforms.
///
/// <para>
/// <b>Managed because the registry is.</b> A native command printing this would
/// have to be told what managed code believes, and a report that is told what to
/// say is a second opinion rather than a reading. This walks the same
/// <see cref="Hierarchy.Links"/> the composition walks.
/// </para>
///
/// <para>
/// <b>Why parenting gets a report at all.</b> Its failures are the only ones on
/// this hierarchy that are entirely VISUAL — a child in the wrong place, a child
/// that stopped following, a child following the wrong frame. None of the three
/// prints anything, and a screenshot shows that something is wrong without saying
/// which. So the LOCAL offset is printed beside the WORLD position, because the
/// two together are what says whether a composition is right: a child that moved
/// the same distance as its parent has followed, and a child whose local offset
/// is unchanged has followed CORRECTLY. Only the second survives the parent
/// rotating.
/// </para>
/// </summary>
[ConCommand("cs_sv_hierarchy",
    Help = "Every parent link: both ends, the frame, and both transforms")]
public sealed class HierarchyReportCommand : IConCommand
{
    private readonly StandaloneHierarchy _standalone;
    private readonly IEngineConsole _console;
    private readonly IEngineEntities _entities;

    public HierarchyReportCommand(StandaloneHierarchy standalone, IEngineConsole console,
                                  IEngineEntities entities)
    {
        _standalone = standalone;
        _console = console;
        _entities = entities;
    }

    public void Execute(string args)
    {
        HierarchyDriver driver = _standalone.Parenting;
        Hierarchy model = driver.Model;

        if (model.LinkCount == 0)
        {
            _console.Msg("[SourceSharp] nothing is parented to anything\n");
            return;
        }

        _console.Msg($"[SourceSharp] {model.LinkCount} parent link(s)\n");

        int index = 0;

        foreach (ParentLink link in model.Links)
        {
            string childName = NameOf(link.Child);

            if (!link.Resolved)
            {
                _console.Msg($"  [{index}] {childName} -> '{link.ParentName}' (UNRESOLVED, "
                             + $"attachment '{Or(link.ParentAttachmentName, "-")}')\n");
                index++;
                continue;
            }

            _console.Msg($"  [{index}] {childName} -> "
                         + $"{(link.ParentIsBase ? "CBaseEntity" : "standalone")} "
                         + $"'{link.ParentName}'"
                         + $"{(link.NeedsSweeping ? "  [swept each frame]" : string.Empty)}\n");

            if (link.ParentAttachment > 0)
                _console.Msg($"        attachment {link.ParentAttachment}\n");

            // THE FRAME, AND WHY IT COULD NOT BE BUILT WHEN IT COULD NOT.
            //
            // "The child is in the wrong place" has four causes that look
            // identical from outside — the parent is not where it says, the
            // parent's cached transform was never written, the frame rule took
            // the wrong arm, or the offset is wrong. Printing the parent's probe
            // bits and the frame the composition would actually use separates
            // the first three from the fourth, which is the split no screenshot
            // can make.
            ParentProbe probe = _entities.ParentProbe(link.Parent);

            if (model.TryGetParentFrame(link.Parent, link.ParentAttachment,
                                        out Transform parentFrame))
            {
                Vector at = parentFrame.Translation;

                _console.Msg($"        frame  {at.X:F2} {at.Y:F2} {at.Z:F2}   "
                             + $"kind {model.FrameKindOf(link.Parent, link.ParentAttachment)} "
                             + $"probe {probe}\n");
            }
            else
            {
                _console.Msg($"        frame  COULD NOT BE BUILT   "
                             + $"kind {model.FrameKindOf(link.Parent, link.ParentAttachment)} "
                             + $"probe {probe} block {_entities.GetEngineBlock(link.Parent).Length}B"
                             + " — the child stays where it is\n");
            }

            Vector offset = link.ChildToParent.Translation;

            _console.Msg($"        offset {offset.X:F2} {offset.Y:F2} {offset.Z:F2}\n");

            Vector world = _entities.GetOrigin(link.Child);

            Vector local = world;
            QAngle localAngles = QAngle.FromVector(_entities.GetAngles(link.Child));

            driver.ToLocal(link.Child, ref local, ref localAngles);

            _console.Msg($"        world  {world.X:F2} {world.Y:F2} {world.Z:F2}\n");
            _console.Msg($"        local  {local.X:F2} {local.Y:F2} {local.Z:F2}   "
                         + $"({localAngles.Pitch:F2} {localAngles.Yaw:F2} {localAngles.Roll:F2})\n");

            index++;
        }

        // THE GUARD WORDS, because they are the thing a reader cannot see any
        // other way. Native decides whether to cross on them, so a link that is
        // right in the registry and missing from a block is a child that follows
        // correctly on the frame something happens to sweep it and never
        // otherwise — a bug that looks like a bad composition and is not one.
        _console.Msg($"[SourceSharp] {driver.GuardedEntityCount} entit(y/ies) carry a block guard "
                     + "word (ParentHandle / ChildCount)\n");
    }

    private string NameOf(EntityHandle handle)
    {
        string name = _entities.GetName(handle);

        return name.Length != 0 ? name : Or(_entities.GetClassName(handle), "(gone)");
    }

    private static string Or(string value, string fallback)
        => value.Length != 0 ? value : fallback;
}

/// <summary>
/// <c>cs_sv_parent</c> — parent one entity to another, by name, ACROSS BOTH
/// HIERARCHIES.
///
/// <para>
/// <b>Not a duplicate of <c>cs_standalone_parent</c>, and the difference is the
/// case the shipped content actually asks for.</b> That command resolves both
/// ends through the shim's own vector, so its parent can only be another managed
/// entity — plan_entity.md §3.3 removed the second arm it used to have, and its
/// own comment records that "a name the directory does not hold is simply not a
/// name". A map's parents are its own doors and lifts, which are CBaseEntities.
/// This resolves this hierarchy FIRST and then the SDK's, which is the order the
/// map's <c>parentname</c> is resolved in and the answer to the same ambiguity:
/// two entities of different kinds may carry one targetname, and a map that
/// parents to a name meaning both wants the one it can see.
/// </para>
///
/// <para>
/// <b>Through the driver rather than through <see cref="Hierarchy"/>.</b> A
/// console door that took a shortcut past the entry point the game uses would be
/// a check that agrees with itself: the driver is what writes the block guard
/// words, and a link made without them is a link native never crosses for.
/// </para>
/// </summary>
[ConCommand("cs_sv_parent",
    Help = "cs_sv_parent <child> [parent] [attachment] — parent by name across both "
         + "hierarchies; no parent unparents")]
public sealed class HierarchyParentCommand : IConCommand
{
    private readonly StandaloneHierarchy _standalone;
    private readonly IEngineConsole _console;
    private readonly IEngineEntities _entities;

    public HierarchyParentCommand(StandaloneHierarchy standalone, IEngineConsole console,
                                  IEngineEntities entities)
    {
        _standalone = standalone;
        _console = console;
        _entities = entities;
    }

    public void Execute(string args)
    {
        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 1)
        {
            _console.Msg("usage: cs_sv_parent <child> [parent] [attachment]   "
                         + "(no parent unparents)\n");
            return;
        }

        EntityHandle child = _entities.FindByName(words[0]);

        if (child == EntityHandle.None)
        {
            _console.Msg($"[SourceSharp] no entity of this hierarchy is called '{words[0]}'\n");
            return;
        }

        HierarchyDriver driver = _standalone.Parenting;

        if (words.Length < 2)
        {
            driver.SetParent(child, EntityHandle.None, 0);
            _console.Msg($"[SourceSharp] '{words[0]}' has no parent now\n");
            return;
        }

        // THIS HIERARCHY FIRST, then the SDK's — the order the map's parentname
        // is resolved in.
        EntityHandle parent = _entities.FindByName(words[1]);
        bool onBase = false;

        if (parent == EntityHandle.None)
        {
            parent = _entities.FindBaseByName(words[1]);
            onBase = parent != EntityHandle.None;
        }

        if (parent == EntityHandle.None)
        {
            _console.Msg($"[SourceSharp] no entity at all is called '{words[1]}'\n");
            return;
        }

        int attachment = 0;

        // A NAME OR AN INDEX, because both are things a person has in hand: a
        // mapper writes a name and the resolve pass turns it into an index, and
        // trying the awkward cases by hand is what this command is for.
        if (words.Length >= 3 && !int.TryParse(words[2], out attachment))
            attachment = _entities.LookupAttachmentAny(parent, words[2]);

        if (driver.SetParent(child, parent, attachment))
        {
            _console.Msg($"[SourceSharp] '{words[0]}' now follows "
                         + $"{(onBase ? "CBaseEntity" : "standalone")} '{words[1]}'"
                         + $"{(attachment > 0 ? $" attachment {attachment}" : string.Empty)}\n");
        }
    }
}

/// <summary>
/// <c>cs_sv_setlocal</c> — move a parented entity in its PARENT's frame, not
/// the world's, and print what the world made of it.
///
/// <para>
/// <b>Managed because the door it opens is reached from C# now.</b> The native
/// spelling lived in <c>shell_standalone_hierarchy.cpp</c> and justified itself
/// as "a door into the ABI rather than a report of state: it drives
/// <c>SourceSharp_EntitySetLocalTransform</c>, so what it exercises is the call
/// C# makes". That was the right argument when C# could not reach the slot from
/// a command of its own. It can: <c>ScriptedEntity.LocalOrigin</c> is that
/// property and <see cref="IEngineEntities.SetLocalTransform"/> is the same
/// crossing this types into. The door is still a door, and it is now on the
/// side the caller is on.
/// </para>
///
/// <para>
/// <b>The read-back is the point, and it is taken AFTER the write.</b> A local
/// offset that was accepted and a world position that moved are two different
/// facts, and the composition sits between them — the whole reason to drive
/// this by hand is to watch a parent's rotation applied to an offset you chose.
/// Printing the world position from before the call would show the second fact
/// as though it were the first.
/// </para>
///
/// <para>
/// The native command found its subject by walking every standalone entity and
/// comparing names case-insensitively with <c>StandaloneNameEquals</c>, whose
/// last caller it was. This asks the directory, which is where the question
/// belongs and which answers by index rather than by sweep.
/// </para>
/// </summary>
[ConCommand("cs_sv_setlocal",
    Help = "cs_sv_setlocal <child> <x> <y> <z> — move a parented entity in its "
         + "PARENT's frame, not the world's")]
public sealed class HierarchySetLocalCommand : IConCommand
{
    private readonly IEngineConsole _console;
    private readonly IEngineEntities _entities;

    public HierarchySetLocalCommand(IEngineConsole console, IEngineEntities entities)
    {
        _console = console;
        _entities = entities;
    }

    public void Execute(string args)
    {
        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 4)
        {
            _console.Msg("usage: cs_sv_setlocal <child> <x> <y> <z>\n");
            return;
        }

        EntityHandle child = _entities.FindByName(words[0]);

        if (child == EntityHandle.None)
        {
            _console.Msg($"[SourceSharp] no standalone entity called '{words[0]}'\n");
            return;
        }

        // REFUSED RATHER THAN READ AS ZERO. The native spelling used atof, so
        // `cs_sv_setlocal crate x 0 0` moved the crate to its parent's origin
        // on one axis and reported having done what was asked. A typo in a
        // coordinate is the likeliest thing to go wrong at this console, and it
        // was the one thing that produced no complaint.
        if (!TryVector(words, out Vector local, out string offender))
        {
            _console.Msg($"[SourceSharp] '{offender}' is not a number\n");
            return;
        }

        _entities.SetLocalTransform(child, local, null);

        Vector world = _entities.GetOrigin(child);

        _console.Msg($"[SourceSharp] '{words[0]}' local {local.X:F2} {local.Y:F2} {local.Z:F2} "
                     + $"is world {world.X:F2} {world.Y:F2} {world.Z:F2}\n");
    }

    /// <summary>
    /// The three coordinates from <paramref name="words"/>[1..3], or the first
    /// word that is not one.
    ///
    /// <para>
    /// <see cref="CultureInfo.InvariantCulture"/>, because a console is typed at
    /// in ASCII whatever the machine's locale is: a comma-decimal locale would
    /// otherwise read <c>1.5</c> as <c>15</c>, which is a silent hundred-unit
    /// error in a command whose whole job is to place something precisely.
    /// </para>
    /// </summary>
    internal static bool TryVector(string[] words, out Vector vector, out string offender)
    {
        vector = default;
        offender = string.Empty;

        Span<float> parsed = stackalloc float[3];

        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(words[i + 1], NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsed[i]))
            {
                offender = words[i + 1];
                return false;
            }
        }

        vector = new Vector(parsed[0], parsed[1], parsed[2]);
        return true;
    }
}
