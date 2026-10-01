using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Fixed-size, reusable geometry: no Instantiate/Destroy or per-frame allocations during combat.
public sealed class DuelEffects : MonoBehaviour
{
    private Material material;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private Ribbon[] ribbons;
    private Burst[] bursts;
    private int nextBurst;
    private static readonly Vector3[] BurstAxes = { Vector3.right, Vector3.up, Vector3.forward };
    private readonly System.Random random = new System.Random(37);
    public int ActiveBurstCount { get { int count = 0; foreach (Burst b in bursts) if (b.Active) count++; return count; } }
    public int TrailSampleCount => ribbons[0].Count + ribbons[1].Count;

    private sealed class Ribbon
    {
        public Transform Sword;
        public Color Color;
        public Mesh Mesh;
        public readonly Vector3[] Base = new Vector3[12], Tip = new Vector3[12];
        public readonly double[] Time = new double[12];
        public int Count;
        public readonly List<Vector3> Vertices = new List<Vector3>(24);
        public readonly List<Color> Colors = new List<Color>(24);
        public readonly List<int> Indices = new List<int>(66);
    }
    private sealed class Burst
    {
        public GameObject Object;
        public Mesh Mesh;
        public Vector3 Center;
        public double Start;
        public float Duration, Size;
        public Color Color;
        public bool Active, Dust;
        public readonly Vector3[] Directions = new Vector3[20];
        public readonly List<Vector3> Vertices = new List<Vector3>(120);
        public readonly List<Color> Colors = new List<Color>(120);
        public readonly List<int> Indices = new List<int>(120);
    }

    public void Initialize(Transform firstSword, Transform secondSword)
    {
        material = new Material(Resources.Load<Shader>("CombatEffects"));
        ribbons = new Ribbon[2];
        for (int i = 0; i < 2; i++)
            ribbons[i] = new Ribbon { Sword = i == 0 ? firstSword : secondSword,
                Color = i == 0 ? new Color(1f, 0.25f, 0.15f) : new Color(0.12f, 0.65f, 1f), Mesh = CreateMesh("Sword ribbon " + i, out _) };
        bursts = new Burst[12];
        for (int i = 0; i < bursts.Length; i++)
        {
            Mesh mesh = CreateMesh("Contact burst " + i, out GameObject go);
            bursts[i] = new Burst { Mesh = mesh, Object = go };
            go.SetActive(false);
        }
    }

    private Mesh CreateMesh(string name, out GameObject go)
    {
        go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        // Geometry is already in world coordinates, independent of the moving combat origin.
        go.transform.SetParent(null, true);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var mesh = new Mesh { name = name }; mesh.MarkDynamic(); meshes.Add(mesh);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        ownedObjects.Add(go);
        return mesh;
    }
    private readonly List<GameObject> ownedObjects = new List<GameObject>();

