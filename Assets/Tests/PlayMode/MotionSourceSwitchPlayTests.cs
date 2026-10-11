using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class MotionSourceSwitchPlayTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;
    private Type phoneType, hardwareType, sampleType;
    private MonoBehaviour phone, hardware, motion;
    private Object previousPhone, previousHardware;

    [SetUp]
    public void SetUp()
    {
        phoneType = RuntimeType("PhoneControllerHub");
        hardwareType = RuntimeType("HardwareControllerHub");
        sampleType = RuntimeType("MotionSample");
        previousPhone = (Object)phoneType.GetField("instance", StaticFields).GetValue(null);
        previousHardware = (Object)hardwareType.GetField("instance", StaticFields).GetValue(null);

        // Inactive hubs skip Awake/Start: no signaling, native WebRTC or USB thread.
        phone = CreateInactiveHub(phoneType);
        hardware = CreateInactiveHub(hardwareType);
        phoneType.GetField("instance", StaticFields).SetValue(null, phone);
        hardwareType.GetField("instance", StaticFields).SetValue(null, hardware);

        motion = (MonoBehaviour)new GameObject("Source switching test sword").AddComponent(RuntimeType("MotionController"));
        motion.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // Restore singleton ownership before deferred destruction runs.
        if (phoneType != null) phoneType.GetField("instance", StaticFields).SetValue(null, previousPhone);
        if (hardwareType != null) hardwareType.GetField("instance", StaticFields).SetValue(null, previousHardware);
        if (motion != null) Object.Destroy(motion.gameObject);
        if (phone != null) Object.Destroy(phone.gameObject);
        if (hardware != null) Object.Destroy(hardware.gameObject);
        yield return null;
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ConnectedPhoneKeepsPriorityUntilDisconnectEvenWhenItsPoseIsStale(int slotIndex)
    {
        SetField(motion, "slot", Enum.ToObject(RuntimeType("ControllerSlot"), slotIndex));
        object phoneSlot = ((Array)GetField(phone, "slots")).GetValue(slotIndex);

        SupplyHardware(slotIndex, 100);
        TickMotion();
        AssertInput("Hardware", true, true, false, 111f);
        AssertSequence(100);
        AssertUpright();
        int hardwareGeneration = Property<int>(motion, "PoseGeneration");

        SetField(phoneSlot, "LatestSample", Sample(7, true, 222f, Time.realtimeSinceStartupAsDouble));
        SetField(phoneSlot, "HasSample", true);
        SetField(phoneSlot, "ChannelOpen", true);
        SupplyHardware(slotIndex, 101);
        Assert.That(Property<bool>(motion, "IsPoseValid"), Is.False, "The previous source's pose is invalid immediately on a source change.");
        TickMotion();
        AssertInput("Phone", true, true, true, 222f);
        AssertSequence(7); // Phone and USB have independent sequence numbers.
        AssertUpright();
        int phoneGeneration = Property<int>(motion, "PoseGeneration");
        Assert.That(phoneGeneration, Is.GreaterThan(hardwareGeneration));

        SetField(phoneSlot, "LatestSample", Sample(7, true, 222f, Time.realtimeSinceStartupAsDouble - 1.0));
        SupplyHardware(slotIndex, 102);
        TickMotion();
        AssertInput("Phone", true, false, false, 0f);
        Assert.That(Property<bool>(motion, "IsStale"), Is.True);
        AssertSequence(7);
        int staleGeneration = Property<int>(motion, "PoseGeneration");
        Assert.That(staleGeneration, Is.GreaterThan(phoneGeneration), "Stale input invalidates swing history.");

        // Use the real disconnect cleanup without constructing a native peer.
        phoneType.GetMethod("ClosePeer", Fields).Invoke(phone, new[] { phoneSlot, "Test disconnect" });
        SupplyHardware(slotIndex, 103);
        Assert.That(Property<bool>(motion, "IsPoseValid"), Is.False);
        TickMotion();
        AssertInput("Hardware", true, true, false, 111f);
        Assert.That(Property<bool>(motion, "IsStale"), Is.False);
        AssertSequence(103);
        AssertUpright();
        Assert.That(Property<int>(motion, "PoseGeneration"), Is.GreaterThan(staleGeneration));
        Assert.That(GetField(hardware, "receiver"), Is.Null, "The test must not start serial I/O.");
        Assert.That(GetField(phone, "signalingSocket"), Is.Null, "The test must not open a signaling socket.");
    }

    private void SupplyHardware(int slotIndex, uint sequence)
    {
        ((Array)GetField(hardware, "latest")).SetValue(Sample(sequence, false, 111f, Time.realtimeSinceStartupAsDouble), slotIndex);
        ((bool[])GetField(hardware, "valid"))[slotIndex] = true;
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FreshButtonsRemainUsableBeforeTheOrientationSensorIsReady(int slotIndex)
    {
        SetField(motion, "slot", Enum.ToObject(RuntimeType("ControllerSlot"), slotIndex));
        object buttons = Activator.CreateInstance(sampleType, new object[] {
            Quaternion.identity, Vector3.zero, Vector3.zero, true, false, false, false,
            1u, (ushort)7, 10d, Time.realtimeSinceStartupAsDouble, true
        });
        ((Array)GetField(hardware, "latest")).SetValue(buttons, slotIndex);
        ((bool[])GetField(hardware, "valid"))[slotIndex] = true;
        TickMotion();
        Assert.That(Property<bool>(motion, "IsPoseValid"), Is.False);
        Assert.That(Property<bool>(motion, "GuardHeld"), Is.True);
        Assert.That(Property<bool>(motion, "RecenterHeld"), Is.True);
        Assert.That(Property<ushort>(motion, "ResetCounter"), Is.EqualTo(7));
        object phoneSlot = ((Array)GetField(phone, "slots")).GetValue(slotIndex);
        SetField(phoneSlot, "LatestSample", buttons); SetField(phoneSlot, "HasSample", true);
        SetField(phoneSlot, "ChannelOpen", true);
        TickMotion();
        Assert.That(Property<bool>(motion, "GuardHeld"), Is.True);
        Assert.That(Property<bool>(motion, "RecenterHeld"), Is.True);
        SetField(phoneSlot, "LatestSample", Activator.CreateInstance(sampleType, new object[] {
            Quaternion.identity, Vector3.zero, Vector3.zero, true, false, false, false,
            1u, (ushort)7, 10d, Time.realtimeSinceStartupAsDouble - 1d, true
        }));
        TickMotion();
        Assert.That(Property<bool>(motion, "GuardHeld"), Is.False);
        Assert.That(Property<bool>(motion, "RecenterHeld"), Is.False);
    }

    private object Sample(uint sequence, bool guard, float speed, double received)
    {
        Quaternion rotation = guard ? Quaternion.Euler(25f, 70f, -10f) : Quaternion.Euler(-30f, -45f, 15f);
        return Activator.CreateInstance(sampleType, new object[]
        {
            rotation, new Vector3(speed, 0f, 0f), Vector3.zero,
            guard, true, true, false, sequence, (ushort)0, (double)sequence * 10.0, received
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FreshPhoneAndHardwareAccelerationProduceOneConsumableThrust(bool usePhone)
    {
        object phoneSlot = ((Array)GetField(phone, "slots")).GetValue(0);
        if (usePhone) SetField(phoneSlot, "ChannelOpen", true);
        uint sequence = 1;
        double timestamp = 0;
        Action<Quaternion, Vector3, bool> supply = (rotation, acceleration, validAcceleration) =>
        {
            timestamp += 20;
            SupplyThrustSample(usePhone, 0, sequence++, timestamp, rotation, acceleration, false, validAcceleration);
        };
        Func<bool> consume = () => (bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null);
        // Calibrate upright, then point the sword forwards before pushing it.
        supply(Quaternion.identity, Vector3.zero, true);
        Quaternion forward = Quaternion.Euler(90, 0, 0);
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero, true);
        Assert.That(consume(), Is.False);
        supply(forward, Vector3.forward * 5, true);
        Assert.That(consume(), Is.False);
        // Reusing the same network packet must not count as sustained motion.
        for (int i = 0; i < 10; i++) TickMotion();
        Assert.That(consume(), Is.False);
        supply(forward, Vector3.forward * 5, true);
        Assert.That(consume(), Is.True);
        Assert.That(consume(), Is.False);
        supply(forward, Vector3.forward * 5, true);
        Assert.That(consume(), Is.False);

        for (int i = 0; i < 30; i++) supply(forward, Vector3.zero, true);
        supply(forward, Vector3.forward * 5, false);
        supply(forward, Vector3.forward * 5, false);
        Assert.That(consume(), Is.False, "A valid pose does not substitute for missing acceleration.");
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero, true);
        supply(forward, Vector3.forward * 5, true);
        supply(forward, Vector3.forward * 5, true);
        if (usePhone)
        {
            SetField(phoneSlot, "LatestSample", Activator.CreateInstance(sampleType, new object[] {
                forward, Vector3.zero, Vector3.forward * 5, false, true, false, true,
                sequence, (ushort)0, timestamp, Time.realtimeSinceStartupAsDouble - 1
            }));
        }
        else ((bool[])GetField(hardware, "valid"))[0] = false;
        Assert.That(consume(), Is.False, "A disconnected source cannot leave a queued thrust.");
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void BothSourcesAndSlotsAllowRepeatedThrustsWithHandRecoveryBeforeCooldown(bool usePhone, int slotIndex)
    {
        SetField(motion, "slot", Enum.ToObject(RuntimeType("ControllerSlot"), slotIndex));
        uint sequence = 1;
        double timestamp = 0;
        Quaternion reference = Quaternion.Euler(-20f, 45f, 30f);
        Quaternion bladeRoll = Quaternion.AngleAxis(23f, Vector3.forward);
        Quaternion forward = reference * Quaternion.Euler(90f, 0f, 0f) * bladeRoll;
        Action<Quaternion, Vector3> supply = (rotation, acceleration) =>
        {
            timestamp += 20;
            SupplyThrustSample(usePhone, slotIndex, sequence++, timestamp, rotation, acceleration);
        };
        Func<bool> consume = () => (bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null);
        supply(reference, Vector3.zero);
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero);
        Assert.That(Quaternion.Angle(Property<Quaternion>(motion, "CurrentLocalRotation"), bladeRoll), Is.LessThan(.05f));

        // Sensor acceleration is local to the device, not the recentered player.
        Quaternion sensorFrame = Quaternion.Inverse(bladeRoll);
        for (int thrust = 0; thrust < 12; thrust++)
        {
            supply(forward, sensorFrame * Vector3.forward * 5f);
            Assert.That(consume(), Is.False);
            for (int i = 0; i < 3; i++) TickMotion();
            Assert.That(consume(), Is.False, "A repeated packet cannot finish an impulse.");
            supply(forward, sensorFrame * Vector3.forward * 5f);
            Assert.That(consume(), Is.True, "thrust " + thrust);
            Assert.That(consume(), Is.False, "Each thrust can be consumed only once.");
            for (int i = 0; i < 18; i++) supply(forward, sensorFrame * Vector3.back * 5f);
            Assert.That(consume(), Is.False, "The hand returning is not an attack.");
            for (int i = 0; i < 10; i++) supply(forward, sensorFrame * new Vector3(6f, 0f, 2f));
            Assert.That(consume(), Is.False);
        }
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void BothSourcesAndSlotsRecognizeNoisyThrustsAfterABriefForwardRelease(bool usePhone, int slotIndex)
    {
        SetField(motion, "slot", Enum.ToObject(RuntimeType("ControllerSlot"), slotIndex));
        uint sequence = 1;
        double timestamp = 0;
        Quaternion reference = Quaternion.Euler(-20f, 45f, 30f);
        Quaternion bladeRoll = Quaternion.AngleAxis(23f, Vector3.forward);
        Quaternion forward = reference * Quaternion.Euler(90f, 0f, 0f) * bladeRoll;
        Quaternion sensorFrame = Quaternion.Inverse(bladeRoll);
        Action<Quaternion, Vector3> supply = (rotation, acceleration) =>
        {
            timestamp += 20;
            SupplyThrustSample(usePhone, slotIndex, sequence++, timestamp, rotation, acceleration);
        };
        Func<bool> consume = () => (bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null);
        supply(reference, Vector3.zero);
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero);
        for (int thrust = 0; thrust < 8; thrust++)
        {
            supply(forward, sensorFrame * Vector3.forward * 6f);
            Assert.That(consume(), Is.False);
            // The actual hand slows and drifts sideways after the initial push.
            supply(forward, sensorFrame * new Vector3(2.5f, 0f, 2.2f));
            Assert.That(consume(), Is.True, "thrust " + thrust);
            Assert.That(consume(), Is.False);
            for (int i = 0; i < 5; i++)
            {
                supply(forward, sensorFrame * new Vector3(2f, 0f, .4f));
                Assert.That(consume(), Is.False);
            }
            for (int i = 0; i < 12; i++)
            {
                supply(forward, sensorFrame * new Vector3(6f, 0f, 2f));
                Assert.That(consume(), Is.False, "Recovery/lateral movement cannot itself attack.");
            }
        }
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void NewInvalidPacketCancelsAQueuedThrustBeforeMotionUpdate(bool usePhone, int invalidKind)
    {
        QueueThrust(usePhone);
        // The hub receives first; execution order must not let MasterController
        // consume the previous pending gesture before MotionController.Update.
        SupplyThrustSample(usePhone, 0, 100, 2000,
            Quaternion.Euler(90f, 0f, 0f), Vector3.zero,
            guard: invalidKind == 2, validAcceleration: invalidKind != 1,
            validOrientation: invalidKind != 0, tickMotion: false);
        Assert.That((bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null), Is.False);
        TickMotion();
        Assert.That((bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecenterCancelsAQueuedThrustBeforeMotionUpdate(bool usePhone)
    {
        QueueThrust(usePhone);
        MonoBehaviour activeHub = usePhone ? phone : hardware;
        activeHub.GetType().GetMethod("Recenter").Invoke(activeHub,
            new[] { Enum.ToObject(RuntimeType("ControllerSlot"), 0) });
        Assert.That((bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null), Is.False);
        TickMotion();
        Assert.That((bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null), Is.False);
    }

    private void QueueThrust(bool usePhone)
    {
        uint sequence = 1;
        double timestamp = 0;
        Quaternion forward = Quaternion.Euler(90f, 0f, 0f);
        Action<Quaternion, Vector3> supply = (rotation, acceleration) =>
        {
            timestamp += 20;
            SupplyThrustSample(usePhone, 0, sequence++, timestamp, rotation, acceleration);
        };
        supply(Quaternion.identity, Vector3.zero);
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero);
        supply(forward, Vector3.forward * 6f);
        supply(forward, Vector3.forward * 6f);
        Assert.That((bool)GetField(motion, "thrustPending"), Is.True);
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    public void GuardOrMissingAccelerationCancelsAnUnconsumedThrust(bool usePhone, bool guard)
    {
        uint sequence = 1;
        double timestamp = 0;
        Quaternion forward = Quaternion.Euler(90f, 0f, 0f);
        Action<Quaternion, Vector3, bool, bool> supply = (rotation, acceleration, held, valid) =>
        {
            timestamp += 20;
            SupplyThrustSample(usePhone, 0, sequence++, timestamp, rotation, acceleration, held, valid);
        };
        supply(Quaternion.identity, Vector3.zero, false, true);
        for (int i = 0; i < 10; i++) supply(forward, Vector3.zero, false, true);
        supply(forward, Vector3.forward * 5f, false, true);
        supply(forward, Vector3.forward * 5f, false, true);
        Assert.That((bool)GetField(motion, "thrustPending"), Is.True);
        supply(forward, Vector3.zero, guard, guard);
        supply(forward, Vector3.zero, false, true);
        Assert.That((bool)motion.GetType().GetMethod("TryConsumeThrust").Invoke(motion, null), Is.False);
    }

    private void SupplyThrustSample(bool usePhone, int slotIndex, uint sequence, double timestamp,
        Quaternion rotation, Vector3 acceleration, bool guard = false, bool validAcceleration = true,
        bool validOrientation = true, bool tickMotion = true)
    {
        object sample = Activator.CreateInstance(sampleType, new object[] {
            rotation, Vector3.zero, acceleration, guard, validOrientation, false, validAcceleration,
            sequence, (ushort)0, timestamp, Time.realtimeSinceStartupAsDouble
        });
        if (usePhone)
        {
            object phoneSlot = ((Array)GetField(phone, "slots")).GetValue(slotIndex);
            SetField(phoneSlot, "ChannelOpen", true);
            SetField(phoneSlot, "LatestSample", sample);
            SetField(phoneSlot, "HasSample", true);
        }
        else
        {
            ((Array)GetField(hardware, "latest")).SetValue(sample, slotIndex);
            ((bool[])GetField(hardware, "valid"))[slotIndex] = true;
        }
        if (tickMotion) TickMotion();
    }

    private void AssertInput(string source, bool connected, bool pose, bool guard, float speed)
    {
        Assert.That(Property<object>(motion, "ActiveSource").ToString(), Is.EqualTo(source));
        Assert.That(Property<bool>(motion, "IsConnected"), Is.EqualTo(connected));
        Assert.That(Property<bool>(motion, "IsPoseValid"), Is.EqualTo(pose));
        Assert.That(Property<bool>(motion, "GuardHeld"), Is.EqualTo(guard));
        Assert.That(Property<float>(motion, "AngularSpeedDegrees"), Is.EqualTo(speed).Within(0.001f));
    }

    private void AssertSequence(uint sequence)
    {
        object[] args = { null };
        Assert.That((bool)motion.GetType().GetMethod("TryGetLatestSample").Invoke(motion, args), Is.True);
        Assert.That(Property<uint>(args[0], "Sequence"), Is.EqualTo(sequence));
    }

    private void AssertUpright() => Assert.That(
        Quaternion.Angle(Property<Quaternion>(motion, "CurrentLocalRotation"), Quaternion.Euler(-90f, 0f, 0f)),
        Is.LessThan(0.05f), "A source change must recenter rather than jump between sensor coordinate frames.");

    private void TickMotion() => motion.GetType().GetMethod("Update", Fields).Invoke(motion, null);
    private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object GetField(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);

    private static MonoBehaviour CreateInactiveHub(Type type)
    {
        GameObject host = new GameObject("Source switching test " + type.Name);
        host.SetActive(false);
        MonoBehaviour component = (MonoBehaviour)host.AddComponent(type);
        component.enabled = false;
        return component;
    }
}
