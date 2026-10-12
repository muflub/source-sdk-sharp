using System.Buffers.Binary;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A class whose <c>SaveState</c> throws — which is not a hypothetical: an
/// override that dereferences something the level tore down first is the
/// ordinary way it happens, and it happens on the frame a player pressed save.
///
/// <para>
/// The registry's answer is to skip the record and carry the other four
/// hundred, which is right and is argued in <c>Collect</c>'s own header. What
/// A2 is about is that the walk must SAY it did that.
/// </para>
/// </summary>
[ScriptedEntity("save_thrower")]
public sealed class SaveThrowerEntity : ScriptedEntity
{
    [ScriptVars] public ReloadVars Vars = new();

    public override void SaveState(SavedEntity state)
        => throw new InvalidOperationException("the level tore this down first");
}
