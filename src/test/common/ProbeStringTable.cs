using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A second table, declared here so that discovery has more than one thing to
/// find and so a fact about "every declared table" is not a fact about one.
/// </summary>
[StringTable("SourceSharpTestProbe", 8)]
public interface ProbeStringTable : IEngineStringTable
{
}
