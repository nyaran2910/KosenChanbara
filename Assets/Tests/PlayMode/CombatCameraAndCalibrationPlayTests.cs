using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class CombatCameraAndCalibrationPlayTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType(name)).First(type => type != null);

    [UnityTest]
    public IEnumerator RepeatedSharedPushesCarryTheSingleCameraAtEveryFrameRate()
    {
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var player = new GameObject("Camera push player");
            var enemy = new GameObject("Camera push enemy");
            var cameraObject = new GameObject("Camera push view", typeof(Camera));
            cameraObject.transform.SetParent(player.transform, false);
            object view = Activator.CreateInstance(RuntimeType("DuelView"), player.transform, enemy.transform, new Renderer[0]);
            try
            {
                enemy.transform.position = Vector3.forward * 2f;
                Call(view, "SetFocusTarget", enemy.transform);
                Call(view, "Tick", 0d);
                Vector3 initialOffset = cameraObject.transform.position - player.transform.position;
                Quaternion initialAim = cameraObject.transform.rotation;
                for (int frame = 1; frame <= fps * 3; frame++)
                {
                    double now = frame / (double)fps;
                    int completed = Mathf.FloorToInt((float)now / .4f);
                    float current = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(((float)now - completed * .4f) / .12f));
                    Vector3 translation = new Vector3(.6f, 0f, 3f) * (completed + current);
                    player.transform.position = translation;
                    enemy.transform.position = translation + Vector3.forward * 2f;
                    Call(view, "Tick", now);
                    Assert.That(Vector3.Distance(cameraObject.transform.position - player.transform.position, initialOffset), Is.LessThan(.003f),
                        "Shared recoil must not accumulate camera lag at " + fps + "fps.");
                    Assert.That(Quaternion.Angle(cameraObject.transform.rotation, initialAim), Is.LessThan(.05f));
                }
                Vector3 beforePause = cameraObject.transform.position;
                Call(view, "Tick", 3d);
                Assert.That(cameraObject.transform.position, Is.EqualTo(beforePause), "A suspended clock must not advance camera easing.");
            }
            finally { Call(view, "Dispose"); Object.Destroy(player); Object.Destroy(enemy); }
        }
        yield return null;
    }

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(component.GetType().Name)) component.enabled = false;
    }

    [UnityTest]
    public IEnumerator PlayingPauseCanCalibrateAxesAndReturnWithoutRestartingOrResumingEarly()
    {
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        MonoBehaviour[] components = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        MonoBehaviour master = components.Single(component => component.GetType().Name == "MasterController");
        MonoBehaviour flow = components.Single(component => component.GetType().Name == "GameFlowController");
        master.enabled = flow.enabled = false;
        Call(flow, "ChooseMode", false); Call(flow, "Ready", 0); Call(flow, "Ready", 1); Call(flow, "BeginMatch");
        Call(flow, "TogglePause");
        Set(flow, "inputAfter", -100f);
        object panel = Field<object>(flow, "axisCalibration");
        Call(panel, "Update");
        Button entry = (Button)panel.GetType().GetProperty("EntryButton").GetValue(panel);
        Assert.That(entry.isActiveAndEnabled, Is.True, "The playing Esc menu exposes axis calibration.");
        double before = Field<double>(master, "matchTime");
        Vector3 position = Field<GameObject>(master, "PlayerObject1").transform.position;
        entry.onClick.Invoke();
        Assert.That((bool)panel.GetType().GetProperty("IsOpen").GetValue(panel), Is.True);
        Assert.That((bool)flow.GetType().GetProperty("PauseOpen").GetValue(flow), Is.True);
        Assert.That((bool)master.GetType().GetProperty("IsPaused").GetValue(master), Is.True);
        Call(panel, "Close");
        Assert.That((bool)flow.GetType().GetProperty("PauseOpen").GetValue(flow), Is.True, "Closing calibration returns to the suspended game menu.");
        Assert.That(Field<GameObject>(flow, "pauseCanvas").activeSelf, Is.True);
        Assert.That(Field<double>(master, "matchTime"), Is.EqualTo(before));
        Call(flow, "TogglePause");
        Assert.That((bool)master.GetType().GetProperty("IsPaused").GetValue(master), Is.False);
        Assert.That(Field<double>(master, "matchTime"), Is.EqualTo(before));
        Assert.That(Field<GameObject>(master, "PlayerObject1").transform.position, Is.EqualTo(position));
        foreach (MonoBehaviour component in components)
            if (component != null && new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(component.GetType().Name)) Object.Destroy(component.gameObject);
        yield return null;
    }
}
