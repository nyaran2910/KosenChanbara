using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ChambaraStageSceneTests
{
    private const string PrefabPath = "Assets/Art/Stages/ChambaraStage/ChambaraStage.prefab";
    private const string SmallPrefabPath = "Assets/Art/Stages/ChambaraStageSmall/ChambaraStageSmall.prefab";
    private const string StadiumPrefabPath = "Assets/Art/Stages/CircularStadium/CircularStadium.prefab";

    [Test]
    public void SuddenDeathUsesTheSuppliedSmallerRimAtTheNormalModelScale()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        GameObject normal = null, small = null;
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmallPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(serialized.FindProperty("chambaraSuddenDeathStage").objectReferenceValue, Is.EqualTo(prefab));
            normal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            small = Object.Instantiate(prefab);
            Assert.That(small.transform.GetChild(0).localScale, Is.EqualTo(normal.transform.GetChild(0).localScale));
            Renderer[] renderers = small.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.EqualTo(73));
            Assert.That(renderers.Single(r => r.name == "Platform_floor").bounds.max.y, Is.EqualTo(0f).Within(.001f));
            Bounds smallRim = renderers.Single(r => r.name == "Pale_red_rim").bounds;
            Bounds normalRim = normal.GetComponentsInChildren<Renderer>().Single(r => r.name == "Pale_red_rim").bounds;
            float ratio = serialized.FindProperty("suddenDeathRadiusRatio").floatValue;
            Assert.That(ratio, Is.EqualTo(.75f).Within(.001f));
            Assert.That(smallRim.extents.x, Is.EqualTo(normalRim.extents.x * ratio).Within(.001f));
            Assert.That(smallRim.extents.z, Is.EqualTo(normalRim.extents.z * ratio).Within(.001f));
            Assert.That(renderers.All(r => r.sharedMaterials.All(m => m != null && m.shader.name == "Universal Render Pipeline/Lit")), Is.True);
        }
        finally
        {
            if (normal != null) Object.DestroyImmediate(normal);
            if (small != null) Object.DestroyImmediate(small);
            Restore(previous);
        }
    }

    [Test]
    public void CircularStadiumKeepsTheCompleteStandsAndPlacesThePlayableGrassAtZero()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        GameObject instance = null;
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StadiumPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(serialized.FindProperty("circularStadiumStage").objectReferenceValue, Is.EqualTo(prefab));
            instance = Object.Instantiate(prefab);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.EqualTo(134), "Preserve the field, stands, roofs, goals and lights while merging material batches.");
            Bounds floor = renderers.Single(r => r.name == "Infield_concrete_foundation").bounds;
            Assert.That(floor.extents.x, Is.EqualTo(83f).Within(.001f));
            Assert.That(floor.extents.z, Is.EqualTo(83f).Within(.001f));
            Renderer[] grass = renderers.Where(r => r.name.StartsWith("Grass_mowing_stripe_")).ToArray();
            Assert.That(grass.Length, Is.EqualTo(14));
            foreach (Renderer stripe in grass) Assert.That(stripe.bounds.max.y, Is.EqualTo(0f).Within(.001f));
            Bounds pitch = renderers.Single(r => r.name == "Pitch_underlay").bounds;
            Assert.That(pitch.center.x, Is.EqualTo(0f).Within(.001f));
            Assert.That(pitch.center.z, Is.EqualTo(0f).Within(.001f));
            Assert.That(Mathf.Min(pitch.extents.x, pitch.extents.z), Is.GreaterThan(serialized.FindProperty("cricketBoundaryRadius").floatValue));
            Assert.That(renderers.All(r => r.sharedMaterials.All(m => m != null && m.shader.name == "Universal Render Pipeline/Lit")), Is.True);
            Assert.That(renderers.SelectMany(r => r.sharedMaterials).Distinct().Count(), Is.EqualTo(24));
            Assert.That(scene.GetRootGameObjects().Any(root => root.name == "CircularStadium" || root.name == "ChambaraStageSmall"), Is.False);
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            Restore(previous);
        }
    }

    [Test]
    public void ImportedStageFloorAndRimMatchTheSceneRingOutBounds()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        GameObject instance = null;
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(serialized.FindProperty("chambaraStage").objectReferenceValue, Is.EqualTo(prefab));
            Assert.That(scene.GetRootGameObjects().Any(root => root.name == "Chambara Stage"), Is.False,
                "Stage generation must save only the prefab reference, without preview geometry in the scene.");
            var arena = (SpriteRenderer)serialized.FindProperty("arena").objectReferenceValue;
            float radius = Mathf.Min(arena.bounds.extents.x, arena.bounds.extents.z);
            instance = Object.Instantiate(prefab);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.EqualTo(73), "Preserve the supplied floor, logo, beams, posts and bolts.");
            Assert.That(renderers.Any(renderer => renderer.name == "Studio_ground"), Is.False);
            Renderer floor = renderers.Single(renderer => renderer.name == "Platform_floor");
            Renderer rim = renderers.Single(renderer => renderer.name == "Pale_red_rim");
            Assert.That(floor.bounds.max.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(floor.bounds.center.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(floor.bounds.center.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rim.bounds.extents.x, Is.EqualTo(radius).Within(0.001f));
            Assert.That(rim.bounds.extents.z, Is.EqualTo(radius).Within(0.001f));
            Renderer logo = renderers.Single(renderer => renderer.name == "Center_logo_gray");
            Assert.That(logo.bounds.min.y, Is.GreaterThan(floor.bounds.max.y));
            Assert.That(logo.bounds.max.y, Is.LessThan(0.01f));
            foreach (Renderer renderer in renderers)
                Assert.That(renderer.sharedMaterials.All(material => material != null && material.shader.name == "Universal Render Pipeline/Lit"), Is.True, renderer.name);
            Assert.That(renderers.SelectMany(renderer => renderer.sharedMaterials).Distinct().Count(), Is.EqualTo(6));
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            Restore(previous);
        }
    }

    [TestCase("chambaraStage", PrefabPath, true)]
    [TestCase("chambaraStage", PrefabPath, false)]
    [TestCase("chambaraSuddenDeathStage", SmallPrefabPath, true)]
    [TestCase("chambaraSuddenDeathStage", SmallPrefabPath, false)]
    [TestCase("circularStadiumStage", StadiumPrefabPath, true)]
    [TestCase("circularStadiumStage", StadiumPrefabPath, false)]
    public void NewlyAddedStageReferenceIsRepairedWithoutOverwritingUnsavedEdits(string field, string path, bool missingStage)
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            MonoBehaviour master = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Single(component => component.GetType().Name == "MasterController" && component.gameObject.scene == scene);
            var serialized = new SerializedObject(master);
            Object existing = serialized.FindProperty("colosseumStage").objectReferenceValue;
            serialized.FindProperty(field).objectReferenceValue = missingStage ? null : existing;
            serialized.FindProperty("guardHeight").floatValue = 0.72f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            master.transform.position = new Vector3(4f, 0f, -5f);
            System.Type.GetType("ChambaraStageAssets, Assembly-CSharp-Editor", true)
                .GetMethod("OnPlayModeChanged", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { PlayModeStateChange.ExitingEditMode });
            serialized.Update();
            Assert.That(serialized.FindProperty(field).objectReferenceValue,
                Is.EqualTo(missingStage ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : existing));
            Assert.That(serialized.FindProperty("guardHeight").floatValue, Is.EqualTo(0.72f));
            Assert.That(master.transform.position, Is.EqualTo(new Vector3(4f, 0f, -5f)));
        }
        finally { Restore(previous); }
    }

    private static void Restore(SceneSetup[] setup)
    {
        if (setup.Any(scene => scene.isLoaded && scene.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
    }
}
