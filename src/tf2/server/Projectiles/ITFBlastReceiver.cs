using SourceSharp.Abi;

namespace TF2.Server.Projectiles;

/// <summary>
/// An entity TF2's blast hands itself to whole (<see cref="TFBlastApplier.Apply"/>), in place of
/// tracing to its centre and dealing it one hit: one that stands for many things spread across the
/// map, whose box's centre says nothing about what the blast can see (a mod's single owner of a
/// level's breakable props). It decides what the blast reaches and what each takes.
/// </summary>
public interface ITFBlastReceiver
{
    /// <summary>
    /// The blast <paramref name="blast"/> from <paramref name="source"/> (its centre, lifted as TF
    /// lifts it), set off by <paramref name="attacker"/> through <paramref name="inflictor"/>.
    /// </summary>
    void TakeBlast(Vector source, in TFBlast blast, EntityHandle attacker, EntityHandle inflictor);
}
