namespace SourceSharp.Server.Interface;

/// <summary>A key a scripted class declares: its name and its C# type's name.</summary>
public sealed record DebugField(string Name, string TypeName);

/// <summary>A declared scripted entity class (group G3).</summary>
/// <param name="ClassName">The map classname.</param>
/// <param name="Kind">How it is backed, e.g. "Logical".</param>
/// <param name="TypeName">The C# type that declares it.</param>
/// <param name="Inputs">Its inputs, in declaration order.</param>
/// <param name="Outputs">Its outputs, in declaration order.</param>
/// <param name="Vars">Its keys.</param>
/// <param name="LiveCount">How many are alive.</param>
public sealed record DebugEntityClass(
    string ClassName, string Kind, string TypeName,
    IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, IReadOnlyList<DebugField> Vars, int LiveCount);

/// <summary>A live entity's key and the text of its value.</summary>
public sealed record DebugVar(string Name, string TypeName, string Value);

/// <summary>What one of a live entity's outputs has done: how often it fired and the last value (null if none).</summary>
public sealed record DebugOutputStat(string Name, int FireCount, string? LastValue);

/// <summary>A live scripted entity (group G3).</summary>
/// <param name="Handle">Its handle; <c>0xFFFFFFFF</c> is none.</param>
/// <param name="Index">Its entity index, or -1.</param>
/// <param name="Valid">Whether the engine still has it.</param>
public sealed record DebugEntityInstance(
    string ClassName, uint Handle, int Index, bool Valid, DebugVec3 Origin, int Health, int SpawnFlags,
    IReadOnlyList<DebugVar> Vars, IReadOnlyList<DebugOutputStat> Outputs);

/// <summary>The server's scripted entity classes and live instances (group G3).</summary>
public interface IDebugEntityCatalog
{
    /// <summary>Every declared class. Callable from any thread.</summary>
    IReadOnlyList<DebugEntityClass> Classes();

    /// <summary>Every live scripted entity. Reads engine state: call it on the game thread.</summary>
    IReadOnlyList<DebugEntityInstance> Instances();
}
