using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ControllerAxisCalibrationPlayTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour flow, panel, phone, hardware;
    private MonoBehaviour[] sensors;
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Fields).GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);
    private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    private static void Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Fields).Invoke(value, args);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(x => x.GetType().Name == "GameFlowController");
        flow.enabled = false;
        panel = (MonoBehaviour)Field<object>(flow, "axisCalibration"); panel.enabled = false;
        sensors = Field<Array>(flow, "motion").Cast<MonoBehaviour>().ToArray();
        phone = Hub("PhoneControllerHub"); hardware = Hub("HardwareControllerHub");
        foreach (MonoBehaviour sensor in sensors) { Set(sensor, "hub", phone); Set(sensor, "hardware", hardware); }
    }

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub"
                || component.GetType().Name == "MotionController") component.enabled = false;
    }

    private static MonoBehaviour Hub(string name)
    {
        GameObject host = new GameObject("Axis test " + name); host.SetActive(false);
        return (MonoBehaviour)host.AddComponent(Runtime(name));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub")
                Object.Destroy(component.gameObject);
        yield return null;
    }

    private void Supply(bool usePhone, int player, uint sequence, Quaternion pose, Vector3 gyro, ushort reset = 0, bool guard = false, Vector3? acceleration = null)
    {
        object sample = Activator.CreateInstance(Runtime("MotionSample"), new object[] {
            pose, gyro, acceleration ?? Vector3.forward * 5f, guard, true, usePhone, true,
            sequence, reset, sequence * 20d, Time.realtimeSinceStartupAsDouble, false
        });
        if (usePhone)
        {
            object slot = Field<Array>(phone, "slots").GetValue(player);
            Set(slot, "LatestSample", sample); Set(slot, "HasSample", true); Set(slot, "ChannelOpen", true);
        }
        else
        { Field<Array>(hardware, "latest").SetValue(sample, player); Field<bool[]>(hardware, "valid")[player] = true; }
        Call(sensors[player], "Update");
    }

    [UnityTest]
    public IEnumerator DiagnosticShowsCanonicalTipDirectionAndKeepsRecenteringAvailable()
    {
        foreach (bool usePhone in new[] { false, true })
            foreach (int player in new[] { 0, 1 })
            {
                foreach (object state in Field<Array>(phone, "slots"))
                { Set(state, "ChannelOpen", false); Set(state, "HasSample", false); }
                Array.Clear(Field<bool[]>(hardware, "valid"), 0, 2);
                foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");

                Quaternion reference = Quaternion.identity;
                uint sequence = 1;
                Supply(usePhone, player, sequence++, reference, Vector3.zero, acceleration: Vector3.zero);
                if (!(bool)Get(flow, "PauseOpen")) Call(flow, "TogglePause");
                yield return new WaitForSecondsRealtime(.25f);
                Call(flow, "OpenAxisCalibration");
                Supply(usePhone, player, sequence++, reference, Vector3.zero, acceleration: Vector3.zero);
                Call(panel, "SelectPlayer", player);
                yield return null;
                Assert.That(Get(panel, "CurrentStep").ToString(), Is.EqualTo("Diagnostics"));
                Assert.That(Field<GameObject>(panel, "page").GetComponentsInChildren<Text>()
                    .Any(t => t.text.Contains("ゲーム中と同じ姿勢")), Is.True);
                Assert.That(Field<System.Collections.Generic.List<Button>>(panel, "diagnosticButtons").Count, Is.EqualTo(2));
                Call(panel, "Update"); // Establish the rest-pose sample for direction preview.

                Supply(usePhone, player, sequence++, Quaternion.AngleAxis(-30f, Vector3.right),
                    Vector3.left * 900f, acceleration: Vector3.zero);
                Call(panel, "Update");
                Assert.That(Field<Text>(panel, "feedback").text, Does.Contain("↑ 上"));

                yield return new WaitForSecondsRealtime(.25f);
                Supply(usePhone, player, sequence++, Quaternion.AngleAxis(-30f, Vector3.right),
                    Vector3.zero, acceleration: Vector3.zero);
                int generation = (int)Get(sensors[player], "PoseGeneration");
                Field<System.Collections.Generic.List<Button>>(panel, "diagnosticButtons")[0].onClick.Invoke();
                Assert.That(Get(sensors[player], "PoseGeneration"), Is.GreaterThan(generation));
                Set(Field<Array>(phone, "slots").GetValue(player), "ChannelOpen", false);
                Field<bool[]>(hardware, "valid")[player] = false;
                Call(sensors[player], "Update");
                Call(panel, "Update");
                Assert.That(Get(panel, "CurrentStep").ToString(), Is.EqualTo("Player"));
                Call(panel, "Close");
            }
    }

    [UnityTest]
    public IEnumerator MenuSampleKeepsRawPoseAndGyroIndependentOfCombatRecenter()
    {
        foreach (object state in Field<Array>(phone, "slots"))
        { Set(state, "ChannelOpen", false); Set(state, "HasSample", false); }
        Array.Clear(Field<bool[]>(hardware, "valid"), 0, 2);
        foreach (MonoBehaviour sensor in sensors) Call(sensor, "Update");
        Quaternion reference = Quaternion.Euler(23f, -70f, 15f);
        Supply(false, 0, 1, reference, Vector3.zero, acceleration: Vector3.zero);
        Call(sensors[0], "Recenter");
        Supply(false, 0, 2, reference, Vector3.zero, acceleration: Vector3.zero);
        Quaternion latest = reference * Quaternion.Euler(-12f, 8f, 4f);
        Supply(false, 0, 3, latest, Vector3.zero, acceleration: Vector3.zero);
        Quaternion expected = latest;
        object[] args = { null };
        Assert.That(sensors[0].GetType().GetMethod("TryGetMenuSample").Invoke(sensors[0], args), Is.True);
        Assert.That(Quaternion.Angle((Quaternion)Get(args[0], "Rotation"), expected), Is.LessThan(.01f));
        yield return null;
    }
}