    public void Emit(Vector3 position, Color color, double now, bool dust = false, float size = 1f)
    {
        Burst b = bursts[nextBurst++ % bursts.Length];
        b.Active = true; b.Object.SetActive(true); b.Center = position; b.Color = color; b.Start = now;
        b.Duration = dust ? 0.3f : 0.24f; b.Size = size; b.Dust = dust;
        for (int i = 0; i < b.Directions.Length; i++)
        {
            Vector3 d = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f).normalized;
            if (dust) d.y = Mathf.Abs(d.y) * 0.3f;
            b.Directions[i] = d;
        }
        DrawBurst(b, 0f);
    }

    public void Tick(double now, bool frozen)
    {
        foreach (Ribbon r in ribbons)
        {
            if (!frozen)
            {
                while (r.Count > 0 && now - r.Time[0] > 0.12)
                {
                    for (int i = 1; i < r.Count; i++) { r.Base[i - 1] = r.Base[i]; r.Tip[i - 1] = r.Tip[i]; r.Time[i - 1] = r.Time[i]; }
                    r.Count--;
                }
                Vector3 tip = r.Sword.position + r.Sword.forward * 2.1f;
                bool moved = r.Count == 0 || Vector3.Distance(tip, r.Tip[r.Count - 1]) > 0.025f;
                if (moved)
                {
                    if (r.Count == r.Base.Length)
                    {
                        for (int i = 1; i < r.Count; i++) { r.Base[i - 1] = r.Base[i]; r.Tip[i - 1] = r.Tip[i]; r.Time[i - 1] = r.Time[i]; }
                        r.Count--;
                    }
                    r.Base[r.Count] = r.Sword.position + r.Sword.forward * 0.55f;
                    r.Tip[r.Count] = tip; r.Time[r.Count++] = now;
                }
                DrawRibbon(r, now);
            }
        }
        foreach (Burst b in bursts)
        {
            if (!b.Active) continue;
            float age = (float)(now - b.Start);
            if (age >= b.Duration) { b.Active = false; b.Object.SetActive(false); continue; }
            DrawBurst(b, age);
        }
    }

    private static void DrawRibbon(Ribbon r, double now)
    {
        r.Mesh.Clear(); r.Vertices.Clear(); r.Colors.Clear(); r.Indices.Clear();
        for (int i = 0; i < r.Count; i++)
        {
            r.Vertices.Add(r.Base[i]); r.Vertices.Add(r.Tip[i]);
            Color c = r.Color; c.a = Mathf.Clamp01(1f - (float)(now - r.Time[i]) / 0.12f) * 0.42f;
            r.Colors.Add(new Color(c.r, c.g, c.b, c.a * 0.25f)); r.Colors.Add(c);
            if (i == 0) continue;
            int v = i * 2; r.Indices.Add(v - 2); r.Indices.Add(v - 1); r.Indices.Add(v);
            r.Indices.Add(v - 1); r.Indices.Add(v + 1); r.Indices.Add(v);
        }
        r.Mesh.SetVertices(r.Vertices); r.Mesh.SetColors(r.Colors); r.Mesh.SetTriangles(r.Indices, 0); r.Mesh.RecalculateBounds();
    }

    private static void DrawBurst(Burst b, float age)
    {
        b.Mesh.Clear(); b.Vertices.Clear(); b.Colors.Clear(); b.Indices.Clear();
        float t = age / b.Duration;
        for (int i = 0; i < b.Directions.Length; i++)
        {
            Vector3 d = b.Directions[i];
            Vector3 side = Vector3.Cross(d, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            float spread = (b.Dust ? 0.3f : 1.1f) * b.Size;
            Vector3 end = b.Center + d * (0.07f + age * 3f) * spread - Vector3.up * age * age * (b.Dust ? 0f : 2f);
            Vector3 start = end - d * (b.Dust ? 0.08f : 0.23f) * (1f - t) * b.Size;
            float width = (b.Dust ? 0.055f : 0.018f) * (1f - t) * b.Size;
            Color c = b.Color; c.a *= 1f - t;
            int v = b.Vertices.Count;
            b.Vertices.Add(start - side * width); b.Vertices.Add(start + side * width); b.Vertices.Add(end);
            b.Colors.Add(c); b.Colors.Add(c); b.Colors.Add(c); b.Indices.Add(v); b.Indices.Add(v + 1); b.Indices.Add(v + 2);
        }
        // Three crossed white diamonds remain visible from both split-screen views.
        if (!b.Dust && t < 0.4f)
        {
            float size = 0.2f * (1f - t / 0.4f) * b.Size;
            for (int i = 0; i < 3; i++)
            {
                Vector3 a = BurstAxes[i] * size, c = BurstAxes[(i + 1) % 3] * size;
                int v = b.Vertices.Count;
                b.Vertices.Add(b.Center + a); b.Vertices.Add(b.Center + c); b.Vertices.Add(b.Center - a); b.Vertices.Add(b.Center - c);
                for (int j = 0; j < 4; j++) b.Colors.Add(Color.white);
                b.Indices.Add(v); b.Indices.Add(v + 1); b.Indices.Add(v + 2); b.Indices.Add(v); b.Indices.Add(v + 2); b.Indices.Add(v + 3);
            }
        }
        b.Mesh.SetVertices(b.Vertices); b.Mesh.SetColors(b.Colors); b.Mesh.SetTriangles(b.Indices, 0); b.Mesh.RecalculateBounds();
    }

    public void ClearTrails() { if (ribbons == null) return; foreach (Ribbon r in ribbons) { r.Count = 0; r.Mesh.Clear(); } }
    public void ResetEffects()
    {
        ClearTrails(); nextBurst = 0;
        if (bursts == null) return;
        foreach (Burst b in bursts) { b.Active = false; b.Object.SetActive(false); b.Mesh.Clear(); }
    }
    private void OnDestroy()
    {
        foreach (GameObject go in ownedObjects) if (go != null) Destroy(go);
        foreach (Mesh mesh in meshes) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
