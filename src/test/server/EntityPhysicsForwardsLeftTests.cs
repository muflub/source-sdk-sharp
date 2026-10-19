using System.IO;
using System.Text.RegularExpressions;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The last thin vphysics forwards leaving native and the ABI
/// (plan_physics_bones_native_zero, 2026-10-12), each removal half with the
/// retention half that doing less cannot satisfy: managed still makes the
/// call, on the entity's object, through the vtable.
/// </summary>
public class EntityPhysicsForwardsLeftTests
{
    /// <summary>Each slot that left, with the EntityPhysicsMotion call EngineSide makes in its place and the vtable slot that call reaches.</summary>
    public static readonly TheoryData<string, string, string> Left = new()
    {
        { "EntityGetMass", "Motion.Mass(entity)", "->GetMass((void*)o)" },
        { "EntitySetMass", "Motion.SetMass(entity, mass);", "->SetMass((void*)o, mass)" },
        { "EntityEnableMotion", "Motion.EnableMotion(entity, enable);", "->EnableMotion((void*)o, enable ? (byte)1 : (byte)0)" },
        { "EntityPhysicsWake", "Motion.Wake(entity);", "->Wake((void*)o)" },
        { "EntityPhysicsSleep", "Motion.Sleep(entity);", "->Sleep((void*)o)" },
        { "EntityApplyVelocityImpulse", "Motion.ApplyVelocityImpulse(h, impulse);", "->AddVelocity((void*)o, &linear, null)" },
        { "EntityApplyForceCenter", "Motion.ApplyForceCenter(entity, force);", "->ApplyForceCenter((void*)o, &force)" },
        { "EntityApplyForceOffset", "Motion.ApplyForceOffset(entity, force, worldPoint);", "->ApplyForceOffset((void*)o, &force, &worldPoint)" },
        { "EntityApplyTorque", "Motion.ApplyTorque(entity, torque);", "->ApplyTorqueCenter((void*)o, &torque)" },
        { "EntityAddPhysicsGameFlags", "Motion.AddGameFlags(entity, flags);", "->SetGameFlags((void*)o, flags)" },
        { "EntityBecomePhysicsTrigger", "Motion.BecomeTrigger(entity, isTrigger);", "->BecomeTrigger((void*)o)" },
    };

    private static string Code(params string[] path) =>
        Regex.Replace(File.ReadAllText(RepoLayout.Repo(path)), @"//[^\n]*", "");

    // EngineSide with both sides' halves (plan_shim_areas_split phase 7).
    // The side and the bindings: since plan_shim_areas_split phase 11 a member
    // that calls an area lives in the binding that calls it.
    private static string SideCode() =>
        Regex.Replace(RepoLayout.SharedTypeSource("EngineSide") + "\n" + RepoLayout.SharedTypeSource("EngineBinding"),
                      @"//[^\n]*", "");

    private static string[] AreaSlots() =>
        File.ReadAllLines(RepoLayout.Repo("docs", "engineapi-areas.tsv"))
            .Select(l => l.Split('\t')[0])
            .ToArray();

    [Theory]
    [MemberData(nameof(Left))]
    public void TheSlotLeftTheAbiAndItsBodyLeftNative(string slot, string managedCall, string vtableCall)
    {
        _ = managedCall;
        _ = vtableCall;

        string[] slots = AreaSlots();
        // The table read: the held-pointer read that replaced them is a row.
        Assert.Contains("EntityPhysicsObject", slots);
        Assert.DoesNotContain(slot, slots);

        foreach (string file in new[] { "shell_scripted.cpp", "shell_entityapi.cpp", "shell_worldquery.cpp" })
        {
            string code = Code("src", "server", "native", file);
            Assert.Contains("SourceSharp_ResolveEntity", code);
            Assert.DoesNotContain("SourceSharp_" + slot + "(", code);
        }

        Assert.DoesNotContain(" " + slot + "(", Code("src", "shared", "native", "ishim_areas.h"));
    }

    [Theory]
    [MemberData(nameof(Left))]
    public void ManagedMakesTheCallOnTheObjectThroughTheVtable(string slot, string managedCall, string vtableCall)
    {
        _ = slot;

        Assert.Contains(managedCall, SideCode());
        Assert.Contains(vtableCall, Code("src", "shared", "interop", "EntityPhysicsMotion.cs"));
    }

