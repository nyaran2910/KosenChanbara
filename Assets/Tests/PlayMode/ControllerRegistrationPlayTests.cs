using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ControllerRegistrationPlayTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour flow, wizard, phone, hardware;
    private MonoBehaviour[] sensors;
    private ControllerPairingRegistry registry;
    private string oldPhone;
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static object Get(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
    private static void Call(object target, string name, params object[] values) => target.GetType().GetMethod(name, Fields).Invoke(target, values);
    private static MonoBehaviour Hub(string name)
    {
        var go = new GameObject("Registration test " + name); go.SetActive(false);
        return (MonoBehaviour)go.AddComponent(Runtime(name));
    }
    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(component.GetType().Name)) component.enabled = false;
    }
    [UnitySetUp]
    public IEnumerator SetUp()
    {
        oldPhone = PlayerPrefs.GetString("PhoneController.p1", "");
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(x => x.GetType().Name == "GameFlowController"); flow.enabled = false;
        wizard = Field<MonoBehaviour>(flow, "registration"); wizard.enabled = false;
        sensors = Field<Array>(flow, "motion").Cast<MonoBehaviour>().ToArray();
        phone = Hub("PhoneControllerHub"); hardware = Hub("HardwareControllerHub");
        registry = new ControllerPairingRegistry(33, 0); Set(hardware, "pairing", registry);
        Set(wizard, "phone", phone); Set(wizard, "hardware", hardware); Set(flow, "phone", phone);
        foreach (MonoBehaviour sensor in sensors) { Set(sensor, "hub", phone); Set(sensor, "hardware", hardware); }
        Call(flow, "TogglePause"); Set(flow, "inputAfter", -1f); Call(flow, "OpenControllerRegistration");
    }
    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        if (wizard != null) Call(wizard, "Close");
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(component.GetType().Name)) Object.Destroy(component.gameObject);
        if (oldPhone.Length == 0) PlayerPrefs.DeleteKey("PhoneController.p1"); else PlayerPrefs.SetString("PhoneController.p1", oldPhone);
        PlayerPrefs.Save(); yield return null;
    }
    [UnityTest]
    public IEnumerator UnconnectedPlayersCanEnrollAndHardwareReturnsToPausedEscWithoutAxes()
    {
        Assert.That(Get(wizard, "CurrentStep").ToString(), Is.EqualTo("Player"));
        Assert.That(Field<GameObject>(wizard, "page").GetComponentsInChildren<Button>().Count(b => b.name == "P1" || b.name == "P2"), Is.EqualTo(2));
        yield return Capture("players.png");
        Call(wizard, "SelectPlayer", 1);
        yield return Capture("waiting.png");
        Assert.That(Get(wizard, "CurrentStep").ToString(), Is.EqualTo("Waiting"));
        Assert.That(registry.WaitingSlot, Is.EqualTo(1));
        var request = new ControllerPairingWire.Frame { Type = ControllerPairingWire.Kind.Request, Device = 11, Request = 22 };
        registry.Handle(request, "peer", 0, out var offer); offer.Type = ControllerPairingWire.Kind.Commit;
        registry.Handle(offer, "peer", 1, out _); registry.Accept(11, offer.Session, 1, 2);
        Call(wizard, "Update");
        Assert.That(Get(wizard, "IsOpen"), Is.False);
        Assert.That(Get(flow, "PauseOpen"), Is.True);
        Assert.That(Field<GameObject>(flow, "pauseCanvas").activeSelf, Is.True);
        Assert.That(Field<MonoBehaviour>(flow, "master").GetType().GetProperty("IsPaused").GetValue(Field<MonoBehaviour>(flow, "master")), Is.True);
        yield return null;
    }
    private object SupplyPhone()
    {
        Call(wizard, "SelectPlayer", 0);
        object slot = Field<Array>(phone, "slots").GetValue(0);
        Set(slot, "ChannelOpen", true); Set(slot, "RegistrationCandidate", true); Set(slot, "PhoneId", "registration-test-phone");
        object sample = Activator.CreateInstance(Runtime("MotionSample"), new object[] {
            Quaternion.identity, Vector3.zero, Vector3.zero, false, true, true, true, 1u, (ushort)0, 10d, Time.realtimeSinceStartupAsDouble, false });
        Set(slot, "HasSample", true); Set(slot, "LatestSample", sample);
        Call(sensors[0], "Update"); Call(wizard, "Update"); return slot;
    }
    private void Ack(object slot)
    {
        int request = Field<int>(slot, "PendingAxisRevision"), order = Field<int>(slot, "PendingOrder"), signs = Field<int>(slot, "PendingSigns");
        Call(phone, "HandleDataChannelMessage", slot, Encoding.UTF8.GetBytes($"{{\"type\":\"axis.state\",\"request\":{request},\"order\":{order},\"signs\":{signs}}}"));
    }
    [UnityTest]
    public IEnumerator AxisPreviewPreservesCalibratedBladePoseAndConfiguredGyroscope()
    {
        object slot = SupplyPhone();
        Quaternion pose = Quaternion.Euler(-65f, 32f, 19f);
        sensors[0].GetType().GetProperty("CurrentLocalRotation").SetValue(sensors[0], pose);
        Call(wizard, "TickSword");
        RectTransform blade = Field<RectTransform>(wizard, "sword");
        Assert.That(Vector3.Angle(blade.localRotation * Vector3.up, pose * Vector3.forward), Is.LessThan(.01f));
        Assert.That(blade.sizeDelta.y, Is.EqualTo(210f));
        // A stationary packet orientation isolates the configured gyro from the
        // world-frame menu projection, which would cancel an axis swap.
        for (int i = -1; i < 3; i++)
        {
            Vector3 gyro = i == 2 ? Vector3.right * 500f : Vector3.up * (i == 0 ? 500f : -500f);
            object sample = Activator.CreateInstance(Runtime("MotionSample"), new object[] {
                Quaternion.Euler(0f, 0f, 90f), gyro, Vector3.zero, false, true, true, true,
                (uint)(i + 2), (ushort)0, 20d + i * 10d, Time.realtimeSinceStartupAsDouble, false });
            Set(slot, "LatestSample", sample);
            Call(sensors[0], "Update");
            Call(wizard, "TickSword");
            if (i < 0) continue;
            Assert.That(Field<Text>(wizard, "feedback").text, Is.EqualTo(i == 0 ? "→ 右" : i == 1 ? "← 左" : "↓ 下"));
        }
        yield return null;
    }
    [UnityTest]
    public IEnumerator PhoneOffersSixOrdersAndIndividualSignsAndWaitsForCommitAcknowledgement()
    {
        object slot = SupplyPhone();
        yield return Capture("axis-order.png");
        Assert.That(Get(wizard, "CurrentStep").ToString(), Is.EqualTo("Order"));
        Assert.That(Field<GameObject>(wizard, "page").GetComponentsInChildren<Button>().Select(b => b.name),
            Is.SupersetOf(new[] { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" }));
        for (int i = 0; i < 6; i++) { Call(wizard, "ChooseOrder", i); Assert.That(Field<int>(slot, "PendingOrder"), Is.EqualTo(i)); }
        Call(wizard, "ShowSigns");
        for (int i = 0; i < 3; i++) Call(wizard, "ToggleAxis", i);
        yield return Capture("axis-signs.png");
        Assert.That(Field<int>(slot, "PendingSigns"), Is.EqualTo(7));
        Call(wizard, "SaveAdjustment"); Call(wizard, "Update");
        Assert.That(Get(wizard, "IsOpen"), Is.True, "An unreliable channel must acknowledge the save before leaving.");
        Assert.That(Field<string>(slot, "AxisAction"), Is.EqualTo("axis.commit"));
        Ack(slot); Call(wizard, "Update");
        Assert.That(Get(wizard, "IsOpen"), Is.False); Assert.That(Get(flow, "PauseOpen"), Is.True);
        Assert.That(PlayerPrefs.GetString("PhoneController.p1"), Is.EqualTo("registration-test-phone"));
        yield return null;
    }
    [UnityTest]
    public IEnumerator CancelClosesEnrollmentAndDelayedCommitCannotReplaceExistingBinding()
    {
        registry.Load(0, 99, 88); Call(wizard, "SelectPlayer", 0);
        registry.Handle(new ControllerPairingWire.Frame { Type = ControllerPairingWire.Kind.Request, Device = 11, Request = 22 }, "peer", 0, out var offer);
        Call(wizard, "ShowPlayers"); offer.Type = ControllerPairingWire.Kind.Commit;
        Assert.That(registry.Handle(offer, "peer", 1, out _), Is.False);
        Assert.That(registry.MatchesBinding(99, 88, 0), Is.True);
        Assert.That(Field<int>(phone, "registrationSlot"), Is.EqualTo(-1));
        yield return null;
    }
    private IEnumerator Capture(string name)
    {
        string folder = Environment.GetEnvironmentVariable("SF_CONTROLLER_PREVIEW_DIR");
        if (string.IsNullOrEmpty(folder)) yield break;
        System.IO.Directory.CreateDirectory(folder);
        yield return null; yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name), image.EncodeToPNG());
        Object.Destroy(image);
    }

}
