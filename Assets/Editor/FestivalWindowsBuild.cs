using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FestivalWindowsBuild
{
    public static void Build()
    {
        string destination = Environment.GetEnvironmentVariable("KOSEN_BUILD_OUTPUT");
        if (string.IsNullOrWhiteSpace(destination)) throw new InvalidOperationException("KOSEN_BUILD_OUTPUT is required.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            throw new InvalidOperationException("Install Windows Build Support for this Unity Editor version.");

        // Preserve the project's scenes, PlayerSettings and saved build options.
        // The public helper always opens SaveBuildPanel. Unity 6000.3's
        // noninteractive variant reads the same saved profile/options.
        EditorUserBuildSettings.SetBuildLocation(BuildTarget.StandaloneWindows64, destination);
        MethodInfo defaults = typeof(BuildPlayerWindow.DefaultBuildMethods).GetMethod(
            "GetBuildPlayerOptionsFromActiveBuildTarget", BindingFlags.Static | BindingFlags.NonPublic);
        if (defaults == null) throw new InvalidOperationException("This build script requires Unity 6000.3's build settings API.");
        var options = (BuildPlayerOptions)defaults.Invoke(null, new object[] { false, new BuildPlayerOptions {
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            locationPathName = destination
        } });
        options.target = BuildTarget.StandaloneWindows64;
        options.targetGroup = BuildTargetGroup.Standalone;
        options.subtarget = (int)StandaloneBuildSubtarget.Player;
        options.locationPathName = destination;
        options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ShowBuiltPlayer);
        if (options.scenes == null || options.scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes in the current build settings.");

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows build failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination), "BuildInfo.json"),
            JsonUtility.ToJson(new BuildInfo {
                unity = Application.unityVersion,
                builtAtUtc = DateTime.UtcNow.ToString("O"),
                target = options.target.ToString(),
                options = options.options.ToString(),
                scenes = options.scenes
            }, true));
        Debug.Log("KosenChanbara Windows build succeeded: " + destination);
    }

    [Serializable] private sealed class BuildInfo
    {
        public string unity, builtAtUtc, target, options;
        public string[] scenes;
    }
}
