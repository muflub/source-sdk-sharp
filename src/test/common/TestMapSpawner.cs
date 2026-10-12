using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Tests;

/// <summary>
/// A <see cref="MapSpawner"/> for a test that is not about map spawning —
/// plan_entity.md step 2d.
///
/// <para>
/// <c>ServerGameDll</c> takes one, because the parse is a step in its order.
/// The facts about that order are not facts about the spawner, so they get a
/// spawner over an EMPTY registry: no classname is registered, so every block in
/// the lump is residue and its worldspawn reaches <c>IEngineLevel.SpawnWorld</c>.
/// A test that wants the spawner to own something registers it.
/// </para>
/// </summary>
internal static class TestMapSpawner
{
    /// <param name="registry">
    /// The registry the spawner asks whether a classname is one of ours. Its own,
    /// empty, unless a caller has one it wants consulted.
    /// </param>
    /// <param name="spawnOrder">
    /// The spawn-priority rule. A REAL one by default, not a double: it is a
    /// dictionary and a stable sort with no dependencies, and what a test of the
    /// spawn pass wants to know is what the ORDER did — which a real one answers
    /// and a double would only record.
    /// </param>
    internal static MapSpawner Over(IEngineEntities entities, IEngineLevel level,
                                    ScriptedEntityRegistry? registry = null,
                                    TemplateStore? templates = null,
                                    IServerMapSpawnOrder? spawnOrder = null)
        => new(new RecordingLog(), registry ?? new ScriptedEntityRegistry(),
               new HierarchyDriver(new NoWorld()),
               templates ?? new TemplateStore(new RecordingLog()),
               entities,
               level,
               spawnOrder ?? new MapSpawnOrder(),
               new EntityFactories());

    /// <summary>
    /// A hierarchy world that answers nothing.
    ///
    /// <para>
    /// The production one is <c>EngineHierarchyWorld</c>, which reads the side's
    /// entity area; in a test there is no engine behind it and a read would
    /// answer a plausible zero rather than saying so. Nothing here is
    /// asked anything: the spawner's only call is
    /// <c>HierarchyDriver.ResolveParentNames</c>, and with no deferred name
    /// recorded that pass touches nothing.
    /// </para>
    /// </summary>
    private sealed class NoWorld : IComposedWorld
    {
        public bool IsComposing => false;

        public bool IsStandalone(EntityHandle handle) => false;
        public bool IsBase(EntityHandle handle) => false;
        public bool CanPose(EntityHandle handle) => false;

        public bool TryGetEntityFrame(EntityHandle handle, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }

        public bool TryGetAttachmentFrame(EntityHandle handle, int attachment, out Transform frame)
        {
            frame = Transform.Identity;
            return false;
        }

        public int LookupAttachment(EntityHandle handle, string attachmentName) => -1;

        public bool TryGetAbsolute(EntityHandle handle, out Vector origin, out QAngle angles)
        {
            origin = Vector.Zero;
            angles = QAngle.Zero;
            return false;
        }

        public void WriteComposed(EntityHandle child, Vector origin, QAngle angles,
                                  Transform childToWorld)
        {
        }

        public EntityHandle FindStandaloneByName(string name) => EntityHandle.None;

        public Span<byte> BlockOf(EntityHandle entity) => Span<byte>.Empty;

        public EntityHandle FindBaseByName(string name) => EntityHandle.None;

        public string NameOf(EntityHandle handle) => string.Empty;

        public void Remove(EntityHandle handle) { }

        public void DetachPhysics(EntityHandle handle) { }

        /// <summary>
        /// Kept rather than swallowed: a warning from the driver in a test that
        /// is not about the driver is something a reader should see, and the
        /// suite's console is where it belongs.
        /// </summary>
        public void Warn(string message) => Console.Error.Write(message);
    }
}
