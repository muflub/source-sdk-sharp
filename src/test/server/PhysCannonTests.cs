/*
 * weapon_physcannon: the shared state machine (PhysCannonBehaviour) over a
 * scripted world, and the server's grab controller over a recording vphysics.
 * The numbers are stock's (weapon_physcannon.cpp, HL2MP's shared copy).
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class PhysCannonTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int Owner = 1;
    private const int Crate = 40;

    //-------------------------------------------------------------------------
    // The machine, over a world that answers what the fact scripts.
    //-------------------------------------------------------------------------

    private sealed class World(bool authoritative = true) : IPhysCannonWorld
    {
        public bool Authoritative => authoritative;

        public int Held;
        public PhysCannonFind Finds = PhysCannonFind.Denied;
        public PhysCannonPunt Punts = PhysCannonPunt.DryFire;
        public bool Ahead;
        public bool Holds = true;
        public float Distance = 50f;

        public int FindAsks;
        public readonly List<bool> Detaches = [];
        public readonly List<int> Launches = [];
        public readonly List<int> VPhysicsPunts = [];

        public int Attached(int owner) => Held;
        public bool UpdateHeld(int owner) => Holds;

        public void DetachHeld(int owner, bool launched)
        {
            Detaches.Add(launched);
            Held = 0;
        }

        public bool TargetAhead(int owner) => Ahead;

        public PhysCannonFind FindObject(int owner, out int attached)
        {
            FindAsks++;
            attached = Finds == PhysCannonFind.Found ? Crate : 0;
            if (Finds == PhysCannonFind.Found)
                Held = Crate;
            return Finds;
        }

        public float HeldDistance(int owner, int held) => Distance;
        public void Launch(int owner, int entity) => Launches.Add(entity);

        public PhysCannonPunt AimPunt(int owner, out int entity)
        {
            entity = Punts == PhysCannonPunt.DryFire ? 0 : Crate;
            return Punts;
        }

        public void PuntNonVPhysics(int owner, int entity) { }

        public bool PuntVPhysics(int owner, int entity)
        {
            VPhysicsPunts.Add(entity);
            return true;
        }

        public void ViewPunch(int owner, QAngle punch) { }
        public void FireFlash(int owner) { }
        public float RandomFloat(float low, float high) => low;
        public int RandomInt(int low, int high) => low;
    }

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];

        public void Sound(string soundScript) => Sounds.Add(soundScript);
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) => throw new InvalidOperationException("the cannon fired a bullet");
        public void MeleeSwing(in WeaponType type) => throw new InvalidOperationException("the cannon swung");
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => throw new InvalidOperationException("the cannon launched a projectile");

        // The activity order v_physcannon carries: idle 0, hold_idle 1 (RELOAD),
        // draw 2, fire 4 (PRIMARYATTACK), altfire 5 (SECONDARYATTACK).
        public int LookupSequence(WeaponAnim anim) => anim switch
        {
            WeaponAnim.Idle => 0,
            WeaponAnim.Reload => 1,
            WeaponAnim.Draw => 2,
            WeaponAnim.Primary => 4,
            WeaponAnim.Secondary => 5,
            _ => -1,
        };

        public float AnimDuration(WeaponAnim anim) => 0.5f;
    }

    private sealed class Rig
    {
        public readonly World World;
        public readonly Arms Arms = new();
        public readonly WeaponNetState W = new();
        public readonly ViewModelNetState Vm = new();
        public readonly WeaponLogic Machine;

        public Rig(bool authoritative = true)
        {
            World = new World(authoritative);
            W.BindLocal();
            Vm.BindLocal();
            W.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPhyscannon);
            W.OwnerIndex = Owner;
            W.CannonChangeState = (int)PhysCannonElements.None;
            Machine = new WeaponLogic(Arms, WeaponCatalog.Stock(),
                                      new WeaponBehaviours(() => [new PhysCannonBehaviourSource(World)]));
        }

        public void Run(int buttons, float now) => Machine.RunCommand(W, Vm, buttons, now, effectsLive: true);
    }

    [Fact]
    public void TheRowRunsThePhysCannonBehaviourAndSpendsNothing()
    {
        WeaponType row = WeaponCatalog.Stock().For(EntityNames.WeaponPhyscannon)!.Value;

        Assert.Equal((PhysCannonBehaviour.Key, "", -1, 0f), (row.Behaviour, row.AmmoName, row.ClipSize, row.DeployTime));
        Assert.Equal(WeaponTypes.All.Length - 1, WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPhyscannon));
    }

    [Fact]
    public void ThePullAtNothingSaysTooHeavyOncePerPress()
    {
        var rig = new Rig();

        rig.Run(Attack2, 10f);
        rig.Run(Attack2, 10.2f);
        rig.Run(Attack2, 10.4f);
        rig.Run(0, 10.5f);
        rig.Run(Attack2, 10.6f);

        Assert.Equal(4, rig.World.FindAsks);
        Assert.Equal([PhysCannonSounds.TooHeavy, PhysCannonSounds.TooHeavy], rig.Arms.Sounds);
        Assert.Equal(10.7f, rig.W.NextSecondaryAttack, precision: 4);
    }

    [Fact]
    public void APullThatFindsAnObjectHoldsItOpensTheClawsAndDebouncesTheButton()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;

        rig.Run(Attack2, 10f);

        Assert.True(rig.W.CannonActive && rig.W.CannonOpen && rig.W.CannonHeld == Crate,
                    "held, claws open, the object named");
        Assert.Equal((int)PhysCannonEffect.Holding, rig.W.CannonEffect);
        Assert.Equal([PhysCannonSounds.OpenClaws, PhysCannonSounds.Pickup], rig.Arms.Sounds);
        Assert.Equal((4, 10.5f), (rig.Vm.Sequence, rig.W.NextSecondaryAttack));

        // Still held down: the debounce keeps it from dropping or searching again.
        rig.Run(Attack2, 11f);
        Assert.True(rig.W.CannonActive && rig.World.FindAsks == 1 && rig.World.Detaches.Count == 0,
                    "a held button neither drops nor searches");
    }

    [Fact]
    public void PressingTheOtherTriggerAgainDropsWhatIsHeld()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;
        rig.Run(Attack2, 10f);
        rig.Run(0, 10.6f);
        rig.Arms.Sounds.Clear();

        rig.Run(Attack2, 10.7f);

        Assert.Equal([false], rig.World.Detaches);
        Assert.True(!rig.W.CannonActive && rig.W.CannonHeld == 0, "let go");
        Assert.Equal([PhysCannonSounds.Drop], rig.Arms.Sounds);
        Assert.Equal((11.2f, 11.2f), (rig.W.NextPrimaryAttack, rig.W.NextSecondaryAttack));
        Assert.Equal((int)PhysCannonEffect.Ready, rig.W.CannonEffect);
    }

    [Fact]
    public void ThePrimaryLaunchesWhatIsHeld()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;
        rig.Run(Attack2, 10f);
        rig.Run(0, 10.6f);
        rig.Arms.Sounds.Clear();

        rig.Run(Attack, 10.7f);

        Assert.Equal([true], rig.World.Detaches);
        Assert.Equal([Crate], rig.World.Launches);
        Assert.True(!rig.W.CannonActive && rig.W.CannonHeld == 0 && rig.W.CannonLastPunted == Crate,
                    "launched, and remembered so it is not punted again at once");
        Assert.Equal([PhysCannonSounds.Launch], rig.Arms.Sounds);
        Assert.Equal((5, 11.2f, 11.2f), (rig.Vm.Sequence, rig.W.NextPrimaryAttack, rig.W.NextSecondaryAttack));
    }

    [Fact]
    public void AHeldObjectBeyondPuntRangeDryFires()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;
        rig.Run(Attack2, 10f);
        rig.Run(0, 10.6f);
        rig.Arms.Sounds.Clear();
        rig.World.Distance = PhysCannonTuning.TraceLength + 1f;

        rig.Run(Attack, 10.7f);

        Assert.True(rig.W.CannonActive && rig.World.Launches.Count == 0, "still held");
        Assert.Equal([PhysCannonSounds.DryFire], rig.Arms.Sounds);
        Assert.Equal(4, rig.Vm.Sequence);
    }

    [Fact]
    public void ThePrimaryAtNothingDryFiresEveryHalfSecond()
    {
        var rig = new Rig();

        rig.Run(Attack, 10f);
        rig.Run(Attack, 10.25f);
        rig.Run(Attack, 10.5f);

        Assert.Equal([PhysCannonSounds.DryFire, PhysCannonSounds.DryFire], rig.Arms.Sounds);
        Assert.Equal((4, 11f), (rig.Vm.Sequence, rig.W.NextPrimaryAttack));
    }

    [Fact]
    public void APuntOfAPhysicsObjectIsNotRepeatedOnItWithinHalfASecond()
    {
        var rig = new Rig();
        rig.World.Punts = PhysCannonPunt.VPhysics;

        rig.Run(Attack, 10f);
        rig.W.NextPrimaryAttack = 10f; // as if the gun were ready again at once
        rig.Run(Attack, 10.1f);
        rig.Run(Attack, 10.6f);

        Assert.Equal([Crate, Crate], rig.World.VPhysicsPunts);
        Assert.Equal([PhysCannonSounds.Launch, PhysCannonSounds.Launch], rig.Arms.Sounds);
        Assert.Equal((5, (int)PhysCannonEffect.Launch), (rig.Vm.Sequence, rig.W.CannonEffect));
    }

    [Fact]
    public void AnObjectThatCanNoLongerBeHeldIsDropped()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;
        rig.Run(Attack2, 10f);
        rig.Arms.Sounds.Clear();
        rig.World.Holds = false;

        rig.Run(Attack2, 10.1f);

        Assert.True(!rig.W.CannonActive && rig.World.Detaches.Single() == false, "dropped, not launched");
        Assert.Contains(PhysCannonSounds.Drop, rig.Arms.Sounds);
    }

    [Fact]
    public void TheClawsOpenAtATargetAndCloseHalfASecondAfterItGoes()
    {
        var rig = new Rig();
        rig.World.Ahead = true;

        rig.Run(0, 10f);
        Assert.True(rig.W.CannonOpen, "a target ahead opens the claws");

        rig.World.Ahead = false;
        rig.Run(0, 10.1f);
        Assert.True(rig.W.CannonOpen, "the close waits for its debounce");
        rig.Run(0, 10.7f);

        Assert.False(rig.W.CannonOpen);
        Assert.Equal([PhysCannonSounds.OpenClaws, PhysCannonSounds.CloseClaws], rig.Arms.Sounds);
        Assert.Equal((int)PhysCannonEffect.Ready, rig.W.CannonEffect);
    }

    [Fact]
    public void TheReplayNeverSearchesOrOpensTheClawsItself()
    {
        var rig = new Rig(authoritative: false);
        rig.World.Ahead = true;
        rig.World.Finds = PhysCannonFind.Found;

        rig.Run(Attack2, 10f);

        Assert.True(rig.World.FindAsks == 0 && !rig.W.CannonOpen && !rig.W.CannonActive,
                    "the pull and the target check are the server's (stock's #ifndef CLIENT_DLL)");
    }

    [Fact]
    public void TheReplayLaunchesWhatTheServerSaysIsHeld()
    {
        var rig = new Rig(authoritative: false);
        rig.W.CannonActive = true;
        rig.W.CannonHeld = Crate;

        rig.Run(Attack, 10f);

        Assert.True(!rig.W.CannonActive && rig.W.CannonHeld == 0 && rig.W.CannonLastPunted == Crate,
                    "the client predicts the launch of the networked object, as ManagePredictedObject lets stock's");
        Assert.Equal((5, 10.5f), (rig.Vm.Sequence, rig.W.NextPrimaryAttack));
    }

    [Fact]
    public void ItCannotBePutAwayWhileHoldingAndAHolsterLetsGo()
    {
        var rig = new Rig();
        rig.World.Finds = PhysCannonFind.Found;
        rig.Run(Attack2, 10f);
        var behaviour = new PhysCannonBehaviour(rig.World);
        var context = new WeaponAttackContext(WeaponCatalog.Stock().For(EntityNames.WeaponPhyscannon)!.Value,
                                              rig.W, rig.Vm, 11f, true, rig.Arms);

        Assert.False(behaviour.CanHolster(in context));

        behaviour.Holster(in context);

        Assert.True(!rig.W.CannonActive && !rig.W.CannonOpen && behaviour.CanHolster(in context), "let go and closed");
        Assert.Equal((int)PhysCannonEffect.None, rig.W.CannonEffect);
        Assert.False(behaviour.LeavesPickup);
    }

    [Theory]
    [InlineData(10f, 1500f)]
    [InlineData(100f, 1500f)]
    [InlineData(350f, 1100f)]
    [InlineData(600f, 700f)]
    public void TheLaunchSpeedHoldsToAHundredKilosAndSplinesDownToSixHundred(float mass, float speed)
        => Assert.Equal(speed, PhysCannonTuning.LaunchSpeed(mass), precision: 2);

    //-------------------------------------------------------------------------
    // The grab controller, over a recording vphysics.
    //-------------------------------------------------------------------------

    private const nint Obj = 0x7700;
    private static readonly EntityHandle Box = new(0x0002_0028u);

    private sealed class Calls : IVPhysicsGrabCalls
    {
        public Vector Pos = new(100f, 0f, 40f);
        public QAngle Ang = new(0f, 90f, 0f);
        public float MassValue = 30f;
        public float Rot = 0.5f;
        public bool Drag = true;
        public ushort Flags;
        public Vector Linear = new(10f, 0f, 0f);
        public Vector Angular = Vector.Zero;
        public readonly List<ShadowControlParams> Shadows = [];
        public readonly List<Vector> Instantaneous = [];
        public readonly List<(Vector Linear, Vector Angular)> Sets = [];
        public GrabContact[] Touching = [];

        public (Vector Position, QAngle Angles) Position(nint o) => (Pos, Ang);
        public Vector ShadowPosition(nint o) => Pos;
        public float Mass(nint o) => MassValue;
        public void SetMass(nint o, float mass) => MassValue = mass;
        public float RotDamping(nint o) => Rot;
        public void SetRotDamping(nint o, float rotDamping) => Rot = rotDamping;
        public void EnableDrag(nint o, bool enable) => Drag = enable;
        public void Wake(nint o) { }
        public bool IsMoveable(nint o) => true;
        public ushort GameFlags(nint o) => Flags;
        public void SetGameFlags(nint o, ushort flags) => Flags = flags;
        public int MaterialIndex(nint o) => 0;
        public (Vector Linear, Vector Angular) Velocity(nint o) => (Linear, Angular);
        public void SetVelocity(nint o, Vector linear, Vector angular) => Sets.Add((linear, angular));
        public void AddVelocity(nint o, Vector linear, Vector angular) { }
        public void SetVelocityInstantaneous(nint o, Vector linear) => Instantaneous.Add(linear);
        public void ApplyForceCenter(nint o, Vector force) { }
        public void ApplyForceOffset(nint o, Vector force, Vector worldPoint) { }

        public float ComputeShadowControl(nint o, in ShadowControlParams p, float secondsToArrival, float dt)
        {
            Shadows.Add(p);
            return 0f;
        }

        public bool HasContact(nint o) => false;

        public int Contacts(nint o, Span<GrabContact> into)
        {
            Touching.AsSpan().CopyTo(into);
            return Touching.Length;
        }

        public void ClearContactFriction(nint o) { }
    }

    private sealed class Controllers : ICustomMotionControllers
    {
        public IMotionSimulate? Made;
        public readonly List<uint> Destroyed = [];

        public uint CreateCustom(EntityHandle entity, IMotionSimulate simulate)
        {
            Made = simulate;
            return 9u;
        }

        public void Destroy(uint motion) => Destroyed.Add(motion);
    }

    private static (GrabController Grab, Calls V, Controllers C) Grab()
    {
        var v = new Calls();
        var c = new Controllers();
        var physics = TestServices.Mock()
            .Returns("EntityPhysics.PhysicsObject", Obj)
            .Returns("EntityPhysics.CollideExtent", new Vector(84f, 0f, 40f));
        return (new GrabController(v, physics, c), v, c);
    }

    private static GrabMatrix Level => GrabMath.AngleMatrix(QAngle.Zero);

    private static GrabController Holding(GrabController grab, Calls v)
    {
        Assert.True(grab.AttachEntity(Box, Level, GrabMath.AngleMatrix(v.Ang), v.Pos, v.Pos, 0.015f), "attached");
        return grab;
    }

    [Fact]
    public void TakingHoldCarriesTheObjectAtOneKiloWithTenRotDampingAndNoDrag()
    {
        var (grab, v, c) = Grab();
        Holding(grab, v);

        Assert.True(c.Made == grab && grab.Controller == 9u, "a custom controller simulated by the grab");
        Assert.Equal((GrabController.ReducedCarryMass, GrabController.HeldRotDamping, false), (v.MassValue, v.Rot, v.Drag));
        Assert.Equal(GrabController.PlayerHeld, v.Flags);
        Assert.Equal((30f, 30f), (grab.LoadWeight, grab.SavedMass));
    }

    [Fact]
    public void LettingGoGivesBackTheMassDampingAndDragAndClearsTheHeldFlag()
    {
        var (grab, v, c) = Grab();
        Holding(grab, v);

        grab.DetachEntity(clearVelocity: false, linearLimit: 285f);

        Assert.Equal((30f, 0.5f, true, (ushort)0), (v.MassValue, v.Rot, v.Drag, v.Flags));
        Assert.Equal([9u], c.Destroyed);
        Assert.Equal(EntityHandle.None, grab.Attached);
    }

    [Fact]
    public void ALaunchStopsTheObjectDeadBeforeItsVelocityIsSet()
    {
        var (grab, v, _) = Grab();
        Holding(grab, v);

        grab.DetachEntity(clearVelocity: true, linearLimit: 285f);

        Assert.Equal([(Vector.Zero, Vector.Zero)], v.Sets);
    }

    [Fact]
    public void TheHoldPositionIsAheadOfTheEyesClearOfTheCarrier()
    {
        var (grab, v, _) = Grab();
        Holding(grab, v);

        // A carrier at the origin looking level along +x, eyes at 64.
        var view = new GrabView(Vector.Zero, new Vector(0f, 0f, 64f), new Vector(16f, 16f, 72f),
                                new Vector(-16f, -16f, 0f), new Vector(16f, 16f, 72f), new Vector(0f, 0f, 36f), false);

        Assert.True(grab.UpdateObject(in view, Level, 12f, frameCount: 1, frameTime: 0.015f, (_, _, _) => 1f), "held");

        // radial = extent - position = (-16, 0, 0) along -forward: radius =
        // |(16,16)| + 16; distance = 24 + 2 radius; the end is distance - radius
        // ahead. The object's centre is its origin, so there is no offset.
        float radius = MathF.Sqrt(16f * 16f * 2f) + 16f;
        float ahead = 24f + radius * 2f - radius;
        Vector target = grab.Shadow.TargetPosition;
        Assert.Equal((ahead, 0f, 64f), (target.X, target.Y, target.Z), new FloatTripleComparer(0.01f));
    }

    [Fact]
    public void SimulateSteersTheShadowAndScalesItsTurnByTheContact()
    {
        var (grab, v, _) = Grab();
        Holding(grab, v);

        grab.Simulate(Obj, 0.1f, out Vector linear, out _);

        // Free of contact: the contact amount approaches 1 by 2*dt from 0, so 0.2, cubed.
        ShadowControlParams p = v.Shadows.Single();
        Assert.Equal(GrabController.DefaultMaxAngular * 0.2f * 0.2f * 0.2f, p.MaxAngular, precision: 2);
        Assert.Equal((1000f, v.Pos), (p.MaxSpeed, p.TargetPosition));
        Assert.Equal([v.Linear], v.Instantaneous);
        Assert.Equal(Vector.Zero, linear);
    }

    [Fact]
    public void SimulateSlidesAlongAHeavyContactRatherThanIntoIt()
    {
        var (grab, v, _) = Grab();
        Holding(grab, v);
        v.Linear = new Vector(10f, 0f, -5f);
        v.Touching = [new GrabContact(0x99, OtherMoveable: false, OtherMass: 0f, Normal: new Vector(0f, 0f, -1f))];

        grab.Simulate(Obj, 0.1f, out _, out _);

        // Pushing into the floor (normal points away from the object) loses
        // the downward part; and a static contact slows the turn to 0.1's approach.
        Assert.Equal(new Vector(10f, 0f, 0f), v.Instantaneous.Single());
        Assert.Equal(GrabController.DefaultMaxAngular * 0.1f * 0.1f * 0.1f, v.Shadows.Single().MaxAngular, precision: 2);
    }

    [Fact]
    public void ThePickupSeesThePitchedViewAndTheHoldItsYawAlone()
    {
        // Looking 40 degrees down along +y: ItemPostFrame's frame (the pickup)
        // still carries the pitch, ItemPreFrame's (the hold) does not.
        var eyes = new Vector(40f, 90f, 0f);

        Assert.Equal(-MathF.Sin(40f * MathF.PI / 180f), PhysCannonWorld.CarrierTransform(eyes, inPostFrame: true).X.Z, precision: 4);
        Assert.Equal(0f, PhysCannonWorld.CarrierTransform(eyes, inPostFrame: false).X.Z, precision: 4);
        Assert.Equal(1f, PhysCannonWorld.CarrierTransform(eyes, inPostFrame: false).X.Y, precision: 4);
    }

    private sealed class FloatTripleComparer(float tolerance) : IEqualityComparer<(float, float, float)>
    {
        public bool Equals((float, float, float) a, (float, float, float) b)
            => MathF.Abs(a.Item1 - b.Item1) <= tolerance && MathF.Abs(a.Item2 - b.Item2) <= tolerance
               && MathF.Abs(a.Item3 - b.Item3) <= tolerance;

        public int GetHashCode((float, float, float) obj) => 0;
    }
}
