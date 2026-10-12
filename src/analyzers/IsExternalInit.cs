namespace System.Runtime.CompilerServices;

/// <summary>
/// The marker the compiler emits for an init-only setter, which is what a
/// record's positional properties are.
///
/// netstandard2.0 predates it and does not ship one, and a generator has to
/// target netstandard2.0 because the compiler loads it into a host that is one.
/// Declaring it here is the ordinary way out: the compiler only needs the type
/// to exist, and this file is compiled into the generator alone.
/// </summary>
internal static class IsExternalInit
{
}
