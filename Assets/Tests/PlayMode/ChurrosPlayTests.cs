using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ChurrosPlayTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour master, flow;
    private UnityEngine.Random.State randomState;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Flags).GetValue(instance);
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Flags).SetValue(instance, value);
    private static object Call(object instance, string name, params object[] values)
        => instance.GetType().GetMethod(name, Flags).Invoke(instance, values);

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub"
                || component.GetType().Name == "MotionController") component.enabled = false;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        randomState = UnityEngine.Random.state;
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        var components = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        master = components.Single(component => component.GetType().Name == "MasterController");
        flow = components.Single(component => component.GetType().Name == "GameFlowController");
        master.enabled = flow.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "PhoneControllerHub" || component.GetType().Name == "HardwareControllerHub")
                Object.Destroy(component.gameObject);
        yield return null;
        UnityEngine.Random.state = randomState;
    }

    [UnityTest]
    public IEnumerator ChurromanIsSelectableInEveryColorAndUsesTheSameCombatBody()
    {
        Call(flow, "ChooseMode", false);
        Button choice = Field<GameObject>(flow, "menuCanvas").GetComponentsInChildren<Button>()
            .First(button => button.GetComponentInChildren<Text>().text.Contains("チュロスメン"));
        object player = Field<object>(master, "first");
        Transform sword = Field<Transform>(player, "Sword");
        object input = sword.GetComponent("MotionController");
        CombatPose original = (CombatPose)Call(master, "PlayerCombatPose", player, new CombatInput(0f, 0f), false);
        choice.onClick.Invoke();
        Set(master, "churrosWeaponChance", 0f);
        Call(master, "ResetMatch");
        for (int color = 0; color < 6; color++)
        {
            Call(master, "SetColor", 0, color);
            Transform root = Field<Transform>(player, "Player");
            GameObject body = root.Cast<Transform>().Single(child => child.gameObject.activeSelf
                && child.name.StartsWith("ChambaraChurroman")).gameObject;
            Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>(), Is.Not.Empty);
            Assert.That(body.GetComponentsInChildren<Collider>(), Is.Empty, "The visual mesh must not introduce body hitboxes.");
            Assert.That(sword.Find("ChambaraChurrosSword"), Is.Not.Null, "Churroman always carries churros, even when the ordinary roll is zero.");
            Assert.That(sword.Find("ChambaraGripGlove"), Is.Null, "Churros weapons must never add grip gloves.");
            Assert.That(sword.GetComponent("MotionController"), Is.SameAs(input));
            CombatPose churroman = (CombatPose)Call(master, "PlayerCombatPose", player, new CombatInput(0f, 0f), false);
            Assert.That(Vector3.Distance(churroman.BodyCenter, original.BodyCenter), Is.LessThan(.01f), "Only the existing sub-centimetre idle breathing may move the common body center.");
            Assert.That(Vector3.Distance(churroman.Grip, original.Grip), Is.LessThan(.01f));
            Assert.That(Vector3.Angle(churroman.Blade, original.Blade), Is.LessThan(.06f));
        }
        Call(master, "SetCharacter", 0, 0);
        Assert.That(sword.Find("ChambaraChurrosSword"), Is.Null, "Changing back to an ordinary character must restore its rolled weapon.");
        Assert.That(sword.Find("ChambaraGripGlove"), Is.Null);
        Call(master, "SetCharacter", 0, 4);
        Call(master, "SetColor", 0, 0);
        Set(master, "churrosWeaponChance", 1f);
        Call(master, "ResetMatch");
        CaptureChurroman(Field<Transform>(player, "Player"), Field<Transform>(Field<object>(master, "second"), "Player"));
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChurrosWeaponRollChangesOnlyTheVisualsForPlayersAndEnemies()
    {
        UnityEngine.Random.InitState(417);
        object player = Field<object>(master, "first");
        Transform sword = Field<Transform>(player, "Sword");
        Set(master, "churrosWeaponChance", 0f);
        Call(master, "ResetMatch");
        Assert.That(sword.Cast<Transform>().Any(child => child.gameObject.activeSelf && child.name == "ChambaraChurrosSword"), Is.False);
        CombatPose normal = (CombatPose)Call(master, "PlayerCombatPose", player, new CombatInput(0f, 0f), false);
        Set(master, "churrosWeaponChance", 1f);
        Call(master, "ResetMatch");
        GameObject churros = sword.Cast<Transform>().Single(child => child.gameObject.activeSelf
            && child.name == "ChambaraChurrosSword").gameObject;
        Assert.That(churros.GetComponentsInChildren<Collider>(), Is.Empty);
        Assert.That(sword.Find("ChambaraGripGlove"), Is.Null, "Ordinary characters also lose gloves when their weapon roll is churros.");
        CombatPose alternate = (CombatPose)Call(master, "PlayerCombatPose", player, new CombatInput(0f, 0f), false);
        Assert.That(Vector3.Distance(alternate.Grip, normal.Grip), Is.LessThan(.0001f));
        Assert.That(Vector3.Angle(alternate.Blade, normal.Blade), Is.LessThan(.06f));
        Assert.That(Vector3.Distance(alternate.BodyCenter, normal.BodyCenter), Is.LessThan(.01f), "Changing weapon must preserve the common animated body anchor.");

        Call(flow, "ChooseMode", true);
        Call(master, "SpawnSingleRound");
        IList enemies = Field<IList>(master, "singleNpcs");
        Assert.That(enemies.Count, Is.EqualTo(10));
        foreach (object npc in enemies)
        {
            Transform npcSword = Field<Transform>(npc, "Sword");
            Assert.That(npcSword.Find("ChambaraChurrosSword"), Is.Not.Null);
            Assert.That(npcSword.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(npcSword.Find("ChambaraGripGlove"), Is.Null);
        }
        // Search deterministic random starts until the real uniform roster
        // selection spawns the new character; do not force a body prefab path.
        bool foundChurroman = enemies.Cast<object>().Any(npc => Field<GameObject>(npc, "Root").transform.Cast<Transform>()
            .Any(child => child.name.StartsWith("ChambaraChurroman")));
        for (int attempt = 0; !foundChurroman && attempt < 12; attempt++)
        {
            Call(master, "ResetMatch");
            Call(master, "SpawnSingleRound");
            foundChurroman = Field<IList>(master, "singleNpcs").Cast<object>().Any(npc => Field<GameObject>(npc, "Root")
                .transform.Cast<Transform>().Any(child => child.name.StartsWith("ChambaraChurroman")));
        }
        Assert.That(foundChurroman, Is.True, "Singleplayer must include Churroman in its actual random roster.");
        Set(master, "churrosWeaponChance", 0f);
        Call(master, "ResetMatch");
        Call(master, "SpawnSingleRound");
        foreach (object npc in Field<IList>(master, "singleNpcs"))
        {
            bool churroman = Field<GameObject>(npc, "Root").transform.Cast<Transform>()
                .Any(child => child.name.StartsWith("ChambaraChurroman"));
            Transform npcSword = Field<Transform>(npc, "Sword");
            Assert.That(npcSword.Find("ChambaraChurrosSword") != null, Is.EqualTo(churroman),
                "At zero ordinary chance, only Churroman enemies must still carry churros.");
            Assert.That(npcSword.Find("ChambaraGripGlove"), Is.Null);
        }
        yield return null;
    }

    private static void CaptureChurroman(Transform player, Transform other)
    {
        string folder = Environment.GetEnvironmentVariable("CHURROS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Camera camera = new GameObject("Churros visual check").AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 45f; camera.nearClipPlane = .1f;
        camera.transform.position = player.position + player.forward * 7f + player.right * 1.5f + Vector3.up * .9f;
        camera.transform.LookAt(player.position + Vector3.up * .7f);
        var target = new RenderTexture(960, 720, 24);
        var picture = new Texture2D(960, 720, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        Renderer[] renderers = player.GetComponentsInChildren<Renderer>(true);
        bool[] hidden = renderers.Select(renderer => renderer.forceRenderingOff).ToArray();
        Renderer[] otherRenderers = other.GetComponentsInChildren<Renderer>(true);
        bool[] otherHidden = otherRenderers.Select(renderer => renderer.forceRenderingOff).ToArray();
        try
        {
            foreach (Renderer renderer in renderers) renderer.forceRenderingOff = false;
            foreach (Renderer renderer in otherRenderers) renderer.forceRenderingOff = true;
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); picture.Apply();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "churroman-and-churros.png"), picture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = hidden[i];
            for (int i = 0; i < otherRenderers.Length; i++) otherRenderers[i].forceRenderingOff = otherHidden[i];
            RenderTexture.active = previous;
            target.Release(); Object.Destroy(target); Object.Destroy(picture); Object.Destroy(camera.gameObject);
        }
    }
}
