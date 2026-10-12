using System.Collections.Generic;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// The fake that sees what the game dispatches: a no-op <see cref="ITempEnts"/>
/// (via <see cref="TempEntsDouble"/>) that records every effect dispatch in
/// order, which is how a fact observes a tempent the server sent. Registered
/// by the shared server fixtures so every server-side test spawns against the
/// same silent sink, and read by the tests that care
/// (BoltImpactTests.DispatchedEffectsOn).
///
/// <para>
/// It lives under <c>test/common</c>, not beside its first reader, for the
/// reason the file it derives from records: the managed leg compiles
/// <c>TestServices.Server.cs</c> by glob, and a sink defined inside one
/// project's own test file would leave that glob holding an unresolvable
/// name - the same three-doubles collision <see cref="TempEntsDouble"/> was
/// written to end.
/// </para>
/// </summary>
internal sealed class RecordingEffectDispatch : TempEntsDouble
{
    public readonly List<DispatchedEffect> Effects = new();

    public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
        => Effects.Add(new DispatchedEffect(filter, delay, effectName, data));
}

/// <summary>One effect dispatch, as the sink saw it.</summary>
internal readonly record struct DispatchedEffect(TeFilter Filter, float Delay, string Name, TeEffectData Data);
