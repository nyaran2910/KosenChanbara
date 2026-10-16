using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ChurrosRoster
{
    private const string Prefabs = "Assets/Art/Chambara/Churros/Prefabs/";

    [MenuItem("Tools/Chambara/Build and install churros")]
    public static void Build()
    {
        ChurrosAssets.Build();
        Install();
    }

    [MenuItem("Tools/Chambara/Install churros roster")]
    public static void Install()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/BattleGround.unity");
        var master = UnityEngine.Object.FindFirstObjectByType<MasterController>();
        var serialized = new SerializedObject(master);
        var names = serialized.FindProperty("characterNames");
        int index = -1;
        for (int i = 0; i < names.arraySize; i++)
            if (names.GetArrayElementAtIndex(i).stringValue == "チュロスメン") index = i;
        if (index < 0) { index = names.arraySize; names.arraySize++; }
        names.GetArrayElementAtIndex(index).stringValue = "チュロスメン";
        var bodies = serialized.FindProperty("characterBodies");
        bodies.arraySize = names.arraySize * MasterController.ColorNames.Length;
        for (int color = 0; color < MasterController.ColorNames.Length; color++)
        {
            string title = MasterController.ColorNames[color].Substring(0, 1)
                + MasterController.ColorNames[color].Substring(1).ToLowerInvariant();
            bodies.GetArrayElementAtIndex(index * MasterController.ColorNames.Length + color).objectReferenceValue
                = RequirePrefab("ChambaraChurroman" + title + "Body");
        }
        var centers = serialized.FindProperty("characterChestCenters");
        centers.arraySize = names.arraySize;
        // Visual proportions never change the shared body/guard target.
        centers.GetArrayElementAtIndex(index).vector3Value = centers.GetArrayElementAtIndex(0).vector3Value;
        serialized.FindProperty("churrosBlade").objectReferenceValue = RequirePrefab("ChambaraChurrosSword");
        serialized.FindProperty("churrosWeaponChance").floatValue = .05f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(master.gameObject.scene);
        EditorSceneManager.SaveScene(master.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Churros roster installed: " + names.arraySize + " characters, Churroman always uses churros, other weapon chance 5%.");
    }

    private static GameObject RequirePrefab(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
        if (prefab == null) throw new InvalidOperationException("Build the churros assets first: " + name);
        return prefab;
    }
}
