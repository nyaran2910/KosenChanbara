using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class ScoreEntry
{
    public string runId, name, createdAt;
    public int rank, defeated;
}

[Serializable]
public sealed class ScoreboardReply
{
    public string difficulty, code, error, serverId;
    public ScoreEntry[] entries;
    public bool qualified;
    public int rank;
    public ScoreEntry entry;
}

[Serializable]
public sealed class ScoreSubmission
{
    public string runId, name, difficulty, serverId;
    public int defeated;
}

// Only the HTTP service opens the SQLite database. Every Unity instance uses
// the same API, including the Unity instance running on the server PC.
public sealed class ScoreboardClient : IDisposable
{
    private readonly HashSet<UnityWebRequest> requests = new HashSet<UnityWebRequest>();
    private readonly ScoreboardDiscovery discovery;
    private ScoreServerEndpoint cachedEndpoint;
    private bool disposed;
    public ScoreboardClient(int discoveryPort = 0) { discovery = new ScoreboardDiscovery(discoveryPort); }

    public IEnumerator Load(string difficulty, Action<ScoreboardReply, string> completed)
        => Request("/api/scores?difficulty=" + difficulty, null, difficulty, null, completed);

    public IEnumerator Qualify(string difficulty, int defeated, Action<ScoreboardReply, string> completed)
        => Request("/api/scores/qualification?difficulty=" + difficulty + "&defeated=" + defeated,
            null, difficulty, null, completed);

    public IEnumerator Submit(ScoreSubmission score, Action<ScoreboardReply, string> completed)
        => Request("/api/scores", JsonUtility.ToJson(score), score.difficulty, score.serverId, completed);

    private IEnumerator Request(string route, string body, string difficulty, string requiredServerId, Action<ScoreboardReply, string> completed)
    {
        for (int attempt = 0; attempt < 2 && !disposed; attempt++)
        {
            ScoreServerEndpoint endpoint = cachedEndpoint;
            if (endpoint == null || (!string.IsNullOrEmpty(requiredServerId) && endpoint.ServerId != requiredServerId))
            {
                string discoveryError = null;
                yield return discovery.Find(requiredServerId, (found, error) => { endpoint = found; discoveryError = error; });
                if (disposed) yield break;
                if (endpoint == null) { completed(null, discoveryError); yield break; }
                cachedEndpoint = endpoint;
            }
            ScoreboardReply reply = null;
            yield return Send(endpoint, route, body, difficulty, result => reply = result);
            if (disposed) yield break;
            if (reply != null) { completed(reply, null); yield break; }
            if (cachedEndpoint == endpoint) cachedEndpoint = null;
        }
        if (!disposed) completed(null, "スコアサーバーに接続できません。接続を確認して再試行してください");
    }

    private IEnumerator Send(ScoreServerEndpoint endpoint, string route, string body, string difficulty, Action<ScoreboardReply> completed)
    {
        if (disposed) yield break;
        UnityWebRequest request = null;
        bool setupFailed = false;
        try
        {
            request = new UnityWebRequest(endpoint.Url + route, body == null ? "GET" : "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 4;
            if (!string.IsNullOrEmpty(endpoint.ServerId)) request.SetRequestHeader("X-Kosen-Score-Server", endpoint.ServerId);
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
        }
        catch (Exception) { setupFailed = true; }
        if (setupFailed)
        {
            request?.Dispose(); completed(null); yield break;
        }
        requests.Add(request);
        try
        {
            UnityWebRequestAsyncOperation operation = null;
            try { operation = request.SendWebRequest(); }
            catch (InvalidOperationException) { }
            if (operation == null) { completed(null); yield break; }
            yield return operation;
            if (disposed) yield break;
            ScoreboardReply reply = null;
            if (!string.IsNullOrEmpty(request.downloadHandler.text))
            {
                try { reply = JsonUtility.FromJson<ScoreboardReply>(request.downloadHandler.text); }
                catch (ArgumentException) { }
            }
            bool knownReply = reply != null && reply.difficulty == difficulty && reply.entries != null
                && Guid.TryParse(reply.serverId, out _)
                && (string.IsNullOrEmpty(endpoint.ServerId) || endpoint.ServerId == reply.serverId);
            bool lostRank = request.responseCode == 409 && knownReply && reply.code == "NOT_QUALIFIED";
            if (knownReply && (body == null || reply.entry != null || lostRank)
                && (request.result == UnityWebRequest.Result.Success || lostRank)) completed(reply);
            else completed(null);
        }
        finally { if (requests.Remove(request)) request.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; discovery.Dispose(); cachedEndpoint = null;
        foreach (UnityWebRequest request in requests) { request.Abort(); request.Dispose(); }
        requests.Clear();
    }
}
