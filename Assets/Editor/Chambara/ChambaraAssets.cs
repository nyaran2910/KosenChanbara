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
        if (File.Exists(Root + "/Roster/Models/ChambaraRoster.json"))
        {
            BuildRoster();
            return;
        }
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
        foreach (string part in new[] { "Body", "Sword" })
        {
            SerializedProperty prefabs = serialized.FindProperty(part == "Body" ? "characterBodies" : "characterBlades");
            prefabs.arraySize = data.characters.Length;
            for (int index = 0; index < data.characters.Length; index++)
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Root + "/Prefabs/Chambara" + data.characters[index].name + part + ".prefab");
        }
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

    // The four bodies share their meshes across six palette variants.
    public static void BuildRoster()
    {
        const string roster = Root + "/Roster";
        const string stage = "Assets/Art/Stages/Colosseum";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var data = JsonUtility.FromJson<ExportData>(File.ReadAllText(roster + "/Models/ChambaraRoster.json"));
        Directory.CreateDirectory(roster + "/Materials");
        Directory.CreateDirectory(roster + "/Prefabs");
        AssetDatabase.Refresh();
        var materials = new Dictionary<string, Material>();
        foreach (MaterialData source in data.materials)
        {
            if (source.name.StartsWith("Studio") || source.name.StartsWith("Display")) continue;
            string name = MaterialKey(source.name);
            string path = roster + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name;
            material.color = new Color(source.color[0], source.color[1], source.color[2], source.color[3]);
            material.SetFloat("_Smoothness", 1f - source.roughness);
            if (name == "Robot_body_metal" || name == "Robot_brushed_metal" || name == "Robot_screws")
                material.SetFloat("_Metallic", 0.65f);
            EditorUtility.SetDirty(material);
            materials[name] = material;
        }
        var bodies = new List<GameObject>();
        var blades = new List<GameObject>();
        foreach (CharacterData character in data.characters)
        {
            GameObject model = ImportRosterModel(roster + "/Models/Chambara" + character.name + "Body.fbx", true, materials);
            foreach (string color in MasterController.ColorNames)
                bodies.Add(PalettePrefab(model, roster + "/Prefabs", "Chambara" + character.name + ColorTitle(color) + "Body", color, materials));
        }
        GameObject swordModel = ImportRosterModel(roster + "/Models/ChambaraOriginalSword.fbx", false, materials);
        foreach (string color in MasterController.ColorNames)
            blades.Add(PalettePrefab(swordModel, roster + "/Prefabs", "Chambara" + ColorTitle(color) + "Sword", color, materials));

        var textureImporter = (TextureImporter)AssetImporter.GetAtPath(stage + "/terrain.png");
        textureImporter.sRGBTexture = true;
        textureImporter.filterMode = FilterMode.Point;
        textureImporter.mipmapEnabled = true;
        textureImporter.SaveAndReimport();
        string terrainPath = stage + "/Terrain.mat";
        var terrain = AssetDatabase.LoadAssetAtPath<Material>(terrainPath);
        if (terrain == null)
        {
            terrain = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(terrain, terrainPath);
        }
        terrain.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(stage + "/terrain.png"));
        terrain.color = Color.white;
        terrain.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(terrain);
        string stageModelPath = stage + "/Colosseum.fbx";
        var stageImporter = (ModelImporter)AssetImporter.GetAtPath(stageModelPath);
        ConfigureModel(stageImporter, false);
        stageImporter.SaveAndReimport();
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(stageModelPath).OfType<Material>())
            stageImporter.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), terrain);
        stageImporter.SaveAndReimport();
        var stageRoot = new GameObject("Colosseum");
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(stageModelPath), stageRoot.transform);
        GameObject stagePrefab = PrefabUtility.SaveAsPrefabAsset(stageRoot, stage + "/Colosseum.prefab");
        Object.DestroyImmediate(stageRoot);

        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        var master = Object.FindFirstObjectByType<MasterController>();
        var serialized = new SerializedObject(master);
        AssignPrefabs(serialized.FindProperty("characterBodies"), bodies);
        AssignPrefabs(serialized.FindProperty("characterBlades"), blades);
        SerializedProperty names = serialized.FindProperty("characterNames");
        SerializedProperty centers = serialized.FindProperty("characterChestCenters");
        names.arraySize = centers.arraySize = data.characters.Length;
        for (int index = 0; index < data.characters.Length; index++)
        {
            names.GetArrayElementAtIndex(index).stringValue = data.characters[index].name.ToUpperInvariant();
            float[] center = data.characters[index].chestCenter;
            centers.GetArrayElementAtIndex(index).vector3Value = new Vector3(center[0], center[1], center[2]);
        }
        serialized.FindProperty("chestCenter").vector3Value = centers.GetArrayElementAtIndex(0).vector3Value;
        serialized.FindProperty("colosseumStage").objectReferenceValue = stagePrefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        for (int index = 0; index < 2; index++)
        {
            var player = (GameObject)serialized.FindProperty("PlayerObject" + (index + 1)).objectReferenceValue;
            var sword = (GameObject)serialized.FindProperty("Sword" + (index + 1)).objectReferenceValue;
            foreach (Transform child in player.transform.Cast<Transform>().Where(t => t.name.StartsWith("Chambara")).ToArray())
                Object.DestroyImmediate(child.gameObject);
            foreach (Transform child in sword.transform.Cast<Transform>().ToArray())
                Object.DestroyImmediate(child.gameObject);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodies[index], player.transform);
            body.transform.localPosition = Vector3.down;
            PrefabUtility.InstantiatePrefab(blades[index], sword.transform);
        }
        PoseSwords(master, Quaternion.Euler(-90f, 0f, 0f), false);
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
        EditorSceneManager.SaveScene(master.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Chambara roster built: " + bodies.Count + " bodies, " + blades.Count + " swords, colosseum.");
        if (File.Exists(Root + "/Churros/Models/ChurrosExport.json")) ChurrosRoster.Build();
    }

    private static string MaterialKey(string name)
    {
        if (name.EndsWith(".001")) name = name.Substring(0, name.Length - 4);
        return name.Replace(" / ", "_").Replace(" _ ", "_").Replace(" ", "_");
    }

    private static string ColorTitle(string color) => color.Substring(0, 1) + color.Substring(1).ToLowerInvariant();

    private static void ConfigureModel(ModelImporter importer, bool body)
    {
        importer.globalScale = 1f;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.animationType = body ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }

    private static GameObject ImportRosterModel(string path, bool body, Dictionary<string, Material> materials)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        ConfigureModel(importer, body);
        importer.SaveAndReimport();
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
        {
            if (!materials.TryGetValue(MaterialKey(embedded.name), out Material mapped))
                throw new InvalidOperationException("Unknown exported material: " + embedded.name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mapped);
        }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static GameObject PalettePrefab(GameObject model, string folder, string name, string color, Dictionary<string, Material> materials)
    {
        var root = new GameObject(name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            Material[] palette = renderer.sharedMaterials;
            for (int index = 0; index < palette.Length; index++)
            {
                string key = MaterialKey(palette[index].name);
                foreach (string prefix in new[] { "Armor_seams_", "Armor_", "Padded_sword_" })
                    if (key.StartsWith(prefix)) { palette[index] = materials[prefix + color]; break; }
            }
            renderer.sharedMaterials = palette;
        }
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void AssignPrefabs(SerializedProperty field, List<GameObject> prefabs)
    {
        field.arraySize = prefabs.Count;
        for (int index = 0; index < prefabs.Count; index++)
            field.GetArrayElementAtIndex(index).objectReferenceValue = prefabs[index];
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
