using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class SessionTimerPlayTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    MonoBehaviour master, flow;
    Keyboard keyboard;
    Keyboard[] physical;
    InputSettings.BackgroundBehavior oldBackground;
    InputSettings.EditorInputBehaviorInPlayMode oldEditor;
    static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Flags).Invoke(value, args);
    static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    SessionPlayTimer Timer => Field<SessionPlayTimer>(flow, "sessionTimer");
    static void Disable(Scene scene, LoadSceneMode mode)
    {
        foreach (var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(c.GetType().Name)) c.enabled = false;
    }
    [UnitySetUp] public IEnumerator Setup()
    {
        oldBackground = InputSystem.settings.backgroundBehavior;
        oldEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        physical = InputSystem.devices.OfType<Keyboard>().Where(device => device.enabled).ToArray();
        foreach (var device in physical) InputSystem.DisableDevice(device);
        keyboard = InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded += Disable; yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= Disable; yield return null;
        master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().Name == "MasterController");
        flow = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c => c.GetType().Name == "GameFlowController");
        master.enabled = flow.enabled = false;
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        SceneManager.sceneLoaded -= Disable;
        InputSystem.RemoveDevice(keyboard);
        foreach (var device in physical) InputSystem.EnableDevice(device);
        if (physical.Length > 0) physical[0].MakeCurrent();
        InputSystem.settings.backgroundBehavior = oldBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
        foreach (var c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(c.GetType().Name)) Object.Destroy(c.gameObject);
        yield return null;
    }
    IEnumerator TapT()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T)); yield return null;
        Call(flow, "Update");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        Call(flow, "Update");
    }
    void Start(string mode)
    {
        if (mode == "single") Call(flow, "ChooseMode", true);
        else if (mode == "duel") Call(flow, "ChooseMode", false);
        else Call(flow, mode == "tutorial" ? "ChooseTutorialMode" : "ChooseFreeMode");
        Call(flow, "Ready", 0);
        if (!Property<bool>(master, "IsSingleMode")) Call(flow, "Ready", 1);
        Assert.That(Property<object>(flow, "CurrentScreen").ToString(), Is.EqualTo("Playing"));
    }
    [UnityTest] public IEnumerator TStartsImmediatelyAndTogglesOffAcrossModes()
    {
        Start("single"); Call(flow, "TickSessionTimer", 1000d);
        Assert.That(Timer.Started, Is.False);
        Call(flow, "ReturnToTitle");
        foreach (string mode in new[] { "single", "duel", "free", "tutorial" })
        {
            yield return TapT();
            Assert.That(Timer.Armed, Is.True); Assert.That(Timer.Started, Is.True);
            double deadline = Timer.EndsAt;
            Assert.That(Timer.RemainingSeconds(Time.realtimeSinceStartupAsDouble), Is.EqualTo(600));
            Start(mode);
            Assert.That(Timer.EndsAt, Is.EqualTo(deadline));
            yield return TapT();
            Assert.That(Timer.Armed, Is.False);
            Assert.That(Field<GameObject>(flow, "timerCanvas").activeSelf, Is.False);
            Call(flow, "TickSessionTimer", deadline + 1);
            Assert.That(Timer.Started, Is.False);
            Call(flow, "ReturnToTitle");
        }
    }
    [UnityTest] public IEnumerator ExpiryIsANonBlockingNoticeAndRetriesKeepTheDeadline()
    {
        yield return TapT(); Start("tutorial");
        Assert.That(Timer.Started, Is.True);
        double deadline = Timer.EndsAt;
        Call(flow, "ReturnToTitle"); Start("single");
        Call(flow, "TickSessionTimer", deadline - .6d);
        Assert.That(Field<Text>(flow, "timerRemaining").text, Is.EqualTo("00:01"));
        Call(flow, "TogglePause"); Call(flow, "TickSessionTimer", deadline - .4d); Call(flow, "TogglePause");
        float scale = Time.timeScale;
        Call(flow, "TickSessionTimer", deadline + .2d);
        Assert.That(Timer.Expired, Is.True);
        Assert.That(Property<bool>(master, "IsPaused"), Is.False);
        Assert.That(Property<bool>(flow, "IsPlaying"), Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(scale));
        Assert.That(Field<RectTransform>(flow, "timerNotice").gameObject.activeSelf, Is.True);
        var canvas = Field<GameObject>(flow, "timerCanvas");
        Assert.That(canvas.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        Assert.That(canvas.GetComponentsInChildren<Button>(true), Is.Empty);
        Assert.That(Field<Text>(flow, "timerRemaining").text, Is.EqualTo("00:00"));
        Call(flow, "TickSessionTimer", deadline + .5d);
        yield return null;
        UnityEngine.Canvas.ForceUpdateCanvases();
        Component clock = Field<Component>(flow, "timerNoticeClock");
        Mesh clockMesh = clock.GetComponent<CanvasRenderer>().GetMesh();
        Assert.That(clockMesh.vertexCount, Is.GreaterThan(100), "The clock icon must generate visible UI geometry.");
        string folder = Environment.GetEnvironmentVariable("SESSION_TIMER_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            CaptureTimer(Path.Combine(folder, "time-is-up.png"));
        }
        Call(master, "ResetMatch"); Call(flow, "TickSessionTimer", deadline + 1d);
        Assert.That(Timer.EndsAt, Is.EqualTo(deadline));
        Call(flow, "TickSessionTimer", deadline + 6d);
        Assert.That(Field<RectTransform>(flow, "timerNotice").gameObject.activeSelf, Is.False);
        yield return TapT();
        Assert.That(Timer.Armed, Is.False);
        yield return TapT();
        Assert.That(Timer.Expired, Is.False); Assert.That(Timer.RemainingSeconds(Time.realtimeSinceStartupAsDouble), Is.EqualTo(600));
    }
    private void CaptureTimer(string path)
    {
        Camera camera = Field<GameObject>(master, "PlayerObject1").GetComponentInChildren<Camera>();
        UnityEngine.Canvas[] canvases = Object.FindObjectsByType<UnityEngine.Canvas>(FindObjectsSortMode.None)
            .Where(canvas => canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        RenderMode[] modes = canvases.Select(canvas => canvas.renderMode).ToArray();
        Camera[] cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
        float[] planes = canvases.Select(canvas => canvas.planeDistance).ToArray();
        var target = new RenderTexture(1280, 720, 24);
        var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previousTarget = camera.targetTexture, active = RenderTexture.active;
        float aspect = camera.aspect;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = 16f / 9f;
            foreach (var canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + .1f;
            }
            UnityEngine.Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); picture.Apply();
            File.WriteAllBytes(path, picture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < canvases.Length; i++)
            { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = planes[i]; }
            camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = active;
            target.Release(); Object.Destroy(target); Object.Destroy(picture);
        }
    }

}
