using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Imports the Blender-exported Churroman body and churros sword. Scene and
// roster assignment are handled by ChurrosRoster after these assets exist.
public static class ChurrosAssets
{
    private const string Root = "Assets/Art/Chambara/Churros";
    private const string Models = Root + "/Models";
    private const string Materials = Root + "/Materials";
    private const string Prefabs = Root + "/Prefabs";

    [Serializable] private sealed class ExportData { public MaterialData[] materials; }
    [Serializable] private sealed class MaterialData
    {
        public string name;
        public float[] color;
        public float roughness;
        public float metallic;
    }

    [MenuItem("Tools/Chambara/Build Churros assets")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ExportData data = JsonUtility.FromJson<ExportData>(File.ReadAllText(Models + "/ChurrosExport.json"));
        Directory.CreateDirectory(Materials);
        Directory.CreateDirectory(Prefabs);
        AssetDatabase.Refresh();
        var materials = new Dictionary<string, Material>();
        foreach (MaterialData source in data.materials)
        {
            string path = Materials + "/" + SafeName(source.name) + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = SafeName(source.name);
            material.color = new Color(source.color[0], source.color[1], source.color[2], source.color[3]);
            material.SetFloat("_Smoothness", 1f - source.roughness);
            material.SetFloat("_Metallic", source.metallic);
            EditorUtility.SetDirty(material);
            materials[Key(source.name)] = material;
        }

        GameObject body = ImportModel(Models + "/ChurromanBody.fbx", true, materials);
        foreach (string color in new[] { "RED", "BLUE", "GREEN", "YELLOW", "PURPLE", "BLACK" })
            SavePalette(body, "ChambaraChurroman" + Title(color) + "Body", color, materials);
        GameObject sword = ImportModel(Models + "/ChurrosBlade.fbx", false, materials);
        var swordRoot = new GameObject("ChambaraChurrosSword");
        PrefabUtility.InstantiatePrefab(sword, swordRoot.transform);
        Save(swordRoot, Prefabs + "/ChambaraChurrosSword.prefab");
        UnityEngine.Object.DestroyImmediate(swordRoot);
        AssetDatabase.SaveAssets();
        Debug.Log("Churros assets built: six Churroman body palettes and alternate sword.");
    }

    private static GameObject ImportModel(string path, bool body, Dictionary<string, Material> materials)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.globalScale = 1f;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.animationType = body ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
        {
            if (!materials.TryGetValue(Key(embedded.name), out Material mapped))
                throw new InvalidOperationException("Unknown Churros export material: " + embedded.name);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mapped);
        }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void SavePalette(GameObject model, string name, string color, Dictionary<string, Material> materials)
    {
        var root = new GameObject(name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                string key = Key(slots[i].name);
                if (key.StartsWith("Armor_"))
                {
                    string paletteKey = "Armor_" + color;
                    if (!materials.TryGetValue(paletteKey, out slots[i]))
                    {
                        Material fallback = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/Art/Chambara/Roster/Materials/" + paletteKey + ".mat");
                        if (fallback == null)
                            throw new InvalidOperationException("Missing palette material " + paletteKey);
                        slots[i] = fallback;
                    }
                }
            }
            renderer.sharedMaterials = slots;
        }
        Save(root, Prefabs + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void Save(GameObject root, string path)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            foreach (Material material in renderer.sharedMaterials)
                if (material == null || material.shader == null || material.shader.name != "Universal Render Pipeline/Lit")
                    throw new InvalidOperationException("Missing URP material on " + renderer.name);
        PrefabUtility.SaveAsPrefabAsset(root, path);
    }

    private static string Key(string name)
    {
        if (name.EndsWith(".001", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
        return name.Replace(" / ", "_").Replace(" _ ", "_").Replace(" ", "_");
    }
    private static string SafeName(string name) => Key(name).Replace("/", "_");
    private static string Title(string color) => color.Substring(0, 1) + color.Substring(1).ToLowerInvariant();
}
