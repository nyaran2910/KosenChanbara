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

public sealed class BattleGroundPlayTests
{
    private Keyboard keyboard;
    private MonoBehaviour master;
    private MonoBehaviour hub;
    private GameObject firstPlayer;
    private GameObject secondPlayer;
    private GameObject firstSword;
    private GameObject secondSword;
    private InputSettings.BackgroundBehavior previousBackgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded += DisableLiveTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableLiveTransport;
        yield return null;
        master = FindComponent("MasterController");
        firstPlayer = Field<GameObject>(master, "PlayerObject1");
        secondPlayer = Field<GameObject>(master, "PlayerObject2");
        firstSword = Field<GameObject>(master, "Sword1");
        secondSword = Field<GameObject>(master, "Sword2");
        Assert.That(master.enabled, Is.True);
        Assert.That(Paused(), Is.True, "The scene must start paused for controller setup.");
        Assert.That(HubOverlay(), Is.True);
        Pause(false);
        yield return null;
    }

    private void DisableLiveTransport(Scene scene, LoadSceneMode mode)
    {
        // Exercise the real scene and keyboard fallback without opening signaling
        // sessions in an automated test. Awake has already created the overlay.
        hub = FindComponent("PhoneControllerHub");
        hub.enabled = false;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "MotionController")
                component.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableLiveTransport;
        InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
        if (hub != null)
            Object.Destroy(hub.gameObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator HitsStopThenPushAndBothPlayersCanWinAndRematch()
    {
        Vector3 initialPosition = master.transform.position;
        int hubId = hub.GetInstanceID();
        for (int attacker = 0; attacker < 2; attacker++)
        {
            float sign = attacker == 0 ? 1f : -1f;
            GameObject sword = attacker == 0 ? firstSword : secondSword;
            GameObject player = attacker == 0 ? firstPlayer : secondPlayer;
            Key attack = attacker == 0 ? Key.UpArrow : Key.W;
            Key returnToStance = attacker == 0 ? Key.DownArrow : Key.S;
            Renderer[] defenders = BodyRenderers(attacker == 0 ? secondPlayer : firstPlayer,
                attacker == 0 ? secondSword : firstSword);
            var block = new MaterialPropertyBlock();
            var arena = Field<SpriteRenderer>(master, "arena");
            int hitsToWin = Mathf.CeilToInt((Mathf.Min(arena.bounds.extents.x, arena.bounds.extents.z) - 1.5f) / 3f);

            for (int hit = 0; hit < hitsToWin; hit++)
            {
                Vector3 beforeHit = master.transform.position;
                yield return HoldUntil(() => Field<bool>(master, "reactionActive"), attack);
                Assert.That(master.transform.position, Is.EqualTo(beforeHit), "Push must wait for hit stop.");
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Quaternion impactRotation = sword.transform.localRotation;
                Vector3 impactPosition = sword.transform.localPosition;
                double start = Field<double>(master, "reactionStart");
                Assert.That(defenders.Length, Is.GreaterThan(1));
                foreach (Renderer defender in defenders)
                {
                    defender.GetPropertyBlock(block);
                    Assert.That(block.GetColor(Shader.PropertyToID("_BaseColor")), Is.EqualTo(Color.white), defender.name);
                }
                Assert.That(master.GetComponent<AudioSource>().isPlaying, Is.True);
                SetKeys(attack, attacker == 0 ? Key.RightShift : Key.LeftShift);
                yield return null;
                if (MatchTime() < start + 0.06)
                {
                    Assert.That(master.transform.position, Is.EqualTo(beforeHit));
                    Assert.That(Quaternion.Angle(sword.transform.localRotation, impactRotation), Is.LessThan(0.001f));
                    Assert.That(sword.transform.localPosition, Is.EqualTo(impactPosition));
                }
                SetKeys(attack);
                yield return new WaitForSecondsRealtime(0.3f);
                foreach (Renderer defender in defenders)
                {
                    defender.GetPropertyBlock(block);
                    Assert.That(block.isEmpty, Is.True, defender.name + " must restore its original properties.");
                }
                Assert.That(master.transform.position.z * sign, Is.GreaterThan(beforeHit.z * sign));
                if (hit < hitsToWin - 1)
                    Assert.That(master.transform.position.z, Is.EqualTo((hit + 1) * 3f * sign).Within(0.001f));

                // Continuing the same swing cannot cause another hit.
                Vector3 afterHit = master.transform.position;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(master.transform.position, Is.EqualTo(afterHit));
                if (hit < hitsToWin - 1)
                {
                    yield return HoldUntil(() => Vector3.Angle(sword.transform.forward, player.transform.forward) >= 89.9f,
                        returnToStance);
                    SetKeys();
                    yield return null;
                }
            }

            SetKeys();
            Assert.That(Winner(), Is.EqualTo(attacker == 0 ? MatchWinner.Player1 : MatchWinner.Player2));
            GameObject resultCanvas = master.transform.Find("Match result").gameObject;
            yield return new WaitForSecondsRealtime(0.65f);
            Assert.That(resultCanvas.activeSelf, Is.True);
            Assert.That(resultCanvas.GetComponentsInChildren<Text>().Any(text => text.text == (attacker == 0 ? "P1 WIN" : "P2 WIN")), Is.True);
            resultCanvas.GetComponentInChildren<Button>().onClick.Invoke();
            Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
            Assert.That(master.transform.position, Is.EqualTo(initialPosition));
            Assert.That(resultCanvas.activeSelf, Is.False);
            Assert.That(hub.GetInstanceID(), Is.EqualTo(hubId), "Rematch must preserve the transport instance.");
            Assert.That(Field<DuelMatch>(master, "match").IsResolving(MatchTime()), Is.False);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator GuardsAndClashesUseTheirOwnPushAndFlash()
    {
        Vector3 initialPosition = master.transform.position;

        // Upright defense is not perpendicular enough to stop a forward swing.
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow, Key.LeftShift);
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.HitStopSeconds));
        AssertBladeColor(secondSword, new Color(1f, 0.4f, 0.04f));
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position.z, Is.EqualTo(initialPosition.z + 1.5f).Within(0.001f));

        yield return ResetInPlace();
        // Turn P2's upright blade sideways, then hold guard while P1 swings.
        yield return HoldUntil(() => Mathf.Abs(Vector3.Dot(secondSword.transform.forward, Vector3.up)) < 0.01f, Key.D);
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow, Key.LeftShift);
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.GuardStopSeconds));
        AssertBladeColor(firstSword, Color.cyan);
        AssertBladeColor(secondSword, Color.cyan);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position.z, Is.EqualTo(initialPosition.z - 1.5f).Within(0.001f));

        yield return ResetInPlace();
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow, Key.W);
        Assert.That(Field<float>(master, "hitStopSeconds"), Is.EqualTo(DuelMatch.GuardStopSeconds));
        AssertBladeColor(firstSword, Color.white);
        AssertBladeColor(secondSword, Color.white);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(master.transform.position, Is.EqualTo(initialPosition));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in firstSword.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.True, "The flash must restore the original renderer properties.");
        }
    }

    [UnityTest]
    public IEnumerator GuardChangesTheWholePoseAndResetUsesHeldGuard()
    {
        object first = Field<object>(master, "first");
        object second = Field<object>(master, "second");
        foreach (object player in new[] { first, second })
        {
            player.GetType().GetField("Pitch").SetValue(player, -60f);
            player.GetType().GetField("Roll").SetValue(player, -45f);
        }
        yield return null;
        Vector3 chest = Field<Vector3>(master, "chestCenter");
        float radius = Field<float>(master, "gripRadius");
        float plane = Field<float>(master, "guardPlaneOffset");
        foreach (GameObject sword in new[] { firstSword, secondSword })
            AssertCurrentPose(sword, false);

        SetKeys(Key.RightShift, Key.LeftShift);
        yield return null;
        yield return null;
        Vector3 normalGuardDirection = firstSword.transform.localRotation * Vector3.forward;
        foreach (GameObject sword in new[] { firstSword, secondSword })
        {
            Vector3 direction = sword.transform.localRotation * Vector3.forward;
            Assert.That(direction.z, Is.EqualTo(0f).Within(0.00001f));
            AssertCurrentPose(sword, true);
        }
        Assert.That(Field<bool>(master, "reactionActive"), Is.False, "A guard toggle cannot cause an attack.");
        // Point directly forward: the last planar direction must survive.
        first.GetType().GetField("Pitch").SetValue(first, 0f);
        yield return null;
        Assert.That(Vector3.Distance(firstSword.transform.localRotation * Vector3.forward, normalGuardDirection), Is.LessThan(0.00001f));
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);

        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        foreach (GameObject sword in new[] { firstSword, secondSword })
        {
            Assert.That(Vector3.Distance(sword.transform.localRotation * Vector3.forward, Vector3.up), Is.LessThan(0.00001f));
            AssertCurrentPose(sword, true);
        }
        SetKeys();
        yield return null;
        yield return null;
        AssertCurrentPose(firstSword, false);
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
    }

    [UnityTest]
    public IEnumerator RCanResetAnOngoingReaction()
    {
        Vector3 initialPosition = master.transform.position;
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
        SetKeys(Key.R);
        yield return null;
        yield return null;
        SetKeys();
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Assert.That(master.transform.position, Is.EqualTo(initialPosition));
        Assert.That(Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward), Is.EqualTo(90f).Within(0.01f));
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
    }


    [UnityTest]
    public IEnumerator PauseStopsTheRoundAndPresentationButKeepsTheControllerObjects()
    {
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
        Pause(true);
        double pausedAt = MatchTime();
        Vector3 root = master.transform.position;
        Vector3 sword = firstSword.transform.position;
        Quaternion rotation = firstSword.transform.rotation;
        Transform chest = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "chest");
        Quaternion body = chest.rotation;
        Camera camera = firstPlayer.GetComponentInChildren<Camera>();
        Vector3 view = camera.transform.localPosition;
        MonoBehaviour fx = FindComponent("DuelEffects");
        int bursts = (int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx);
        Assert.That(bursts, Is.GreaterThan(0));
        Assert.That(HubOverlay(), Is.True);
        Assert.That(hub.gameObject.activeInHierarchy, Is.True);
        Assert.That(firstSword.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name == "MotionController"), Is.True);
        Assert.That(master.transform.Find("Pause UI/Pause hint").gameObject.activeSelf, Is.False);
        SetKeys(Key.UpArrow, Key.RightShift);
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(MatchTime(), Is.EqualTo(pausedAt));
        Assert.That(master.transform.position, Is.EqualTo(root));
        Assert.That(firstSword.transform.position, Is.EqualTo(sword));
        Assert.That(firstSword.transform.rotation, Is.EqualTo(rotation));
        Assert.That(chest.rotation, Is.EqualTo(body));
        Assert.That(camera.transform.localPosition, Is.EqualTo(view));
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(bursts));
        Assert.That(Field<DuelMatch>(master, "match").IsStunned(1, MatchTime()), Is.True);
        Pause(false);
        Assert.That(HubOverlay(), Is.False);
        Assert.That(master.transform.Find("Pause UI/Pause hint").gameObject.activeSelf, Is.True);
        // The unfinished stop and stun must survive the wall-clock pause.
        Assert.That(firstSword.transform.rotation, Is.EqualTo(rotation));
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(master.transform.position.z, Is.GreaterThan(root.z));
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(0));
    }

    [UnityTest]
    public IEnumerator ResumeStartsANewAttackBaselineAndPausedResetStaysPaused()
    {
        yield return null;
        Pause(true);
        object first = Field<object>(master, "first");
        first.GetType().GetField("Pitch").SetValue(first, -30f);
        SetKeys(Key.UpArrow, Key.RightShift);
        yield return null;
        Pause(false);
        yield return null;
        yield return null;
        Assert.That(Field<bool>(master, "reactionActive"), Is.False, "Motion made during pause must not trigger an attack on resume.");
        AssertCurrentPose(firstSword, true);
        Pause(true);
        SetKeys(Key.R);
        yield return null;
        yield return null;
        Assert.That(Paused(), Is.True);
        Assert.That(HubOverlay(), Is.True);
        Assert.That(Field<bool>(master, "reactionActive"), Is.False);
        Assert.That(Winner(), Is.EqualTo(MatchWinner.None));
        SetKeys();
        yield return null;
        SetKeys(Key.Escape);
        yield return null;
        yield return null;
        Assert.That(Paused(), Is.False);
        SetKeys();
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
    }

    [UnityTest]
    public IEnumerator CharacterBonesMoveWithoutMovingCombatCentersAndEffectsReset()
    {
        Vector3 firstCenter = firstPlayer.transform.position, secondCenter = secondPlayer.transform.position;
        Transform chest = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "chest");
        Transform knee = firstPlayer.GetComponentsInChildren<Transform>().Single(t => t.name == "foot_l2");
        Quaternion body = chest.rotation;
        Vector3 foot = knee.position;
        SetKeys(Key.UpArrow);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.That(Quaternion.Angle(chest.rotation, body), Is.GreaterThan(0.1f));
        Assert.That(Vector3.Distance(knee.position, foot), Is.GreaterThan(0.001f));
        Assert.That(firstPlayer.transform.position, Is.EqualTo(firstCenter));
        Assert.That(secondPlayer.transform.position, Is.EqualTo(secondCenter));
        MonoBehaviour fx = FindComponent("DuelEffects");
        Assert.That((int)fx.GetType().GetProperty("TrailSampleCount").GetValue(fx), Is.InRange(2, 24));
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.InRange(1, 12));
        Camera camera = firstPlayer.GetComponentInChildren<Camera>();
        Assert.That(camera.fieldOfView, Is.InRange(60, 62.01f));
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.That(camera.fieldOfView, Is.EqualTo(60f).Within(0.001f));
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        Assert.That((int)fx.GetType().GetProperty("TrailSampleCount").GetValue(fx), Is.EqualTo(0));
        Assert.That((int)fx.GetType().GetProperty("ActiveBurstCount").GetValue(fx), Is.EqualTo(0));
        foreach (SkinnedMeshRenderer skin in firstPlayer.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh, true);
            float floor = mesh.vertices.Min(v => skin.transform.TransformPoint(v).y);
            Assert.That(floor, Is.GreaterThanOrEqualTo(-0.001f), skin.name);
            Object.Destroy(mesh);
        }
    }

    [UnityTest]
    public IEnumerator OwnCameraFadesOnlyItsBodyAndRestoresMaterialsAndFlash()
    {
        yield return HoldUntil(() => Field<bool>(master, "reactionActive"), Key.UpArrow);
        Pause(true); // Keep the hit flash stable while rendering both viewports.
        Renderer[] firstBody = BodyRenderers(firstPlayer, firstSword), secondBody = BodyRenderers(secondPlayer, secondSword);
        Material[][] originals = firstBody.Concat(secondBody).Select(r => r.sharedMaterials).ToArray();
        bool seen = false;
        Camera firstCamera = firstPlayer.GetComponentInChildren<Camera>();
        Camera secondCamera = secondPlayer.GetComponentInChildren<Camera>();
        Action<ScriptableRenderContext, Camera> probe = (context, camera) =>
        {
            if (camera != firstCamera && camera != secondCamera) return;
            seen = true;
            foreach (Renderer renderer in camera == firstCamera ? firstBody : secondBody)
                foreach (Material material in renderer.sharedMaterials)
                    Assert.That(material.color.a, Is.EqualTo(0.3f).Within(0.001f));
            foreach (Renderer renderer in camera == firstCamera ? secondBody : firstBody)
                foreach (Material material in renderer.sharedMaterials)
                    Assert.That(material.color.a, Is.EqualTo(1f));
            foreach (Renderer renderer in firstSword.GetComponentsInChildren<Renderer>())
                Assert.That(renderer.sharedMaterial.color.a, Is.EqualTo(1f));
        };
        RenderPipelineManager.beginCameraRendering += probe;
        try
        {
            Capture(firstCamera, "hit-P1"); Capture(secondCamera, "hit-P2");
            Assert.That(seen, Is.True);
        }
        finally { RenderPipelineManager.beginCameraRendering -= probe; }
        Renderer[] both = firstBody.Concat(secondBody).ToArray();
        for (int i = 0; i < both.Length; i++) Assert.That(both[i].sharedMaterials, Is.EqualTo(originals[i]));
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in secondBody)
        {
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(Shader.PropertyToID("_BaseColor")), Is.EqualTo(Color.white));
        }
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        Pause(false); SetKeys(); yield return null;
        Capture(firstCamera, "idle-P1"); Capture(secondCamera, "idle-P2");
        SetKeys(Key.RightShift, Key.LeftShift); yield return null; yield return null;
        Capture(firstCamera, "guard-P1"); Capture(secondCamera, "guard-P2");
        // Third camera shows grounded skin geometry without own-view fading.
        Camera front = new GameObject("Rig preview").AddComponent<Camera>();
        front.enabled = false; front.fieldOfView = 45; front.nearClipPlane = 0.1f;
        front.transform.position = firstPlayer.transform.TransformPoint(new Vector3(0, 0.7f, 6));
        front.transform.LookAt(firstPlayer.transform.TransformPoint(new Vector3(0, 0, 0)));
        secondPlayer.SetActive(false);
        Capture(front, "guard-front");
        secondPlayer.SetActive(true); Object.Destroy(front.gameObject);
    }

    private void Capture(Camera camera, string name)
    {
        Rect previous = camera.rect;
        RenderTexture previousActive = RenderTexture.active;
        var texture = new RenderTexture(960, 720, 24);
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            camera.rect = new Rect(0, 0, 1, 1); texture.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            string output = Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
        }
        finally
        {
            camera.rect = previous; RenderTexture.active = previousActive;
            texture.Release(); Object.Destroy(texture); Object.Destroy(image);
        }
    }

    private bool Paused() => (bool)master.GetType().GetProperty("IsPaused").GetValue(master);
    private double MatchTime() => (double)master.GetType().GetProperty("MatchTime").GetValue(master);
    private bool HubOverlay() => (bool)hub.GetType().GetProperty("OverlayVisible").GetValue(hub);
    private void Pause(bool value) => master.GetType().GetMethod("SetPaused").Invoke(master, new object[] { value });
    private void AssertCurrentPose(GameObject sword, bool guard)
    {
        int index = sword == firstSword ? 0 : 1;
        object presentation = Field<object>(master, "presentation");
        Vector3 chest = Field<Vector3>(master, "chestCenter") + (Vector3)presentation.GetType().GetMethod("ChestOffset").Invoke(presentation, new object[] { index });
        var settings = new SwordPoseSettings(chest, Field<float>(master, "gripRadius"), Field<float>(master, "guardRadius"), Field<float>(master, "guardPlaneOffset"), Field<float>(master, "guardHeight"));
        Vector3 last = sword.transform.localRotation * Vector3.forward;
        // Direction already reflects guard projection; the visible pose must use that same direction.
        SwordPose expected = SwordPoseMath.Calculate(sword.transform.localRotation, guard, settings, ref last);
        Assert.That(Vector3.Distance(sword.transform.localPosition, expected.Position), Is.LessThan(0.0001f));
    }

    private IEnumerator HoldUntil(Func<bool> condition, params Key[] keys)
    {
        SetKeys(keys);
        double deadline = Time.realtimeSinceStartupAsDouble + 4.0;
        do
        {
            yield return null;
            if (Time.realtimeSinceStartupAsDouble >= deadline)
            {
                object player = Field<object>(master, "first");
                Assert.Fail("Keyboard action timed out. Current keyboard=" + Keyboard.current?.deviceId
                    + ", synthetic keyboard=" + keyboard.deviceId + ", up=" + keyboard.upArrowKey.isPressed
                    + ", pitch=" + player.GetType().GetField("Pitch").GetValue(player)
                    + ", sword angle=" + Vector3.Angle(firstSword.transform.forward, firstPlayer.transform.forward)
                    + ", dt=" + Time.unscaledDeltaTime);
            }
        } while (!condition());
    }

    private void SetKeys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private IEnumerator ResetInPlace()
    {
        SetKeys();
        // Let the queued release reach InputSystem before resetting; otherwise
        // LateUpdate can advance one blade with the previous held attack key.
        yield return null;
        master.GetType().GetMethod("ResetMatch").Invoke(master, null);
        yield return null;
    }
    private static void AssertBladeColor(GameObject sword, Color expected)
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in sword.GetComponentsInChildren<Renderer>())
        {
            renderer.GetPropertyBlock(block);
            Color actual = block.GetColor(Shader.PropertyToID("_BaseColor"));
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f), renderer.name);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f), renderer.name);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f), renderer.name);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f), renderer.name);
        }
    }
    private static Renderer[] BodyRenderers(GameObject player, GameObject sword)
        => player.GetComponentsInChildren<Renderer>()
            .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(sword.transform)).ToArray();
    private MatchWinner Winner() => (MatchWinner)master.GetType().GetProperty("Winner").GetValue(master);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static MonoBehaviour FindComponent(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
        .Single(component => component.GetType().Name == name);
}
