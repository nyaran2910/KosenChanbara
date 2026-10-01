using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Materials are swapped only for the camera whose player owns this body.
// Restore at camera end so the other viewport and scene cameras see an opaque opponent.
public sealed class DuelView
{
    private readonly Camera camera;
    private readonly Vector3 basePosition;
    private readonly Quaternion baseRotation;
    private readonly float baseFov;
    private double shakeStart = -100;
    private float strength;
    private readonly Renderer[] body;
    private readonly Material[][] opaque, transparent;
    private readonly List<Material> created = new List<Material>();
    private readonly MaterialPropertyBlock[] saved;
    private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private bool swapped;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public DuelView(Transform player, Transform sword, Renderer[] renderers)
    {
        camera = player.GetComponentInChildren<Camera>(); body = renderers;
        if (camera != null)
        {
            camera.transform.localPosition = new Vector3(0.35f, 1.6f, -3.4f);
            camera.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.35f, -0.6f, 6.4f));
            camera.nearClipPlane = 0.1f;
            basePosition = camera.transform.localPosition; baseRotation = camera.transform.localRotation; baseFov = camera.fieldOfView;
        }
        opaque = new Material[body.Length][]; transparent = new Material[body.Length][]; saved = new MaterialPropertyBlock[body.Length];
        var cache = new Dictionary<Material, Material>();
        Material template = Resources.Load<Material>("OwnBodyFade");
        for (int i = 0; i < body.Length; i++)
        {
            opaque[i] = body[i].sharedMaterials; transparent[i] = new Material[opaque[i].Length]; saved[i] = new MaterialPropertyBlock();
            for (int j = 0; j < opaque[i].Length; j++)
            {
                Material original = opaque[i][j];
                if (!cache.TryGetValue(original, out Material fade))
                {
                    fade = new Material(template != null ? template : original) { name = original.name + " (own view)" };
                    fade.CopyPropertiesFromMaterial(original);
                    ConfigureFade(fade);
                    Color c = original.color; c.a = 0.3f; fade.color = c;
                    cache.Add(original, fade); created.Add(fade);
                }
                transparent[i][j] = fade;
            }
        }
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    public static void ConfigureFade(Material material)
    {
        material.renderQueue = 3000;
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetShaderPassEnabled("ShadowCaster", true);
    }

    private void BeginCamera(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering != camera || swapped) return;
        swapped = true;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == null) continue;
            body[i].GetPropertyBlock(saved[i]); body[i].sharedMaterials = transparent[i];
            body[i].GetPropertyBlock(block);
            // With no flash, keep each material's original color (e.g. face and uniform).
            if (block.HasColor(BaseColor)) { Color c = block.GetColor(BaseColor); c.a = 0.3f; block.SetColor(BaseColor, c); }
            body[i].SetPropertyBlock(block);
        }
    }
    private void EndCamera(ScriptableRenderContext context, Camera rendering) { if (rendering == camera) Restore(); }
    private void Restore()
    {
        if (!swapped) return;
        for (int i = 0; i < body.Length; i++)
            if (body[i] != null) { body[i].sharedMaterials = opaque[i]; body[i].SetPropertyBlock(saved[i]); }
        swapped = false;
    }
    public void Kick(double now, float amount) { strength = Mathf.Min(1f, amount + (now - shakeStart < 0.12 ? strength * 0.3f : 0f)); shakeStart = now; }
    public void Tick(double now)
    {
        if (camera == null) return;
        float t = Mathf.Clamp01((float)(now - shakeStart) / 0.12f);
        float decay = (1f - t) * (1f - t) * strength;
        float oscillation = Mathf.Sin(t * Mathf.PI * 5f);
        camera.transform.localPosition = basePosition + new Vector3(oscillation, Mathf.Sin(t * Mathf.PI * 7f) * 0.65f, 0f) * (0.045f * decay);
        camera.transform.localRotation = baseRotation * Quaternion.Euler(oscillation * decay * 0.5f, 0f, oscillation * decay * 1.1f);
        camera.fieldOfView = baseFov + decay * 2f;
    }
    public void Reset() { shakeStart = -100; strength = 0f; Tick(0); Restore(); }
    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera; RenderPipelineManager.endCameraRendering -= EndCamera;
        Reset(); foreach (Material material in created) Object.Destroy(material);
    }
}
