using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SchoolFestival.Combat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ScoreboardPlayTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private MonoBehaviour flow, master;
    private TestScoreServer server;
    private ScoreboardDiscoveryPlayTests.DiscoveryResponder responder;
    private Keyboard keyboard;
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    private static MonoBehaviour Find(string name) => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(c => c.GetType().Name == name);
    private static bool Open(MonoBehaviour flow) => (bool)flow.GetType().GetProperty("ScoreDialogOpen", Flags).GetValue(flow);

    [UnitySetUp]
    public IEnumerator Setup()
    {
        server = new TestScoreServer();
        responder = new ScoreboardDiscoveryPlayTests.DiscoveryResponder(server.Port, server.ServerId);
        keyboard = InputSystem.AddDevice<Keyboard>();
        SceneManager.sceneLoaded += DisableTransport;
        yield return SceneManager.LoadSceneAsync("BattleGround");
        SceneManager.sceneLoaded -= DisableTransport;
        yield return null;
        flow = Find("GameFlowController"); master = Find("MasterController"); master.enabled = false;
        object previous = Field<object>(flow, "scoreClient");
        ((IDisposable)previous).Dispose();
        Set(flow, "scoreClient", Activator.CreateInstance(previous.GetType(), new object[] { responder.Port }));
    }

    private static void DisableTransport(Scene scene, LoadSceneMode mode)
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub", "MotionController" }.Contains(component.GetType().Name)) component.enabled = false;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        SceneManager.sceneLoaded -= DisableTransport;
        if (flow != null) { Call(flow, "ReturnToTitle"); ((IDisposable)Field<object>(flow, "scoreClient")).Dispose(); }
        responder?.Dispose();
        server?.Dispose();
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (new[] { "PhoneControllerHub", "HardwareControllerHub" }.Contains(component.GetType().Name)) Object.Destroy(component.gameObject);
        yield return null;
    }

    private static IEnumerator Until(Func<bool> ready)
    {
        float deadline = Time.realtimeSinceStartup + 8;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(ready(), Is.True, "Timed out waiting for scoreboard UI / HTTP.");
    }

    private void Begin()
    {
        Call(flow, "ChooseMode", true); Call(flow, "Ready", 0);
        Set(master, "matchTime", 4d);
    }

    [UnityTest]
    public IEnumerator SingleSelectionShowsTenNamesAndScoresOnRightAndKeepsOtherModesUnchanged()
    {
        Call(flow, "ChooseSingleMode"); Call(flow, "ChooseDifficulty", false);
        yield return Until(() => flow.GetComponentsInChildren<Text>().Any(t => t.name == "Leaderboard name 1" && t.text == "チャンピオン"));
        RectTransform panel = flow.transform.Find("Game flow/Page/Single leaderboard").GetComponent<RectTransform>();
        Assert.That(panel.anchoredPosition.x, Is.GreaterThan(0));
        Assert.That(panel.GetComponentsInChildren<Text>().Count(t => t.name.StartsWith("Leaderboard name ")), Is.EqualTo(10));
        Assert.That(flow.transform.Find("Game flow/Page/Live fighter preview").GetComponent<RectTransform>().anchoredPosition.x, Is.LessThan(0));
        Assert.That(panel.GetComponentsInChildren<Text>().Single(t => t.name == "Leaderboard score 1").text, Is.EqualTo("99人"));
        yield return Capture("single-ranking.png");
        Call(flow, "ChooseSingleMode"); Call(flow, "ChooseDifficulty", true);
        yield return Until(() => flow.GetComponentsInChildren<Text>().Any(t => t.name == "Leaderboard name 1" && t.text == "イージーの人"));
        Call(flow, "ChooseTutorialMode");
        yield return null;
        Assert.That(flow.transform.Find("Game flow/Page/Single leaderboard"), Is.Null);
        Assert.That(flow.transform.Find("Game flow/Page/Live fighter preview").GetComponent<RectTransform>().anchoredPosition.x, Is.Zero);
        Call(flow, "ChooseMode", false);
        yield return null;
        Assert.That(flow.transform.Find("Game flow/Page/Single leaderboard"), Is.Null);
    }

    [UnityTest]
    public IEnumerator DeathOffersNameAfterTwoSecondsSavesOnceAndRefreshesSelection()
    {
        Begin();
        ShowdownProgress progress = Field<ShowdownProgress>(master, "showdown");
        for (int i = 0; i < 88; i++) { progress.TrySpawn(); progress.Defeat(); }
        for (int i = 0; progress.Hearts > 0; i++) progress.Damage(1 + i * .21d);
        Call(master, "BeginSingleDefeat", 4d);
        Call(master, "TickSingleMode", 5.999d, .016f);
        Assert.That(Open(flow), Is.False);
        Call(master, "TickSingleMode", 6d, .001f);
        Call(master, "TickSingleMode", 6.01d, .01f);
        yield return Until(() => Open(flow) && Field<InputField>(flow, "scoreName").gameObject.activeSelf);
        Assert.That(server.Qualifications, Is.EqualTo(1), "Repeated result frames must not offer duplicate registrations.");
        Assert.That((bool)flow.GetType().GetProperty("IsPlaying").GetValue(flow), Is.False);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R, Key.T, Key.Digit8, Key.LeftShift));
        InputSystem.Update(); Call(master, "LateUpdate"); Call(flow, "Update");
        Assert.That(progress.Defeated, Is.EqualTo(88), "Typing must not restart or jump rounds.");
        object timer = Field<object>(flow, "sessionTimer");
        Assert.That((bool)timer.GetType().GetProperty("Armed").GetValue(timer), Is.False, "Typing T must not arm the session timer.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        Field<InputField>(flow, "scoreName").text = "新人";
        yield return Capture("score-entry.png");
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        yield return Until(() => Field<Text>(flow, "scoreTitle").text == "記録しました");
        Assert.That(server.Posts, Is.EqualTo(1));
        Assert.That(server.Bodies.Single(), Does.Contain("\"defeated\":88"));
        Assert.That(server.Bodies.Single(), Does.Contain("新人"));
        Call(flow, "CloseScoreDialog"); Call(flow, "ReturnToCharacters");
        yield return Until(() => flow.GetComponentsInChildren<Text>().Any(t => t.name == "Leaderboard name 3" && t.text == "新人"));
    }

    [UnityTest]
    public IEnumerator OutsideTopTenSkipsNameEntryAndConcurrentUpdateCanRejectASave()
    {
        Begin(); server.Qualified = false;
        Call(flow, "OfferSingleScore", 1);
        yield return Until(() => !Open(flow));
        Assert.That(server.Posts, Is.Zero);
        server.Qualified = true; server.LoseRankOnSave = true;
        Call(flow, "OfferSingleScore", 88);
        yield return Until(() => Field<InputField>(flow, "scoreName").gameObject.activeSelf);
        Field<InputField>(flow, "scoreName").text = "新人";
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        yield return Until(() => Field<Text>(flow, "scoreTitle").text == "ランキングが更新されました");
        Assert.That(Field<InputField>(flow, "scoreName").gameObject.activeSelf, Is.False);
        Assert.That(Field<Button>(flow, "scoreSkip").interactable, Is.True);
    }

    [UnityTest]
    public IEnumerator ConnectionFailureCanRetryAndLateRepliesCannotReopenResetResults()
    {
        Begin(); server.Unavailable = true;
        Call(flow, "OfferSingleScore", 88);
        yield return Until(() => Field<Text>(flow, "scoreTitle").text == "ランキングに接続できません");
        server.Unavailable = false;
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        yield return Until(() => Field<InputField>(flow, "scoreName").gameObject.activeSelf);
        Call(master, "ResetMatch");
        Assert.That(Open(flow), Is.False);
        server.HoldQualification.Reset();
        int previousQualifications = server.Qualifications;
        Call(flow, "OfferSingleScore", 88);
        yield return Until(() => server.Qualifications > previousQualifications);
        Call(flow, "ReturnToTitle");
        server.HoldQualification.Set();
        yield return new WaitForSecondsRealtime(.2f);
        Assert.That(Open(flow), Is.False);
    }

    [UnityTest]
    public IEnumerator UncertainSubmissionRetriesTheSameRunAndName()
    {
        Begin();
        Call(flow, "OfferSingleScore", 88);
        yield return Until(() => Field<InputField>(flow, "scoreName").gameObject.activeSelf);
        Field<InputField>(flow, "scoreName").text = "新人";
        server.Unavailable = true;
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        yield return Until(() => Field<Text>(flow, "scoreFeedback").text.Contains("再試行してください"));
        Assert.That(Field<InputField>(flow, "scoreName").interactable, Is.False);
        server.Unavailable = false;
        Field<Button>(flow, "scoreSave").onClick.Invoke();
        yield return Until(() => Field<Text>(flow, "scoreTitle").text == "記録しました");
        Assert.That(server.Bodies.Count, Is.EqualTo(3));
        Assert.That(server.Bodies.All(body => body == server.Bodies.First()), Is.True);
    }

    [UnityTest]
    public IEnumerator HundredDefeatsQualifyButUsingRoundShortcutsDoesNot()
    {
        Begin(); Call(master, "JumpToSingleRound", 5);
        CompleteRun();
        Call(master, "TickSingleMode", 10d, .016f);
        Assert.That(Open(flow), Is.False);
        Assert.That(server.Qualifications, Is.Zero);
        Call(master, "ResetMatch"); CompleteRun();
        Call(master, "TickSingleMode", 20d, .016f);
        yield return Until(() => Field<InputField>(flow, "scoreName").gameObject.activeSelf);
        Assert.That(Field<Text>(flow, "scoreDetail").text, Is.EqualTo("100人撃破"));

        void CompleteRun()
        {
            ShowdownProgress progress = Field<ShowdownProgress>(master, "showdown");
            while (progress.TrySpawn()) { }
            while (progress.Defeat()) { }
        }
    }

    private static IEnumerator Capture(string name)
    {
        string folder = Environment.GetEnvironmentVariable("SF_SCORE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(folder)) yield break;
        Directory.CreateDirectory(folder);
        yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame();
        Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToPNG()); Object.Destroy(image);
    }

    // HTTP fixture exercises UnityWebRequest and UI independently of Node being
    // installed on the machine running Unity's PlayMode suite.
    internal sealed class TestScoreServer : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Any, 0);
        private readonly Thread thread;
        private volatile bool stopped, saved;
        public volatile bool Qualified = true, Unavailable, LoseRankOnSave;
        public int Qualifications, Posts;
        public readonly ConcurrentQueue<string> Bodies = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<string> Headers = new ConcurrentQueue<string>();
        public readonly ManualResetEventSlim HoldQualification = new ManualResetEventSlim(true);
        public readonly string ServerId;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public TestScoreServer(string serverId = null)
        {
            ServerId = serverId ?? Guid.NewGuid().ToString();
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true }; thread.Start();
        }
        private void Serve()
        {
            while (!stopped)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 3000;
                        var header = new StringBuilder();
                        while (!header.ToString().EndsWith("\r\n\r\n"))
                        { int b = stream.ReadByte(); if (b < 0) throw new IOException(); header.Append((char)b); }
                        string headers = header.ToString();
                        Headers.Enqueue(headers);
                        string length = headers.Split('\n').FirstOrDefault(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                        byte[] bytes = new byte[length == null ? 0 : int.Parse(length.Split(':')[1].Trim())];
                        for (int i = 0; i < bytes.Length;) { int n = stream.Read(bytes, i, bytes.Length - i); if (n == 0) throw new IOException(); i += n; }
                        bool post = headers.StartsWith("POST"), qualification = headers.Contains("/qualification?");
                        if (qualification) { Interlocked.Increment(ref Qualifications); HoldQualification.Wait(6000); }
                        if (post) { Interlocked.Increment(ref Posts); Bodies.Enqueue(Encoding.UTF8.GetString(bytes)); }
                        bool fail = Unavailable;
                        if (post && !fail && !LoseRankOnSave) saved = true;
                        string difficulty = headers.Contains("difficulty=easy") ? "easy" : "normal";
                        string entries = "[" + string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"rank\":" + i + ",\"name\":\""
                            + (saved && i == 3 ? "新人" : difficulty == "easy" ? "イージーの人" : i == 1 ? "チャンピオン" : "プレイヤー" + i)
                            + "\",\"defeated\":" + (saved && i == 3 ? 88 : 100 - i) + "}")) + "]";
                        string body = "{\"serverId\":\"" + ServerId + "\",\"difficulty\":\"" + difficulty + "\",\"entries\":" + entries
                            + ",\"qualified\":" + (Qualified ? "true" : "false") + ",\"rank\":3,\"entry\":{\"name\":\"新人\",\"defeated\":88}"
                            + (post && LoseRankOnSave ? ",\"code\":\"NOT_QUALIFIED\"" : "") + "}";
                        int status = fail ? 503 : post && LoseRankOnSave ? 409 : 200;
                        if (fail) body = "{\"error\":\"unavailable\"}";
                        byte[] payload = Encoding.UTF8.GetBytes(body);
                        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Test\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + payload.Length + "\r\n\r\n");
                        stream.Write(response, 0, response.Length); stream.Write(payload, 0, payload.Length);
                    }
                }
                catch (Exception) { if (stopped) return; }
            }
        }
        public void Dispose()
        {
            if (stopped) return;
            stopped = true; HoldQualification.Set(); listener.Stop(); thread.Join(1000);
            HoldQualification.Dispose();
        }
    }
}
