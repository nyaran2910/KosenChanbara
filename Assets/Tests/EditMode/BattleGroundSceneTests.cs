using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class BattleGroundSceneTests
{
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
