using UnityEditor;

// Match ProjectSettings/ProjectSettings.asset in an already-open Editor too.
// The scoreboard intentionally uses HTTP on the local network.
[InitializeOnLoad]
public static class ScoreboardNetworkSettings
{
    static ScoreboardNetworkSettings()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingEditMode) Apply();
        };
    }

    private static void Apply()
    {
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
    }
}
