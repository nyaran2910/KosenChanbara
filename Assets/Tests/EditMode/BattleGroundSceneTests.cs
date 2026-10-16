using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class BattleGroundSceneTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void PlayPreparationRepairsAMissingStadiumWithoutOverwritingSceneEdits(bool missingStage)
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            Object originalStage = serialized.FindProperty("colosseumStage").objectReferenceValue;
            // Reproduce the open scene's stale reference while keeping other unsaved settings.
            serialized.FindProperty("cricketStadiumStage").objectReferenceValue = missingStage ? null : originalStage;
            serialized.FindProperty("cricketBoundaryRadius").floatValue = 23f;
            serialized.FindProperty("guardHeight").floatValue = 0.72f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            master.transform.position = new Vector3(4f, 0f, -5f);
            System.Type.GetType("CricketStadiumAssets, Assembly-CSharp-Editor", true)
                .GetMethod("OnPlayModeChanged", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
            serialized.Update();
            Object expectedStage = missingStage
                ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Stages/CricketStadium/CricketStadium.prefab")
                : originalStage;
            Assert.That(serialized.FindProperty("cricketStadiumStage").objectReferenceValue, Is.EqualTo(expectedStage));
            Assert.That(serialized.FindProperty("cricketBoundaryRadius").floatValue,
                missingStage ? Is.GreaterThan(30f).And.LessThan(31f) : Is.EqualTo(23f));
            Assert.That(serialized.FindProperty("guardHeight").floatValue, Is.EqualTo(0.72f));
            Assert.That(serialized.FindProperty("colosseumStage").objectReferenceValue, Is.EqualTo(originalStage));
            Assert.That(master.transform.position, Is.EqualTo(new Vector3(4f, 0f, -5f)));
        }
        finally
        {
            if (previousSetup.Any(setup => setup.isLoaded && setup.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }

    [Test]
    public void BattleGroundHasAllCombatReferencesAndAHorizontalArena()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            foreach (string field in new[] { "Sword1", "Sword2", "PlayerObject1", "PlayerObject2", "arena" })
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            foreach (string field in new[] { "characterBodies", "characterBlades" })
            {
                SerializedProperty choices = serialized.FindProperty(field);
                Assert.That(choices.arraySize, Is.EqualTo(field == "characterBodies" ? 30 : 6), field);
                for (int i = 0; i < choices.arraySize; i++)
                    Assert.That(choices.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, field + " " + i);
            }
            Assert.That(serialized.FindProperty("characterNames").arraySize, Is.EqualTo(5));
            Assert.That(serialized.FindProperty("characterChestCenters").arraySize, Is.EqualTo(5));
            Assert.That(serialized.FindProperty("colosseumStage").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("cricketStadiumStage").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("cricketBoundaryRadius").floatValue, Is.GreaterThan(15f));
            var arena = (SpriteRenderer)serialized.FindProperty("arena").objectReferenceValue;
            Assert.That(Mathf.Min(arena.bounds.extents.x, arena.bounds.extents.z), Is.GreaterThan(3f));
            Assert.That(Mathf.Abs(Vector3.Dot(arena.transform.forward, Vector3.up)), Is.GreaterThan(0.999f));
            foreach (string field in new[] { "PlayerObject1", "PlayerObject2" })
            {
                var player = (GameObject)serialized.FindProperty(field).objectReferenceValue;
                Assert.That(player.transform.IsChildOf(master.transform), Is.True, field);
            }
        }
        finally
        {
            if (previousSetup.Any(setup => setup.isLoaded && setup.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }

    [Test]
    public void CricketStadiumHasGroundedGrassAndAllFiveUrpTextures()
    {
        const string root = "Assets/Art/Stages/CricketStadium/";
        var stage = AssetDatabase.LoadAssetAtPath<GameObject>(root + "CricketStadium.prefab");
        Assert.That(stage, Is.Not.Null);
        Renderer[] renderers = stage.GetComponentsInChildren<Renderer>();
        Assert.That(renderers.SelectMany(r => r.sharedMaterials).All(m => m != null && m.shader.name == "Universal Render Pipeline/Lit"), Is.True);
        Renderer grass = renderers.Single(r => r.name == "Ground .009");
        Assert.That(grass.bounds.max.y, Is.EqualTo(0f).Within(0.001f));
        Assert.That(grass.bounds.center.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(grass.bounds.center.z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(Mathf.Min(grass.bounds.extents.x, grass.bounds.extents.z), Is.GreaterThan(35f));
        string[] materials = { "Boundary_texture", "Ground_texture", "Pitch", "Rope_texture", "Scoreboard_screen" };
        string[] textures = { "Boundary", "Ground", "Pitch", "Rope_02", "Scoreboard" };
        for (int i = 0; i < materials.Length; i++)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(root + "Materials/" + materials[i] + ".mat");
            Assert.That(material, Is.Not.Null, materials[i]);
            Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(root + textures[i] + ".png")), materials[i]);
            Assert.That(renderers.Any(r => r.sharedMaterials.Contains(material)), Is.True, materials[i] + " is used by the model");
        }
    }

    private static Vector3[] WorldPoints(Renderer renderer)
    {
        Mesh mesh;
        bool baked = renderer is SkinnedMeshRenderer;
        if (baked)
        {
            mesh = new Mesh();
            // FBX armatures carry a scale conversion; include it when converting to world space.
            ((SkinnedMeshRenderer)renderer).BakeMesh(mesh, true);
        }
        else mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] points = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray();
        if (baked) Object.DestroyImmediate(mesh);
        return points;
    }

    [Test]
    public void ChurromanAndChurrosUseTheCommonCombatAnchorsAndNoMeshColliders()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            Assert.That(serialized.FindProperty("characterNames").GetArrayElementAtIndex(4).stringValue, Is.EqualTo("チュロスメン"));
            var centers = serialized.FindProperty("characterChestCenters");
            Assert.That(centers.GetArrayElementAtIndex(4).vector3Value, Is.EqualTo(centers.GetArrayElementAtIndex(0).vector3Value));
            var bodies = serialized.FindProperty("characterBodies");
            for (int color = 0; color < 6; color++)
            {
                var body = (GameObject)bodies.GetArrayElementAtIndex(24 + color).objectReferenceValue;
                Assert.That(body, Is.Not.Null);
                Assert.That(body.GetComponentsInChildren<Collider>(), Is.Empty);
                foreach (string bone in new[] { "chest", "head", "hip", "foot_l1", "foot_l2", "ankle_l", "foot_r1", "foot_r2", "ankle_r" })
                    Assert.That(body.GetComponentsInChildren<Transform>().Any(child => child.name == bone), Is.True, bone);
                Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>(), Is.Not.Empty);
            }
            var blade = (GameObject)serialized.FindProperty("churrosBlade").objectReferenceValue;
            Assert.That(blade, Is.Not.Null);
            Assert.That(blade.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(serialized.FindProperty("churrosWeaponChance").floatValue, Is.EqualTo(.05f));
            Assert.That(CombatContact.BladeLength, Is.EqualTo(2.1f));
            var instance = UnityEngine.Object.Instantiate(blade);
            try
            {
                float tip = instance.GetComponentsInChildren<Renderer>().SelectMany(WorldPoints)
                    .Select(point => instance.transform.InverseTransformPoint(point).z).Max();
                Assert.That(tip, Is.EqualTo(CombatContact.BladeLength).Within(.03f), "The visible churros tip uses the same grip-to-tip length.");
                Renderer crust = instance.GetComponentsInChildren<Renderer>().First(renderer => renderer.name.Contains("Churro_body"));
                Vector3[] points = WorldPoints(crust).Select(point => instance.transform.InverseTransformPoint(point)).ToArray();
                float bottom = points.Min(point => point.z), top = points.Max(point => point.z);
                Assert.That(bottom, Is.LessThan(-.125f), "The authored grip must leave room below the hand anchor.");
                foreach (Vector3[] end in new[] { points.Where(point => point.z < bottom + .08f).ToArray(),
                    points.Where(point => point.z > top - .08f).ToArray() })
                    Assert.That(new Vector2(end.Average(point => point.x), end.Average(point => point.y)).magnitude,
                        Is.LessThan(.05f), "The authored pose must not leave the weapon leaning away from the logical blade axis.");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        finally
        {
            if (previousSetup.Any(setup => setup.isLoaded && setup.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    [Test]
    public void AllFourCharactersHaveUsableSkeletonsAndSixMatchingPalettes()
    {
        string[] characters = { "Original", "Bald", "Afro", "Robot" };
        string[] colors = { "Red", "Blue", "Green", "Yellow", "Purple", "Black" };
        foreach (string character in characters)
        {
            Color[] palette = new Color[colors.Length];
            for (int index = 0; index < colors.Length; index++)
            {
                var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Chambara/Roster/Prefabs/Chambara" + character + colors[index] + "Body.prefab");
                var blade = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Chambara/Roster/Prefabs/Chambara" + colors[index] + "Sword.prefab");
                Assert.That(body, Is.Not.Null);
                Assert.That(blade, Is.Not.Null);
                SkinnedMeshRenderer[] skins = body.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(skins.Length, Is.GreaterThan(20));
                foreach (SkinnedMeshRenderer skin in skins)
                {
                    Assert.That(skin.name.Contains("Glove") || skin.name.Contains("Cuff"), Is.False);
                    Assert.That(skin.bones.All(bone => bone != null), Is.True, skin.name);
                    Assert.That(skin.sharedMesh.bindposes.Length, Is.EqualTo(skin.bones.Length));
                    Assert.That(skin.sharedMesh.boneWeights.Any(weight => weight.weight0 > 0f), Is.True);
                    Assert.That(skin.sharedMaterials.All(material => material.shader.name == "Universal Render Pipeline/Lit"), Is.True);
                }
                foreach (string bone in new[] { "chest", "hip", "ankle_l", "ankle_r" })
                    Assert.That(body.GetComponentsInChildren<Transform>().Any(t => t.name == bone), Is.True, character + " " + bone);
                Material armor = skins.SelectMany(skin => skin.sharedMaterials).First(material => material.name.StartsWith("Armor_") && !material.name.StartsWith("Armor_seams_"));
                Material sword = blade.GetComponentsInChildren<Renderer>().SelectMany(renderer => renderer.sharedMaterials).First(material => material.name.StartsWith("Padded_sword_"));
                palette[index] = armor.color;
                Assert.That(sword.color, Is.EqualTo(armor.color), character + " " + colors[index]);
            }
            Assert.That(palette.Distinct().Count(), Is.EqualTo(6), character);
        }
        var stage = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Stages/Colosseum/Colosseum.prefab");
        Assert.That(stage, Is.Not.Null);
        Renderer terrain = stage.GetComponentInChildren<Renderer>();
        Assert.That(terrain.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
        Assert.That(terrain.bounds.size.x, Is.GreaterThan(40f));
        Assert.That(terrain.bounds.size.z, Is.GreaterThan(35f));
    }

    [Test]
    public void BothCharactersHaveGroundedBodiesAndForwardGripOriginSwords()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController");
            var serialized = new SerializedObject(master);
            foreach (int index in new[] { 1, 2 })
            {
                var player = (GameObject)serialized.FindProperty("PlayerObject" + index).objectReferenceValue;
                var sword = (GameObject)serialized.FindProperty("Sword" + index).objectReferenceValue;
                Assert.That(player.GetComponent<Renderer>().enabled, Is.False);
                Renderer[] body = player.GetComponentsInChildren<Renderer>()
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(sword.transform)).ToArray();
                Assert.That(body.Length, Is.GreaterThan(1));
                Assert.That(body.All(renderer => renderer is SkinnedMeshRenderer), Is.True, "Every body part must follow the rest skeleton.");
                foreach (SkinnedMeshRenderer skin in body)
                {
                    Assert.That(skin.bones.Length, Is.GreaterThan(0), skin.name);
                    Assert.That(skin.bones.All(bone => bone != null), Is.True, skin.name);
                    Assert.That(skin.sharedMesh.boneWeights.Any(weight => weight.weight0 > 0f), Is.True, skin.name);
                    Assert.That(skin.sharedMesh.bindposes.Length, Is.EqualTo(skin.bones.Length), skin.name);
                }
                foreach (string bone in new[] { "chest", "hip", "foot_l1", "foot_l2", "ankle_l", "foot_r1", "foot_r2", "ankle_r" })
                    Assert.That(player.GetComponentsInChildren<Transform>().Any(t => t.name == bone), Is.True, bone);
                Bounds bounds = new Bounds(WorldPoints(body[0]).First(), Vector3.zero);
                foreach (Renderer renderer in body)
                {
                    foreach (Vector3 point in WorldPoints(renderer)) bounds.Encapsulate(point);
                    Assert.That(renderer.name.Contains("Glove"), Is.False);
                    Assert.That(renderer.name.Contains("Sword"), Is.False);
                }
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(bounds.size.y, Is.EqualTo(2f).Within(0.001f));
                Renderer nose = body.Single(renderer => renderer.name.EndsWith("_Nose"));
                Assert.That(player.transform.InverseTransformPoint(nose.bounds.center).z, Is.GreaterThan(0f), "The face must point at the opponent.");
                foreach (Renderer renderer in body.Concat(sword.GetComponentsInChildren<Renderer>()))
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null);
                        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
                    }
                Vector3[] swordPoints = sword.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                    filter.sharedMesh.vertices.Select(vertex => sword.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)))).ToArray();
                Assert.That(swordPoints.Max(point => point.z), Is.EqualTo(2.1f).Within(0.001f));
                Assert.That(swordPoints.Min(point => point.z), Is.LessThan(0f), "The grip origin must have a pommel behind it.");
                Renderer grip = sword.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name.EndsWith("_Sword_grip"));
                Assert.That(sword.transform.InverseTransformPoint(grip.bounds.center).magnitude, Is.LessThan(0.001f));
                Renderer chest = body.Single(renderer => renderer.name.EndsWith("_Chest_guard"));
                Vector3 center = serialized.FindProperty("chestCenter").vector3Value;
                Assert.That(center.y, Is.EqualTo(player.transform.InverseTransformPoint(chest.bounds.center).y).Within(0.001f));
                float front = body.Where(renderer => renderer.name.Contains("Chest_")).SelectMany(renderer =>
                    WorldPoints(renderer).Select(vertex => player.transform.InverseTransformPoint(vertex).z)).Max();
                Assert.That(center.z + serialized.FindProperty("guardPlaneOffset").floatValue, Is.GreaterThan(front + 0.08f));
            }
        }
        finally
        {
            if (previousSetup.Any(setup => setup.isLoaded && setup.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
    }
}
