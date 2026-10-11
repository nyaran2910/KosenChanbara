using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ChambaraStageAssets
{
    private const string Root = "Assets/Art/Stages/ChambaraStage";

    [Serializable] private sealed class Manifest { public StageMaterial[] materials; }
    [Serializable] private sealed class StageMaterial
    {
        public string name;
        public float[] color;
        public float roughness;
        public float metallic;
        public float[] emission;
        public float emissionStrength;
    }

    [InitializeOnLoadMethod]
    private static void RegisterSceneRepair()
    {
        EditorApplication.update += RestoreAfterReload;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void RestoreAfterReload()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= RestoreAfterReload;
        RestoreLoadedScenes();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => RestoreLoadedScenes();
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) RestoreLoadedScenes();
    }

    [PostProcessScene]
    private static void RestoreLoadedScenes()
    {
        // Fill the newly added reference in an already open scene without
        // reopening or saving it, so the user's unsaved work remains intact.
        string[] fields = { "chambaraStage", "chambaraSuddenDeathStage", "circularStadiumStage" };
        string[] paths = { Root + "/ChambaraStage.prefab",
            "Assets/Art/Stages/ChambaraStageSmall/ChambaraStageSmall.prefab",
            "Assets/Art/Stages/CircularStadium/CircularStadium.prefab" };
        foreach (MasterController master in Resources.FindObjectsOfTypeAll<MasterController>())
        {
            Scene scene = master.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) continue;
            var serialized = new SerializedObject(master);
            bool changed = false;
            for (int i = 0; i < fields.Length; i++)
            {
                SerializedProperty stage = serialized.FindProperty(fields[i]);
                if (stage.objectReferenceValue != null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null) continue;
                stage.objectReferenceValue = prefab;
                changed = true;
            }
            if (!changed) continue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    [MenuItem("Tools/Chambara/Rebuild chambara stage")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        MasterController master = Object.FindFirstObjectByType<MasterController>();
        var serialized = new SerializedObject(master);
        var arena = (SpriteRenderer)serialized.FindProperty("arena").objectReferenceValue;
        float arenaRadius = Mathf.Min(arena.bounds.extents.x, arena.bounds.extents.z);
        if (arenaRadius <= 0f) throw new InvalidOperationException("The playable arena radius is missing.");

        string modelPath = Root + "/ChambaraStage.fbx";
        ImportModel(modelPath);

        var root = new GameObject("Chambara Stage");
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Renderer rim = renderers.Single(renderer => renderer.name == "Pale_red_rim");
            Renderer floor = renderers.Single(renderer => renderer.name == "Platform_floor");
            float scale = arenaRadius / Mathf.Min(rim.bounds.extents.x, rim.bounds.extents.z);
            instance.transform.localScale = Vector3.one * scale;
            Bounds floorBounds = floor.bounds;
            instance.transform.localPosition = new Vector3(-floorBounds.center.x, -floorBounds.max.y, -floorBounds.center.z);
            if (Mathf.Abs(floor.bounds.max.y) > 0.001f
                || Mathf.Abs(Mathf.Min(rim.bounds.extents.x, rim.bounds.extents.z) - arenaRadius) > 0.001f)
                throw new InvalidOperationException("The imported stage does not match the playable floor/ring-out bounds.");
            foreach (Renderer renderer in renderers)
                if (renderer.sharedMaterials.Any(material => material == null || material.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("Stage material remapping failed: " + renderer.name);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/ChambaraStage.prefab");
            serialized.FindProperty("chambaraStage").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("Chambara stage built: " + renderers.Length + " meshes, floor " + floor.bounds + ", ring-out radius " + arenaRadius + ", scale " + scale);
        }
        finally { Object.DestroyImmediate(root); }
        // Only persist the reference after removing the temporary prefab root.
        // A live root saved here would remain underneath every later stage.
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
        EditorSceneManager.SaveScene(master.gameObject.scene);
        AssetDatabase.SaveAssets();
    }
    private static void ImportModel(string modelPath)
    {
        Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.ChangeExtension(modelPath, ".json")));
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath) + "/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.globalScale = 1f;
        importer.useFileScale = false;
        importer.indexFormat = ModelImporterIndexFormat.UInt32;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importTangents = ModelImporterTangents.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        foreach (var identifier in importer.GetExternalObjectMap().Keys.ToArray())
            if (identifier.type == typeof(Material)) importer.RemoveRemap(identifier);
        importer.SaveAndReimport();
        foreach (StageMaterial entry in manifest.materials)
        {
            string path = Path.GetDirectoryName(modelPath) + "/Materials/" + entry.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]).gamma;
            material.SetFloat("_Smoothness", 1f - entry.roughness);
            material.SetFloat("_Metallic", entry.metallic);
            if (entry.emission != null && entry.emission.Length >= 3 && entry.emissionStrength > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(entry.emission[0], entry.emission[1], entry.emission[2]) * entry.emissionStrength);
            }
            if (entry.color[3] < .99f)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), material);
        }
        AssetDatabase.SaveAssets();
        importer.SaveAndReimport();

    }

    [MenuItem("Tools/Chambara/Rebuild sudden-death stage and circular stadium")]
    public static void BuildAdditionalStages()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        MasterController master = Object.FindFirstObjectByType<MasterController>();
        var serialized = new SerializedObject(master);
        const string smallRoot = "Assets/Art/Stages/ChambaraStageSmall/ChambaraStageSmall";
        const string stadiumRoot = "Assets/Art/Stages/CircularStadium/CircularStadium";
        ImportModel(smallRoot + ".fbx");
        ImportModel(stadiumRoot + ".fbx");
        var normal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/ChambaraStage.prefab"));
        try
        {
            float scale = normal.transform.GetChild(0).localScale.x;
            float normalRadius = Radius(normal);
            GameObject small = SaveNormalized(smallRoot, scale, "Platform_floor");
            var smallInstance = Object.Instantiate(small);
            try
            {
                float ratio = Radius(smallInstance) / normalRadius;
                if (ratio <= 0f || ratio >= 1f) throw new InvalidOperationException("The sudden-death platform must be smaller than the normal platform.");
                serialized.FindProperty("suddenDeathRadiusRatio").floatValue = ratio;
            }
            finally { Object.DestroyImmediate(smallInstance); }
            serialized.FindProperty("chambaraSuddenDeathStage").objectReferenceValue = small;
            // FBX export is in centimeters. A stadium meter equals one gameplay meter.
            GameObject stadium = SaveNormalized(stadiumRoot, .01f, "Grass_mowing_stripe_00");
            serialized.FindProperty("circularStadiumStage").objectReferenceValue = stadium;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        finally { Object.DestroyImmediate(normal); }
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
        EditorSceneManager.SaveScene(master.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static float Radius(GameObject root)
    {
        Renderer rim = root.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name == "Pale_red_rim");
        return Mathf.Min(rim.bounds.extents.x, rim.bounds.extents.z);
    }

    private static GameObject SaveNormalized(string path, float scale, string floorName)
    {
        var root = new GameObject(Path.GetFileName(path));
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path + ".fbx"), root.transform);
            model.transform.localScale = Vector3.one * scale;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            Renderer floor = renderers.Single(renderer => renderer.name == floorName);
            // The grass stripe is offset from the center, so preserve the original
            // centered pitch coordinates rather than centering on that stripe.
            model.transform.localPosition = Vector3.down * floor.bounds.max.y;
            foreach (Renderer renderer in renderers)
                if (renderer.sharedMaterials.Any(material => material == null || material.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("Stage material remapping failed: " + renderer.name);
            Debug.Log("Built " + path + ": " + renderers.Length + " renderers, floor " + floor.bounds.max.y);
            return PrefabUtility.SaveAsPrefabAsset(root, path + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

}
