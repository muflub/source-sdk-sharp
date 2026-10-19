extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A client entity with nothing on it but its classname, so a fact about the
/// DIRECTORY is not also a fact about an entity class.
/// </summary>
public sealed class DirectoryProbeEntity : ClientStandaloneEntity
{
    public DirectoryProbeEntity(string className)
        : base(ClientDoubles.Fake()) => ClassName = className;

    public override string ClassName { get; }

    /// <summary>
    /// The engine index the shell would have written. Set through the same
    /// internal path the registry uses, so the directory is driven exactly as
    /// production drives it.
    /// </summary>
    public DirectoryProbeEntity At(int index)
    {
        BindIndex(index);
        return this;
    }
}