    /// <summary>The body of the native function <paramref name="signature"/> opens, to its closing brace.</summary>
    private static string Body(string code, string signature)
    {
        int at = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature + " is not in the file");
        int end = code.IndexOf("\n}", at, StringComparison.Ordinal);
        Assert.True(end > at, signature + " has no closing brace");
        return code[at..end];
    }

    /// <summary>The four velocity bodies that stay, each with the shell member it keeps.</summary>
    public static readonly TheoryData<string, string, string> VelocityBodies = new()
    {
        { "shell_entityapi.cpp", "void SourceSharp_EntityGetVelocity(", "pStandalone->GetVelocity()" },
        { "shell_entityapi.cpp", "void SourceSharp_EntitySetVelocity(", "pStandalone->SetVelocity(" },
        { "shell_scripted.cpp", "void SourceSharp_EntityGetAngularVelocity(", "pStandalone->GetAngularVelocity()" },
        { "shell_scripted.cpp", "void SourceSharp_EntitySetAngularVelocity(", "pStandalone->SetAngularVelocity(" },
    };

    [Theory]
    [MemberData(nameof(VelocityBodies))]
    public void AVelocityBodyKeepsTheShellsMemberAndAsksNoObject(string file, string signature, string member)
    {
        string body = Body(Code("src", "server", "native", file), signature);

        Assert.Contains(member, body);
        Assert.DoesNotContain("Physics", body);
        Assert.DoesNotContain("VelocityHome", body);
    }

    [Fact]
    public void ManagedDecidesTheVelocityHome()
    {
        string side = SideCode();
        Assert.Contains("return Motion.Velocity(h);", side);
        Assert.Contains("Motion.SetVelocity(h, velocity);", side);
        Assert.Contains("return Motion.AngularVelocity(entity);", side);
        Assert.Contains("Motion.SetAngularVelocity(entity, degreesPerSecond);", side);

        string motion = Code("src", "shared", "interop", "EntityPhysicsMotion.cs");
        Assert.Contains("return o != 0 ? _v.Velocity(o).Linear : _host.EntityVelocity(entity);", motion);
        Assert.Contains("return o != 0 ? _v.LocalToWorldVector(o, _v.Velocity(o).Angular) : _host.EntityAngularVelocity(entity);", motion);
    }

    [Fact]
    public void TheTeleportsSolverHalfLeftNative()
    {
        string body = Body(Code("src", "server", "native", "shell_entityapi.cpp"), "void SourceSharp_EntityTeleport(");

        Assert.Contains("ent.pStandalone->SetAbsOrigin( vecOrigin );", body);
        Assert.DoesNotContain("SetPosition", body);
        Assert.DoesNotContain("GetPhysics", body);
        Assert.DoesNotContain("Wake", body);
    }

    [Fact]
    public void ManagedMovesTheObjectAfterTheTeleport()
    {
        Assert.Contains("Motion.TeleportObject(h, origin, angles, zeroVelocity);",
                        SideCode());
        Assert.Contains("->SetPosition((void*)o, &origin, &angles, isTeleport ? (byte)1 : (byte)0)",
                        Code("src", "shared", "interop", "EntityPhysicsMotion.cs"));
    }

    [Fact]
    public void TheNativeGameFlagHelpersLeftWithTheirLastCaller()
    {
        string header = Code("src", "shared", "native", "ss_physics_shared.h");
        Assert.Contains("kPhysFlagForceCollideMoveable = 0x1000,", header);
        Assert.DoesNotContain("PhysSetGameFlags(", header);
        Assert.DoesNotContain("PhysClearGameFlags(", header);
    }

    [Fact]
    public void ManagedOrsTheGameFlagsIn()
    {
        Assert.Contains("_v.SetGameFlags(o, (ushort)(_v.GameFlags(o) | (ushort)flags));",
                        Code("src", "shared", "interop", "EntityPhysicsMotion.cs"));
    }

    [Fact]
    public void TheObjectComesFromTheHeldPointerRead()
    {
        Assert.Contains("public nint PhysicsObject(EntityHandle entity) => side.EntityPhysicsObject(entity);",
                        SideCode());
        Assert.Contains("void *SourceSharp_EntityPhysicsObject( uint32_t hEntity )",
                        Code("src", "server", "native", "shell_scripted.cpp"));
    }
}
