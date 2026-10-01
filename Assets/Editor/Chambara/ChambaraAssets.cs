using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SchoolFestival.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Rebuilds only the imported character assets and their BattleGround instances.
public static class ChambaraAssets
{
    private const string Root = "Assets/Art/Chambara";
    private const string Models = Root + "/Models";
    [Serializable] private sealed class ExportData
    {
        public MaterialData[] materials;
        public CharacterData[] characters;
    }
    [Serializable] private sealed class MaterialData
    {
        public string name;
        public float[] color;
        public float roughness;
    }
    [Serializable] private sealed class CharacterData
    {
        public string name;
        public float[] chestCenter;
        public float guardPlaneOffset;
    }

    [MenuItem("Tools/Chambara/Rebuild character assets")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var data = JsonUtility.FromJson<ExportData>(File.ReadAllText(Models + "/ChambaraExport.json"));
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory("Assets/Resources");
        AssetDatabase.Refresh();
        // Include the transparent URP variant in player builds as well as the editor.
        const string fadePath = "Assets/Resources/OwnBodyFade.mat";
        var fade = AssetDatabase.LoadAssetAtPath<Material>(fadePath);
        if (fade == null)
        {
            fade = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(fade, fadePath);
        }
        DuelView.ConfigureFade(fade);
        fade.color = new Color(1f, 1f, 1f, 0.3f);
        EditorUtility.SetDirty(fade);
        var materials = new Dictionary<string, Material>();
        foreach (MaterialData source in data.materials)
        {
            if (source.name.StartsWith("Studio") || source.name.StartsWith("Display"))
                continue;
            string path = Root + "/Materials/" + source.name.Replace(" / ", "_").Replace(" ", "_") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = new Color(source.color[0], source.color[1], source.color[2], source.color[3]);
            material.SetFloat("_Smoothness", 1f - source.roughness);
            EditorUtility.SetDirty(material);
            materials.Add(source.name.Replace('/', '_'), material);
        }
        foreach (CharacterData character in data.characters)
        {
            foreach (string part in new[] { "Body", "Sword" })
            {
                string path = Models + "/Chambara" + character.name + part + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale = 1f;
                importer.bakeAxisConversion = true;
                importer.importAnimation = false;
                importer.animationType = part == "Body" ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    if (!materials.TryGetValue(embedded.name, out Material mapped))
                        throw new InvalidOperationException("Unknown exported material: " + embedded.name);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mapped);
                }
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root = new GameObject("Chambara" + character.name + part);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                    foreach (Material material in renderer.sharedMaterials)
                        if (material == null || material.shader.name != "Universal Render Pipeline/Lit")
                            throw new InvalidOperationException("Missing URP material on " + renderer.name);
                PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Chambara" + character.name + part + ".prefab");
                Object.DestroyImmediate(root);
            }
        }
        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        var master = Object.FindFirstObjectByType<MasterController>();
        var serialized = new SerializedObject(master);
        for (int index = 0; index < 2; index++)
        {
            string suffix = (index + 1).ToString();
            var player = (GameObject)serialized.FindProperty("PlayerObject" + suffix).objectReferenceValue;
            var sword = (GameObject)serialized.FindProperty("Sword" + suffix).objectReferenceValue;
            player.GetComponent<Renderer>().enabled = false;
            Transform oldBody = player.transform.Find("Chambara" + data.characters[index].name + "Body");
            if (oldBody != null)
                Object.DestroyImmediate(oldBody.gameObject);
            foreach (Transform child in sword.transform.Cast<Transform>().ToArray())
                Object.DestroyImmediate(child.gameObject);
            GameObject body = Instantiate(data.characters[index].name, "Body", player.transform);
            body.transform.localPosition = Vector3.down;
            Instantiate(data.characters[index].name, "Sword", sword.transform);
        }
        CharacterData anchor = data.characters[0];
        serialized.FindProperty("chestCenter").vector3Value = new Vector3(anchor.chestCenter[0], anchor.chestCenter[1], anchor.chestCenter[2]);
        serialized.FindProperty("gripRadius").floatValue = 0.55f;
        serialized.FindProperty("guardRadius").floatValue = 0.6f;
        serialized.FindProperty("guardHeight").floatValue = 0.55f;
        serialized.FindProperty("guardPlaneOffset").floatValue = 0.55f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PoseSwords(master, Quaternion.Euler(-90f, 0f, 0f), false);
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
        EditorSceneManager.SaveScene(master.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Chambara assets built. Chest=" + serialized.FindProperty("chestCenter").vector3Value
            + ", guard plane=" + anchor.guardPlaneOffset);
    }

    private static GameObject Instantiate(string color, string part, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Chambara" + color + part + ".prefab");
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
    }

    private static void PoseSwords(MasterController master, Quaternion input, bool guard)
    {
        var serialized = new SerializedObject(master);
        Vector3 center = serialized.FindProperty("chestCenter").vector3Value;
        float radius = serialized.FindProperty("gripRadius").floatValue;
        float plane = serialized.FindProperty("guardPlaneOffset").floatValue;
        foreach (string field in new[] { "Sword1", "Sword2" })
        {
            var sword = (GameObject)serialized.FindProperty(field).objectReferenceValue;
            Vector3 last = Vector3.up;
            SwordPose pose = SwordPoseMath.Calculate(input, guard, new SwordPoseSettings(center, radius, serialized.FindProperty("guardRadius").floatValue, plane, serialized.FindProperty("guardHeight").floatValue), ref last);
            sword.transform.SetLocalPositionAndRotation(pose.Position, pose.Rotation);
        }
    }

    // Batch-friendly previews use the scene's real cameras plus a front view.
    public static void Preview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        var master = Object.FindFirstObjectByType<MasterController>();
        string output = Environment.GetEnvironmentVariable("CHAMBARA_PREVIEW_DIR") ?? Path.GetTempPath() + "chambara-previews";
        Directory.CreateDirectory(output);
        var serialized = new SerializedObject(master);
        var players = new[] { (GameObject)serialized.FindProperty("PlayerObject1").objectReferenceValue,
            (GameObject)serialized.FindProperty("PlayerObject2").objectReferenceValue };
        var front = new GameObject("Preview front camera").AddComponent<Camera>();
        front.clearFlags = CameraClearFlags.SolidColor;
        front.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
        front.fieldOfView = 45f;
        front.nearClipPlane = 0.1f;
        front.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var directions = new[] { Vector3.up, new Vector3(0f, 1f, 1f).normalized,
            new Vector3(1f, 1f, 0.5f).normalized, new Vector3(-1f, -1f, 0.5f).normalized, Vector3.right, Vector3.down };
        for (int i = 0; i < directions.Length; i++)
        {
            PoseSwords(master, Quaternion.FromToRotation(Vector3.forward, directions[i]), i >= 2);
            for (int p = 0; p < players.Length; p++)
            {
                var camera = players[p].GetComponentInChildren<Camera>();
                Capture(camera, Path.Combine(output, $"pose{i}-P{p + 1}.png"));
                front.transform.position = players[p].transform.TransformPoint(new Vector3(0f, 0.7f, 6f));
                front.transform.LookAt(players[p].transform.TransformPoint(new Vector3(0f, 0.4f, 0f)), players[p].transform.up);
                bool otherActive = players[1 - p].activeSelf;
                players[1 - p].SetActive(false);
                Capture(front, Path.Combine(output, $"pose{i}-front-P{p + 1}.png"));
                players[1 - p].SetActive(otherActive);
            }
        }
        Object.DestroyImmediate(front.gameObject);
        Debug.Log("Chambara previews saved to " + output);
    }

    private static void Capture(Camera camera, string path)
    {
        Rect previous = camera.rect;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        var texture = new RenderTexture(960, 720, 24);
        texture.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
        RenderTexture.active = texture;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.rect = previous;
        Object.DestroyImmediate(image);
        texture.Release();
        Object.DestroyImmediate(texture);
    }
}
