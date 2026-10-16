using UnityEngine;
using UnityEngine.UI;

// Small vector clock, sharp at every CanvasScaler resolution.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SessionTimerClock : Graphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .34f;
        Vector2 center = rectTransform.rect.center;
        float width = Mathf.Max(1.5f, radius * .14f);
        const int segments = 48;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
            Line(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width);
        }
        Line(mesh, center, center + Vector2.up * radius * .65f, width);
        Line(mesh, center, center + new Vector2(.5f, -.3f) * radius, width);
        Line(mesh, center + new Vector2(-.85f, 1.05f) * radius, center + new Vector2(-.5f, 1.3f) * radius, width * 1.4f);
        Line(mesh, center + new Vector2(.85f, 1.05f) * radius, center + new Vector2(.5f, 1.3f) * radius, width * 1.4f);
    }

    private void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width)
    {
        Vector2 side = new Vector2(b.y - a.y, a.x - b.x).normalized * (width * .5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(a - side, color, Vector2.zero); mesh.AddVert(a + side, color, Vector2.zero);
        mesh.AddVert(b + side, color, Vector2.zero); mesh.AddVert(b - side, color, Vector2.zero);
        mesh.AddTriangle(start, start + 2, start + 1); mesh.AddTriangle(start, start + 3, start + 2);
    }
}
