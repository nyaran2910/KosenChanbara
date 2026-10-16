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
