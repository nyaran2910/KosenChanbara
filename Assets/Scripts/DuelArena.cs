using System.Collections.Generic;
using UnityEngine;

public sealed class DuelArena : MonoBehaviour
{
    private readonly List<Material> materials = new List<Material>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private GameObject platform;
    private SpriteRenderer source;
    private GameObject environment;
    private GameObject background;
    private Bounds fieldBounds;
    private bool hasFieldBounds;

    public bool TryGetFieldBoundary(out Vector2 center, out float radius)
    {
        center = new Vector2(fieldBounds.center.x, fieldBounds.center.z);
        radius = Mathf.Min(fieldBounds.extents.x, fieldBounds.extents.z);
        return hasFieldBounds && radius > 0f;
    }

    public void Initialize(SpriteRenderer arena)
    {
        source = arena;
        Bounds b = arena.bounds;
        float radius = Mathf.Min(b.extents.x, b.extents.z);
        platform = new GameObject("Duel platform");
        platform.transform.position = new Vector3(b.center.x, 0f, b.center.z);
        CreateRing("Floor", 0f, radius, 0f, new Color(0.88f, 0.89f, 0.84f));
        CreateRing("Orange edge", radius - 0.18f, radius, 0.006f, new Color(1f, 0.44f, 0.08f));
        CreateRing("Inner boundary", radius - 0.5f, radius - 0.47f, 0.007f, new Color(0.68f, 0.7f, 0.65f));
        var side = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        side.name = "Platform rim"; side.transform.SetParent(platform.transform, false);
        // Keep the cylinder cap below the floor mesh to avoid coplanar flicker.
        side.transform.localPosition = Vector3.down * 0.165f;
        side.transform.localScale = new Vector3(radius * 2f, 0.16f, radius * 2f);
        Destroy(side.GetComponent<Collider>());
        side.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.36f, 0.22f, 0.12f));
        arena.enabled = false;
    }
    public void SetStage(GameObject prefab, bool ringOutEnabled = true, bool baseballField = false, bool replacePlatform = false,
        GameObject backgroundPrefab = null)
    {
        if (environment != null) { environment.SetActive(false); Destroy(environment); }
        if (background != null) { background.SetActive(false); Destroy(background); }
        background = null;
        hasFieldBounds = false;
        platform.SetActive(ringOutEnabled && (!replacePlatform || prefab == null));
        environment = prefab == null ? null : Instantiate(prefab);
        if (environment != null)
        {
            environment.name = "Duel stage";
            // The imported platform and field place their playable floor at y = 0.
            environment.transform.position = platform.transform.position
                + (ringOutEnabled && !replacePlatform ? Vector3.down * 0.35f : Vector3.zero);
            if(baseballField)environment.AddComponent<BaseballField>().Initialize(environment);
            if (!ringOutEnabled)
                foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>())
                    if (renderer.name == "Infield_concrete_foundation")
                    {
                        fieldBounds = renderer.bounds;
                        hasFieldBounds = true;
                        break;
                    }
        }
        if (ringOutEnabled && backgroundPrefab != null)
        {
            background = Instantiate(backgroundPrefab);
            background.name = "Stadium background";
            float floor = platform.transform.position.y - .35f;
            if (environment != null)
                foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>())
                    floor = Mathf.Min(floor, renderer.bounds.min.y);
            // Stand the platform's supplied support posts on the stadium grass.
            // The fighting floor and ring-out coordinates remain at y = 0.
            background.transform.position = new Vector3(platform.transform.position.x, floor, platform.transform.position.z);
        }
    }
    private Material CreateMaterial(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = color; m.SetFloat("_Smoothness", 0.15f); materials.Add(m); return m;
    }
    private void CreateRing(string name, float inner, float outer, float height, Color color)
    {
        const int segments = 96;
        var vertices = new Vector3[(segments + 1) * 2]; var triangles = new int[segments * 6]; var normals = new Vector3[vertices.Length];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[2 * i] = d * inner + Vector3.up * height; vertices[2 * i + 1] = d * outer + Vector3.up * height;
            normals[2 * i] = normals[2 * i + 1] = Vector3.up;
            if (i == segments) continue;
            int v = 2 * i, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles, normals = normals }; mesh.RecalculateBounds(); meshes.Add(mesh);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(platform.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial(color);
    }
    private void OnDestroy()
    {
        if (source != null) source.enabled = true;
        if (platform != null) Destroy(platform);
        if (environment != null) Destroy(environment);
        if (background != null) Destroy(background);
        foreach (Mesh mesh in meshes) Destroy(mesh);
        foreach (Material material in materials) Destroy(material);
    }
}
