using System;

namespace SourceSharp.Abi;

/// <summary>
/// THE MARKED AND CUSTOM TIERS, AS A WALK OVER ANY OBJECT.
///
/// <para>
/// <b>WHY THIS EXISTS.</b> The hot reload carries state in three tiers
/// (<c>docs/managed-saverestore.md</c>): <b>automatic</b> — what a class
/// declares through <c>[ScriptVar]</c>, <c>[NetField]</c> and its outputs;
/// <b>marked</b> — <see cref="SaveFieldAttribute"/> on a field or property; and
/// <b>custom</b> — <see cref="ISaveCustom"/>. All three are walked by
/// <c>ScriptedEntity.SaveDeclaredState</c>, which is the SERVER's entity base.
/// </para>
///
/// <para>
/// <b>The CLIENT's base walked only the first</b> until plan_client_novalve
/// §3.8: <c>ClientStandaloneEntity.SaveDeclaredState</c> carried its
/// <c>[NetState]</c> objects and nothing else, so a client <c>[SaveField]</c>
/// was silence. It calls this walk now (<c>ClientSaveTierTests</c>).
/// </para>
///
/// <para>
/// <b>WHY A NEW FILE RATHER THAN A CHANGE TO EITHER BASE.</b> Step 6e owns the
/// client ragdoll and neither entity base; <c>ScriptedEntity.cs</c> is the
/// <c>Entity</c> base, which the lane brief names as never a lane's to edit, and
/// <c>ClientStandaloneEntity.cs</c> belongs to step 6b. So the walk lands here,
/// where any class on either side can call it, and
/// <c>.claude/entity-port/requests/6e.md</c> asks the integrator for the two
/// changes that make it the ONE implementation: three lines in each base.
/// </para>
///
/// <para>
/// <b>THE READERS ARE SHARED, ONLY THE WALK IS NOT.</b> Every value here goes
/// through <see cref="SaveFieldCatalog"/>, <see cref="ScriptedVarParse"/>,
/// <see cref="SaveGraph"/> and <see cref="ReloadRecordText"/> — the same four
/// the server's walk uses. So the two cannot disagree about what a value MEANS;
/// they could only disagree about which members are visited, and
/// <c>SaveTierTests.TheTwoWalksAgreeOnTheSameShape</c> is the fact that says
/// they do not. A duplicated implementation with no such fact is drift waiting
/// to happen, which is why the fact exists before the request is answered.
/// </para>
/// </summary>
public static class SaveTiers
{
    /// <summary>
    /// The key an object's custom record is carried under, inside the record's
    /// own local store. The same prefix <c>ScriptedEntity.CustomPrefix</c> uses,
    /// because a record written by one walk has to be readable by the other —
    /// an entity whose class moves between the two sides must not lose its
    /// state on the way.
    /// </summary>
    public const string CustomPrefix = "custom.";

    /// <summary>
    /// The MARKED tier: every field or property of <paramref name="instance"/>
    /// carrying <see cref="SaveFieldAttribute"/>, into the record.
    ///
    /// <para>
    /// A value that will not serialise is named and skipped rather than taking
    /// the whole record down with it. <paramref name="subject"/> is what a
    /// complaint names — the class and instance, for a reader who has to find
    /// which entity it was.
    /// </para>
    /// </summary>
    /// <returns>How many members were written.</returns>
    public static int SaveMarked(object instance, SavedEntity state, string subject, ILog log)
    {
        if (instance is null || state is null)
            return 0;

        int written = 0;

        foreach (SaveFieldInfo marked in SaveFieldCatalog.Build(instance.GetType(), log))
        {
            object? value = marked.Read(instance);

            if (value is null)
            {
                state.Set(marked.Key, string.Empty);
                written++;
                continue;
            }

            // A LEAF GOES THROUGH THE SAME SPELLING THE MAP KEYS USE, so a
            // vector written here reads the way a vector written anywhere else
            // in this record reads. Anything else is an object, and goes through
            // the graph writer with its refusals.
            if (IsLeaf(value))
            {
                state.Set(marked.Key, SaveGraph.LeafText(value));
                written++;
                continue;
            }

            string? text = SaveGraph.Write(value, subject, marked.Key, log);

            if (text is not null)
            {
                state.Set(marked.Key, text);
                written++;
            }
        }

        return written;
    }

    /// <summary>Puts back what <see cref="SaveMarked"/> carried.</summary>
    /// <returns>How many members were written back.</returns>
    /// <param name="handles">The restoring side's handle re-check (plan_abi_hotreload D8).</param>
    public static int LoadMarked(object instance, SavedEntity state, string subject, SavedHandles handles, ILog log)
    {
        if (instance is null || state is null)
            return 0;

        int restored = 0;

        foreach (SaveFieldInfo marked in SaveFieldCatalog.Build(instance.GetType(), log))
        {
            if (!state.Has(marked.Key))
            {
                // NEW IN THIS BUILD. Never saved, so it comes up holding its own
                // initialiser and reports correctly that nobody set it. Not a
                // warning: it is the ordinary case for a field somebody added.
                continue;
            }

            if (!marked.CanWrite)
            {
                log.Warning($"[SourceSharp] {subject}: [SaveField] '{marked.Key}' was " +
                               $"carried but {marked.DeclaredBy}.{marked.MemberName} cannot " +
                               $"be written, so it is NOT restored. A get-only member is " +
                               $"saved for a reader and comes back holding whatever this " +
                               $"build computes\n");
                continue;
            }

            string raw = state.GetString(marked.Key);

            if (raw.Length == 0)
            {
                if (!marked.MemberType.IsValueType)
                {
                    marked.Write(instance, null);
                    restored++;
                }

                continue;
            }

            if (TryParseLeaf(raw, marked.MemberType, out object? value))
            {
                // A HANDLE IS RE-RESOLVED ON THE WAY IN and never handed back
                // raw. The caller cannot see inside its own graph without
                // walking it, and a handle that failed reads as ENTRY ZERO —
                // worldspawn — rather than as nothing.
                if (marked.MemberType == typeof(EntityHandle))
                    value = handles.Resolve((EntityHandle)value!);

                marked.Write(instance, value);
                restored++;
                continue;
            }

            object? built = SaveGraph.Read(raw, marked.MemberType, subject, marked.Key, handles, log);

            if (built is not null)
            {
                marked.Write(instance, built);
                restored++;
            }
        }

        return restored;
    }

