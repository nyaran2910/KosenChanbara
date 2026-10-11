using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CricketStadiumAssets
{
    private const string Root = "Assets/Art/Stages/CricketStadium";

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
        if (state == PlayModeStateChange.ExitingEditMode)
            RestoreLoadedScenes();
    }

    [PostProcessScene]
    private static void RestoreLoadedScenes()
    {
        // A scene already open when the stadium was added can still have an empty
        // in-memory reference even though its file on disk has been updated.
        // Repair only that missing reference, keeping unsaved scene edits intact.
        GameObject prefab = null;
        foreach (var master in Resources.FindObjectsOfTypeAll<MasterController>())
        {
            Scene scene = master.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) continue;
            var serialized = new SerializedObject(master);
            SerializedProperty stage = serialized.FindProperty("cricketStadiumStage");
            if (stage.objectReferenceValue != null) continue;
            if (prefab == null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/CricketStadium.prefab");
            if (prefab == null) continue;
            Renderer rope = prefab.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name == "outer area.003");
            stage.objectReferenceValue = prefab;
            serialized.FindProperty("cricketBoundaryRadius").floatValue = Mathf.Min(rope.bounds.extents.x, rope.bounds.extents.z) - 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorApplication.isPlaying)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    [MenuItem("Tools/Chambara/Rebuild cricket stadium")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string modelPath = Root + "/CricketStadium.fbx";
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        var textures = new Dictionary<string, string>
        {
            { "Boundary texture", "Boundary" }, { "Ground texture", "Ground" },
            { "Pitch", "Pitch" }, { "Rope texture", "Rope_02" }, { "Scoreboard screen", "Scoreboard" }
        };
        // Texture reimports can reload the model importer, so finish them before remapping materials.
        foreach (string textureName in textures.Values)
        {
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(Root + "/" + textureName + ".png");
            textureImporter.sRGBTexture = true;
            textureImporter.mipmapEnabled = true;
            textureImporter.SaveAndReimport();
        }
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.globalScale = 1f;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importTangents = ModelImporterTangents.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        foreach (var identifier in importer.GetExternalObjectMap().Keys.ToArray())
            if (identifier.type == typeof(Material)) importer.RemoveRemap(identifier);
        importer.SaveAndReimport();
        var sources = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>()
            .Select(source => new { source.name, source.color }).ToArray();
        var remaps = new Dictionary<string, Material>();
        foreach (var source in sources)
        {
            string path = Root + "/Materials/" + source.name.Replace(' ', '_') + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = source.color;
            material.SetFloat("_Smoothness", 0.1f);
            if (textures.TryGetValue(source.name, out string textureName))
            {
                string texturePath = Root + "/" + textureName + ".png";
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                material.color = Color.white;
                material.SetFloat("_Smoothness", 0f);
            }
            if (source.name == "Emissive")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.white);
            }
            EditorUtility.SetDirty(material);
            remaps.Add(source.name, material);
        }
        AssetDatabase.SaveAssets();
        importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        foreach (var remap in remaps)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), remap.Key), remap.Value);
        importer.SaveAndReimport();

        var root = new GameObject("Cricket Stadium");
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
            Renderer ground = instance.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name == "Ground .009");
            Bounds floor = ground.bounds;
            instance.transform.localPosition = new Vector3(-floor.center.x, -floor.max.y, -floor.center.z);
            Renderer rope = instance.GetComponentsInChildren<Renderer>().Single(renderer => renderer.name == "outer area.003");
            float radius = Mathf.Min(rope.bounds.extents.x, rope.bounds.extents.z) - 2f;
            if (radius < 15f || radius > Mathf.Min(floor.extents.x, floor.extents.z))
                throw new InvalidOperationException("Unexpected cricket field dimensions: " + floor);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (renderer.sharedMaterials.Any(material => material == null || material.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("Cricket material remapping failed: " + renderer.name);
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/CricketStadium.prefab");
            Bounds alignedFloor = ground.bounds;
            Object.DestroyImmediate(root);
            EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
            var master = Object.FindFirstObjectByType<MasterController>();
            var serialized = new SerializedObject(master);
            serialized.FindProperty("cricketStadiumStage").objectReferenceValue = prefab;
            serialized.FindProperty("cricketBoundaryRadius").floatValue = radius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
            EditorSceneManager.SaveScene(master.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Cricket stadium built. Ground " + alignedFloor + ", safe field radius " + radius);
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
    }
}
