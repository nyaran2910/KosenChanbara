using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class KojoRequestedChecks
{
    private static TestRunnerApi runner;
    static KojoRequestedChecks()
    {
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.RegisterCallbacks(new Results());
    }
    [MenuItem("Tools/Kojo Checks/Run Controller Edit")]
    public static void RunControllerEdit() => Run(TestMode.EditMode, null, "controller-edit");
    [MenuItem("Tools/Kojo Checks/Run Controller Play")]
    public static void RunControllerPlay()
    {
        Environment.SetEnvironmentVariable("SF_CONTROLLER_PREVIEW_DIR", "/tmp/sf-controller-preview");
        Run(TestMode.PlayMode, "ControllerRegistrationPlayTests,ControllerAxisCalibrationPlayTests,GameFlowPlayTests,MotionSourceSwitchPlayTests,CombatCameraAndCalibrationPlayTests", "controller-play");
    }
    [MenuItem("Tools/Kojo Checks/Run Preview Play")]
    public static void RunPlay()
    {
        Environment.SetEnvironmentVariable("CHAMBARA_PREVIEW_DIR", "/tmp/kojo-field-preview");
        Run(TestMode.PlayMode, "GameFlowPlayTests,BattleGroundPlayTests", "preview-play");
    }
    [MenuItem("Tools/Kojo Checks/Run All Play")]
    public static void RunAllPlay()
    {
        Environment.SetEnvironmentVariable("CHAMBARA_PREVIEW_DIR", "/tmp/kojo-field-preview");
        Run(TestMode.PlayMode, null, "preview-all-play");
    }
    [MenuItem("Tools/Kojo Checks/Run Scoreboard Play")]
    public static void RunScoreboardPlay()
    {
        Environment.SetEnvironmentVariable("SF_SCORE_PREVIEW_DIR", "/tmp/sf-score-preview");
        Run(TestMode.PlayMode, "ScoreboardPlayTests,ScoreboardDiscoveryPlayTests,GameFlowPlayTests,DuelPresentationResultPlayTests,SingleDefeatPlayTests,ShowdownRoundShortcutPlayTests", "scoreboard-play");
    }
    [MenuItem("Tools/Kojo Checks/Run Scoreboard Discovery")]
    public static void RunScoreboardDiscovery()
        => Run(TestMode.PlayMode, "ScoreboardPlayTests,ScoreboardDiscoveryPlayTests,DuelPresentationResultPlayTests,SingleDefeatPlayTests,ShowdownRoundShortcutPlayTests", "scoreboard-discovery");
    [MenuItem("Tools/Kojo Checks/Capture Preview")]
    public static void Capture()
    {
        Environment.SetEnvironmentVariable("CHAMBARA_PREVIEW_DIR", "/tmp/kojo-field-preview");
        Run(TestMode.PlayMode, "CaptureSelectionFlowWhenRequested", "preview-capture");
    }
    [MenuItem("Tools/Kojo Checks/Build Latest Mac")]
    public static void BuildMac()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/KosenChanbara.app"));
        File.WriteAllText("/tmp/kojo-preview-build-state", "building " + path);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
            locationPathName = path, target = BuildTarget.StandaloneOSX, options = BuildOptions.None
        });
        File.WriteAllText("/tmp/kojo-preview-build-state", report.summary.result + " errors=" + report.summary.totalErrors + " path=" + path);
    }
    private static void Run(TestMode mode, string name, string tag)
    {
        if (EditorApplication.isPlaying) { File.WriteAllText("/tmp/kojo-preview-check-state", "stop play first"); return; }
        SessionState.SetString("KojoPreviewCheckTag", tag);
        File.WriteAllText("/tmp/kojo-preview-check-state", "starting " + tag);
        var filter = new Filter { testMode = mode };
        if (name != null) filter.testNames = name.Split(',');
        runner.Execute(new ExecutionSettings(filter));
    }
    private sealed class Results : ICallbacks
    {
        private string tag => SessionState.GetString("KojoPreviewCheckTag", "preview-play");
        public void RunStarted(ITestAdaptor testsToRun) { File.WriteAllText("/tmp/kojo-preview-check-state", "running " + tag); }
        public void RunFinished(ITestResultAdaptor result)
        {
            TestRunnerApi.SaveResultToFile(result, "/tmp/kojo-" + tag + ".xml");
            File.WriteAllText("/tmp/kojo-preview-check-state", tag + " " + result.ResultState + " passed=" + result.PassCount + " failed=" + result.FailCount);
            Environment.SetEnvironmentVariable("CHAMBARA_PREVIEW_DIR", null);
            Environment.SetEnvironmentVariable("SF_CONTROLLER_PREVIEW_DIR", null);
            Environment.SetEnvironmentVariable("SF_SCORE_PREVIEW_DIR", null);
        }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
    }
}