    /// <summary>
    /// The CUSTOM tier: a class that implements <see cref="ISaveCustom"/> and
    /// hands over state the declarations cannot describe.
    ///
    /// <para>
    /// A record of its own, flattened into the local store under
    /// <see cref="CustomPrefix"/> — so it reaches a <c>.sav</c> with no format
    /// change, because the local store is already carried by both paths. The
    /// record is dropped when the class wrote nothing.
    /// </para>
    /// </summary>
    /// <returns>True when a record was written.</returns>
    public static bool SaveCustom(object instance, SavedEntity state, string subject, ILog log)
    {
        if (instance is not ISaveCustom custom || state is null)
            return false;

        var record = new ReloadRecord(instance.GetType().FullName ?? subject);
        var access = new SaveRecordAccess(record, subject, log);

        try
        {
            custom.OnSave(access);
        }
        catch (Exception e)
        {
            // NAMED AND SKIPPED. One class's bug must not cost this entity its
            // DECLARED state, which is already in the record by now.
            log.Warning($"[SourceSharp] {subject}: OnSave threw ({e.Message}); its " +
                           $"custom state is not carried. Its declared state still is\n");
            return false;
        }

        if (record.IsEmpty)
            return false;

        ReloadRecordText.Flatten(record, CustomPrefix, state.Set);
        return true;
    }

    /// <summary>Hands back what <see cref="SaveCustom"/> carried.</summary>
    /// <returns>True when the class was given a record.</returns>
    /// <param name="handles">The restoring side's handle re-check (plan_abi_hotreload D8).</param>
    public static bool LoadCustom(object instance, SavedEntity state, string subject, SavedHandles handles, ILog log)
    {
        if (instance is not ISaveCustom custom || state is null)
            return false;

        var record = ReloadRecordText.Unflatten(instance.GetType().FullName ?? subject,
                                                CustomPrefix, state.Local);

        try
        {
            custom.OnRestore(new SaveRecordAccess(record, subject, log, handles));
        }
        catch (Exception e)
        {
            log.Warning($"[SourceSharp] {subject}: OnRestore threw ({e.Message}); its " +
                           $"custom state is not put back. Its declared state already is\n");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a value is written with the map-key spelling rather than through
    /// the graph. The same list <c>ScriptedEntity.SaveMarkedState</c> tests,
    /// which is why it is a method here — one list, and
    /// <c>SaveTierTests</c> compares the two walks over a type that has one of
    /// each.
    /// </summary>
    private static bool IsLeaf(object value)
        => value is string or bool or int or float or Vector or EntityHandle
                 or byte or sbyte or short or ushort or uint or long or ulong or double
                 or QAngle
           || value.GetType().IsEnum;

    /// <summary>
    /// One marked value out of its text, for the types the map-key spelling
    /// already covers. False means "this is not a leaf", which is the caller's
    /// signal to go through the graph reader — NOT "this value is bad".
    /// </summary>
    private static bool TryParseLeaf(string raw, Type type, out object? value)
    {
        value = null;

        if (type == typeof(string)) { value = raw; return true; }

        if (type == typeof(bool))
        {
            if (!ScriptedVarParse.Bool(raw, out bool b)) return false;
            value = b; return true;
        }

        if (type == typeof(int))
        {
            if (!ScriptedVarParse.Int(raw, out int i)) return false;
            value = i; return true;
        }

        if (type == typeof(float))
        {
            if (!ScriptedVarParse.Float(raw, out float f)) return false;
            value = f; return true;
        }

        if (type == typeof(Vector))
        {
            if (!ScriptedVarParse.Vector(raw, out var v)) return false;
            value = v; return true;
        }

        if (type == typeof(QAngle))
        {
            if (!ScriptedVarParse.QAngle(raw, out var q)) return false;
            value = q; return true;
        }

        // The handle's raw value, which is a uint: the invalid handle does not
        // fit an int, and reading it as one lost it (SaveGraph.LeafText).
        if (type == typeof(EntityHandle))
        {
            if (!uint.TryParse(raw, System.Globalization.NumberStyles.Integer,
                               System.Globalization.CultureInfo.InvariantCulture, out uint h)) return false;
            value = new EntityHandle(h); return true;
        }

        if (type.IsEnum)
        {
            try
            {
                value = Enum.Parse(type, raw, ignoreCase: true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // THE REMAINING NUMERIC WIDTHS, through Convert rather than a case each.
        // They are leaves on the way OUT (IsLeaf lists them) and a member that
        // could be written but not read back would be the worst of the three
        // possible answers: carried, silently dropped, and reported as neither.
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(uint) || type == typeof(long) ||
            type == typeof(ulong) || type == typeof(double))
        {
            try
            {
                value = Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }
}
