using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// vphysics forwarder tables for a fact (plan_engine_forwarders.md): a vphysics
/// service is built over its side's <see cref="VPhysicsTables"/>, and a fact
/// that builds one either fills the tables it calls or, when it never reaches
/// a vphysics object, hands it tables that are all null.
/// </summary>
internal static unsafe class TestVPhysics
{
    /// <summary>
    /// Every table null: for a fact whose subject never calls a vphysics
    /// object. A call that does reach one is a null call, so a subject that
    /// starts calling vphysics fails the fact rather than passing it quietly.
    /// </summary>
    public static VPhysicsTables None() => new(new VPhysicsTables.Set());

    /// <summary>The tables a fact built, already bound.</summary>
    public static VPhysicsTables Of(VPhysicsTables.Set set) => new(set);
}
