using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// WHICH OPS A CLASS ACTUALLY WANTS, computed once at registration by
/// reflection (plan_entity.md §3.4: "computed by reflection
/// (<c>DeclaringType != typeof(Entity)</c>)").
///
/// <para>
/// This is the "does C# override this?" bit the tree did not have. The shell
/// holds the mask in its class descriptor and tests it before crossing, so the
/// cost of an op a class ignores is one AND against a word already in cache
/// rather than a managed transition.
/// </para>
///
/// <para>
/// <b>Reflection over the VIRTUAL, not over a list of names.</b> A hand-kept
/// list of "classes that think" is a second declaration of something the class
/// already states by overriding <c>OnThink</c>, and the two would drift the
/// first time somebody added an override. <c>DeclaringType</c> answers it from
/// the type itself: a method whose most-derived declaration is still
/// <see cref="ScriptedEntity"/>'s is the empty body, and an empty body is
/// exactly the crossing worth not making.
/// </para>
/// </summary>
public static class EntityOpMask
{
    /// <summary>
    /// The bit for one op. <c>ENTITY_OP_BIT</c> in <c>entity_op.h</c>.
    /// </summary>
    public static uint Bit(EntityOp op) => 1u << (int)op;

    /// <summary>
    /// THE OPS THE BIT CANNOT TURN OFF. <c>ENTITY_OP_ALWAYS_MASK</c>.
    ///
    /// <para>
    /// Not the ones nobody wanted to optimise — the ones where the managed side
    /// does BOOKKEEPING rather than merely calling a virtual the class may not
    /// have overridden. <see cref="EntityOp.Spawn"/> and
    /// <see cref="EntityOp.KeyValue"/> create the instance and bind its var
    /// sets; elide them and the entity has no C# object at all, so every later
    /// op finds nothing. <see cref="EntityOp.Removed"/> drops the registry row;
    /// elide it and the registry keeps an instance for every entity the map
    /// ever had. <see cref="EntityOp.FilterPasses"/> is asked ABOUT an entity by
    /// a third party and has no <see cref="ScriptedEntity"/> virtual to reflect
    /// over, so there is nothing for a bit to be computed from.
    /// </para>
    /// </summary>
    public const uint Always =
        (1u << (int)EntityOp.Spawn)
        | (1u << (int)EntityOp.KeyValue)
        | (1u << (int)EntityOp.Removed)
        | (1u << (int)EntityOp.FilterPasses);

    /// <summary>
    /// What a class that has never registered costs: everything.
    /// <c>ENTITY_OP_MASK_ALL</c>.
    /// </summary>
    public const uint All = 0xFFFFFFFFu;

    /// <summary>
    /// WHICH VIRTUAL EACH ELIDABLE OP READS. The whole table, in one place, so
    /// that a fact can walk it rather than restating it.
    ///
    /// <para>
    /// Three ops share <c>OnTouch</c> — <see cref="EntityOp.StartTouch"/>,
    /// <see cref="EntityOp.Touch"/> and <see cref="EntityOp.Impact"/> — because
    /// the shell fires them at three different rates but the managed contract
    /// has one call for "somebody is in me". A class that overrides
    /// <c>OnTouch</c> wants all three; one that does not wants none.
    /// </para>
    ///
    /// <para>
    /// <see cref="EntityOp.Damage"/> reads TWO names. <c>OnDamaged</c> is the
    /// old shape and <c>OnDamagedAt</c> the one that also carries where the
    /// blow landed; the native side calls the same op for both, so overriding
    /// either has to arm it. Missing one would silently make a breakable
    /// invulnerable.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<EntityOp, string[]> Virtuals { get; } =
        new Dictionary<EntityOp, string[]>
        {
            [EntityOp.Input] = new[] { nameof(ScriptedEntity.OnInput) },
            [EntityOp.Think] = new[] { nameof(ScriptedEntity.OnThink) },
            [EntityOp.StartTouch] = new[] { nameof(ScriptedEntity.OnTouch) },
            [EntityOp.Touch] = new[] { nameof(ScriptedEntity.OnTouch) },
            [EntityOp.EndTouch] = new[] { nameof(ScriptedEntity.OnEndTouch) },
            [EntityOp.Impact] = new[] { nameof(ScriptedEntity.OnTouch) },
            [EntityOp.MoveDone] = new[] { nameof(ScriptedEntity.OnMoveDone) },
            [EntityOp.Blocked] = new[] { nameof(ScriptedEntity.OnBlocked) },
            [EntityOp.Damage] = new[]
            {
                nameof(ScriptedEntity.OnDamaged), nameof(ScriptedEntity.OnDamagedAt),
            },
            [EntityOp.Killed] = new[] { nameof(ScriptedEntity.OnKilled) },
            [EntityOp.PhysicsCreated] = new[] { nameof(ScriptedEntity.OnPhysicsCreated) },
        };

    /// <summary>
    /// The mask for one registered class.
    /// </summary>
    /// <param name="type">The <see cref="ScriptedEntity"/> subclass.</param>
    /// <param name="hasBoundInputs">
    /// Whether the class binds any <c>[ScriptedEvent]</c> method. An input
    /// reaches a bound method WITHOUT going through <c>OnInput</c>, so a class
    /// that binds one and overrides nothing still needs
    /// <see cref="EntityOp.Input"/> armed. Passed in rather than re-derived
    /// because the registry has already built that dictionary and a second walk
    /// could disagree with the first.
    /// </param>
    public static uint For(Type type, bool hasBoundInputs)
    {
        ArgumentNullException.ThrowIfNull(type);

        uint mask = Always;

        foreach (var (op, names) in Virtuals)
        {
            foreach (string name in names)
            {
                if (Overrides(type, name))
                {
                    mask |= Bit(op);
                    break;
                }
            }
        }

        if (hasBoundInputs)
            mask |= Bit(EntityOp.Input);

        return mask;
    }

    /// <summary>
    /// Whether <paramref name="type"/> declares a body for a
    /// <see cref="ScriptedEntity"/> virtual, at any depth beneath it.
    ///
    /// <para>
    /// <c>GetBaseDefinition().DeclaringType</c> is the virtual's ROOT and
    /// <c>DeclaringType</c> is where the running body was written. They are
    /// equal exactly when nobody has overridden it. Comparing against
    /// <c>typeof(ScriptedEntity)</c> directly would be wrong for a two-level
    /// hierarchy — a base class that overrides <c>OnTouch</c> and a leaf that
    /// does not still wants the bit, and this gets that right because the
    /// leaf's <c>DeclaringType</c> is the base, not <c>ScriptedEntity</c>.
    /// </para>
    ///
    /// <para>
    /// An overload set is walked whole: a class may add a differently-shaped
    /// <c>OnInput</c> beside the virtual one, and only the member that IS the
    /// virtual counts.
    /// </para>
    /// </summary>
    public static bool Overrides(Type type, string virtualName)
    {
        ArgumentNullException.ThrowIfNull(type);

        foreach (var method in type.GetMethods(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, virtualName, StringComparison.Ordinal))
                continue;

            if (!method.IsVirtual)
                continue;

            var root = method.GetBaseDefinition();

            if (root.DeclaringType != typeof(ScriptedEntity))
                continue;

            if (method.DeclaringType != typeof(ScriptedEntity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The ops a mask arms, in order, for a report.
    /// </summary>
    public static IReadOnlyList<EntityOp> Armed(uint mask)
        => Enum.GetValues<EntityOp>().Where(op => (mask & Bit(op)) != 0).ToArray();
}
