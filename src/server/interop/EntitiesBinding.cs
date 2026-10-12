using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The entity ops and the entity-scoped asks, managed-built
/// (plan_engineapi_vtables E9): the Entry.cs thunks' bodies and their dispatch
/// helpers, moved. Dispatch keeps its own catch, because a failed FilterPasses
/// answers -1 ("no answer") where the generated thunk's catch would answer 0
/// ("refused"); TemplatesAddNamed keeps its -1 likewise.
/// </summary>
public sealed unsafe class EntitiesBinding(Crossings crossings, ILog log, IServerModule game, LivenessMirror liveness,
                                            EntityRemovalRoutes removal, ShellLessRoutes shellLess) : IManagedEntities
{
    private readonly string[] _dispatchNames = Enum.GetNames<EntityOp>().Select(n => "ServerDispatch." + n).ToArray();

    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    private static Vector ReadVector(float* p)
        => p == null ? default : new Vector(p[0], p[1], p[2]);

    public int Dispatch(uint a0, int a1, void* a2)
    {
        crossings.Count(CrossingOp.ServerDispatch);
        crossings.CountEntityOp(a1);

        var handle = new EntityHandle(a0);
        var kind = (EntityOp)a1;
        var args = (OpArgs*)a2;

        try
        {
            if (kind == EntityOp.Spawn)
                liveness.NoteCreated(handle);

            if (kind == EntityOp.Removed)
            {
                try { return Deliver(handle, kind, args); }
                finally { liveness.NoteRemoved(handle); }
            }

            return Deliver(handle, kind, args);
        }
        catch (Exception e)
        {
            int i = (int)kind;
            ManagedVtable.Fail(log, (uint)i < (uint)_dispatchNames.Length ? _dispatchNames[i] : "ServerDispatch", e);
            return kind == EntityOp.FilterPasses ? -1 : 0;
        }
    }

    private int Deliver(EntityHandle handle, EntityOp op, OpArgs* args)
    {
        var blob = args != null && args->Blob != null && args->BlobBytes > 0
            ? new Span<byte>(args->Blob, args->BlobBytes)
            : Span<byte>.Empty;

        return game.Dispatch(op, handle, blob, DecodeArgs(op, args));
    }

    private static EntityOpArgs DecodeArgs(EntityOp op, OpArgs* args)
    {
        if (args == null)
            return EntityOpArgs.None;

        switch (op)
        {
            case EntityOp.Spawn:
                return EntityOpArgs.None with { ClassName = Utf8(args->Text0) };

            case EntityOp.KeyValue:
                return EntityOpArgs.None with
                {
                    ClassName = Utf8(args->Text0),
                    Text1 = Utf8(args->Text1),
                    Text2 = Utf8(args->Text2),
                };

            case EntityOp.Input:
                return EntityOpArgs.None with
                {
                    Other = new EntityHandle(args->Other),
                    Text1 = Utf8(args->Text1),
                    Text2 = Utf8(args->Text2),
                    Number = args->Float0,
                    Flags = args->Int0,
                };

            case EntityOp.Damage:
                return EntityOpArgs.None with
                {
                    Other = new EntityHandle(args->Other),
                    Number = args->Float0,
                    Flags = args->Int0,
                    Position = ReadVector(args->Vec0),
                    Force = ReadVector(args->Vec1),
                };

            case EntityOp.FilterPasses:
                return EntityOpArgs.None with
                {
                    Other = new EntityHandle(args->Other),
                    Flags = args->Int0,
                    IsDamageQuestion = args->Float0 != 0.0f,
                };

            case EntityOp.StartTouch:
            case EntityOp.Touch:
            case EntityOp.EndTouch:
            case EntityOp.Impact:
            case EntityOp.Blocked:
            case EntityOp.Killed:
                return EntityOpArgs.With(new EntityHandle(args->Other));

            default:
                return EntityOpArgs.None;
        }
    }

    public int EntityRemoveRequested(uint a0)
    {
        Enter(CrossingOp.ServerEntityRemoveRequested);
        return removal.Remove(new EntityHandle(a0)) ? 1 : 0;
    }

    public void OnEntityIgnite(uint a0, float a1, float a2)
    {
        Enter(CrossingOp.ServerOnEntityIgnite);
        game.OnEntityIgnite(new EntityHandle(a0), a1, a2);
    }

    public void OnFireAddHeatInRadius(void* a0, float a1, float a2)
    {
        Enter(CrossingOp.ServerOnFireAddHeatInRadius);
        float* origin = (float*)a0;
        game.AddFireHeatInRadius(new Vector(origin[0], origin[1], origin[2]), a1, a2);
    }

    public int OnRagdollMagnetForce(uint a0, void* a1, void* a2)
    {
        Enter(CrossingOp.ServerOnRagdollMagnetForce);
        float* centre = (float*)a1, outForce = (float*)a2;
        if (centre == null || outForce == null)
            return 0;

        if (game.OnRagdollMagnetForce(new EntityHandle(a0), new Vector(centre[0], centre[1], centre[2])) is not Vector pull)
            return 0;

        outForce[0] = pull.X;
        outForce[1] = pull.Y;
        outForce[2] = pull.Z;
        return 1;
    }

    public int ShellLessFindByName(byte* a0, void* a1, int a2)
    {
        Enter(CrossingOp.ServerShellLessFindByName);
        return ShellLessFind(a0, (uint*)a1, a2, byClassname: false);
    }

    public int ShellLessFindByClassname(byte* a0, void* a1, int a2)
    {
        Enter(CrossingOp.ServerShellLessFindByClassname);
        return ShellLessFind(a0, (uint*)a1, a2, byClassname: true);
    }

    private int ShellLessFind(byte* pQuery, uint* pOut, int nOutMax, bool byClassname)
    {
        if (pQuery == null)
            return 0;

        Span<uint> into = pOut == null || nOutMax <= 0 ? Span<uint>.Empty : new Span<uint>(pOut, nOutMax);
        return shellLess.Find(Utf8(pQuery), byClassname, into);
    }

    public int TemplatesAddNamed(byte* a0, byte* a1, byte* a2, int a3)
    {
        Enter(CrossingOp.ServerTemplatesAddNamed);
        try
        {
            if (a2 == null || a3 <= 0) return -1;

            var mapData = new byte[a3];
            new ReadOnlySpan<byte>(a2, a3).CopyTo(mapData);
            return game.Templates.Add(Utf8(a0), Utf8(a1), mapData);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedEntities.TemplatesAddNamed", e); return -1; }
    }
}
